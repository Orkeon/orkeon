using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Administration;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Administration;

/// <summary>
/// The operator's side: listing accounts, signing OAuth accounts in (device code and loopback
/// PKCE, over a real loopback listener and a scripted identity provider), signing out, checking.
/// </summary>
public sealed class EmailAccountAdministrationTests
{
    private const string GoogleSecretVariable = "ORKEON_TEST_GOOGLE_SECRET";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_list_every_account_with_its_readiness_and_the_default()
    {
        var options = new EmailToolsOptions { DefaultAccount = "perso" };
        options.Accounts["perso"] = TestAccounts.Gmail(EmailRights.Read | EmailRights.Draft);
        options.Accounts["hotmail"] = TestAccounts.Outlook(EmailRights.Read | EmailRights.Send);
        options.Accounts["broken"] = TestAccounts.Custom(EmailRights.None);
        using var fixture = new AdministrationFixture(options);

        var statuses = await fixture.Administration.ListAsync(Token);

        Assert.Equal(["broken", "hotmail", "perso"], statuses.Select(s => s.Name));
        var broken = statuses[0];
        Assert.Equal(("?", false, false), (broken.Provider, broken.Ready, broken.IsDefault));
        Assert.StartsWith("Rights is required", broken.Problem, StringComparison.Ordinal);
        var hotmail = statuses[1];
        Assert.Equal(("someone@outlook.com", "Outlook", "Graph", "Graph", "Read, Send", "OAuth2"),
            (hotmail.Address, hotmail.Provider, hotmail.Reads, hotmail.Sends, hotmail.Rights, hotmail.Auth));
        Assert.False(hotmail.Ready);
        Assert.Equal("E-mail account 'hotmail' needs an OAuth sign-in: run `orkeon email login hotmail` in a terminal.", hotmail.Problem);
        var perso = statuses[2];
        Assert.Equal(("Imap", "Smtp", "Password", true, true, (string?)null),
            (perso.Reads, perso.Sends, perso.Auth, perso.Ready, perso.IsDefault, perso.Problem));
    }

    [Fact]
    public async Task Should_call_an_account_ready_When_its_password_is_only_in_the_user_scope()
    {
        // `orkeon email accounts` reads through the seam the run reads through: a password Studio
        // stored makes the account ready for the verb as it is for the run.
        using var fixture = new AdministrationFixture(
            Accounts(("perso", TestAccounts.Gmail())), TestAccounts.UserScope((TestAccounts.PasswordVariable, TestAccounts.Password)));
        using var unset = new AdministrationFixture(Accounts(("perso", TestAccounts.Gmail())), TestAccounts.UserScope());

        var ready = Assert.Single(await fixture.Administration.ListAsync(Token));
        var notReady = Assert.Single(await unset.Administration.ListAsync(Token));

        Assert.Equal((true, (string?)null), (ready.Ready, ready.Problem));
        Assert.False(notReady.Ready);
        Assert.EndsWith("which is set neither in the process environment nor in the user's.", notReady.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_sign_in_with_the_device_code_flow_and_store_the_tokens()
    {
        using var fixture = new AdministrationFixture(Accounts(("hotmail", TestAccounts.Outlook())));
        fixture.Credentials.Handler.EnqueueJson("""{"device_code":"dev-1","user_code":"WDJB-MJHT","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":5}""");
        fixture.Credentials.Handler.EnqueueJson("""{"error":"authorization_pending"}""");
        fixture.Credentials.Handler.EnqueueJson("""{"access_token":"at-1","refresh_token":"rt-1","expires_in":3600}""");
        var interaction = new FakeLoginInteraction();

        await fixture.Administration.LoginAsync("hotmail", interaction, Token);

        Assert.Equal(("hotmail", new Uri("https://microsoft.com/devicelogin"), "WDJB-MJHT"), interaction.DeviceCode);
        var stored = Assert.Single(fixture.Credentials.Store.Tokens);
        Assert.Equal(EmailCredentialProvider.TokenKey(fixture.Resolve("hotmail")), stored.Key);
        Assert.Equal(("at-1", "rt-1"), (stored.Value.AccessToken, stored.Value.RefreshToken));
        Assert.Equal(3, fixture.Credentials.Handler.Requests.Count);
    }

    [Fact]
    public async Task Should_say_how_long_the_device_code_lives_as_the_provider_granted_it()
    {
        // What a screen counts down from (STUDIO-70): the provider's own figure, not the default.
        using var fixture = new AdministrationFixture(Accounts(("hotmail", TestAccounts.Outlook())));
        fixture.Credentials.Handler.EnqueueJson("""{"device_code":"dev-1","user_code":"WDJB-MJHT","verification_uri":"https://microsoft.com/devicelogin","expires_in":600,"interval":5}""");
        fixture.Credentials.Handler.EnqueueJson("""{"access_token":"at-1","refresh_token":"rt-1","expires_in":3600}""");
        var interaction = new FakeLoginInteraction();

        await fixture.Administration.LoginAsync("hotmail", interaction, Token);

        Assert.Equal(TimeSpan.FromMinutes(10), interaction.DeviceCodeExpiresIn);
    }

    [Fact]
    public async Task Should_sign_in_through_the_browser_redirect_with_PKCE_and_the_client_secret()
    {
        using var fixture = new AdministrationFixture(Accounts(("google", TestAccounts.GmailOAuth())), (GoogleSecretVariable, "GOCSPX-secret"));
        fixture.Credentials.Handler.EnqueueJson("""{"access_token":"ya29.at","refresh_token":"1//rt","expires_in":3599}""");
        var interaction = new FakeLoginInteraction();

        var login = fixture.Administration.LoginAsync("google", interaction, Token);
        var authorization = await interaction.AuthorizationShown.WaitAsync(Token);
        var query = Query(authorization);
        var redirect = new Uri(query["redirect_uri"]);
        var browser = await BrowseAsync(redirect, $"GET /?state={Uri.EscapeDataString(query["state"])}&code=4%2F0AbCd&scope=mail HTTP/1.1");
        await login;

        Assert.StartsWith("HTTP/1.1 200 OK", browser, StringComparison.Ordinal);
        Assert.Equal("google", interaction.AuthorizationAccount);
        Assert.Equal("someone@gmail.com", query["login_hint"]);
        var form = Assert.Single(fixture.Credentials.Handler.Requests).Form;
        Assert.Equal("4/0AbCd", form["code"]);
        Assert.Equal(redirect.AbsoluteUri, form["redirect_uri"]);
        Assert.Equal("GOCSPX-secret", form["client_secret"]);
        Assert.Equal(query["code_challenge"], Challenge(form["code_verifier"]));
        Assert.Equal("1//rt", Assert.Single(fixture.Credentials.Store.Tokens).Value.RefreshToken);
    }

    [Fact]
    public async Task Should_finish_on_the_browser_redirect_While_the_terminal_blocks_on_its_read()
    {
        using var fixture = new AdministrationFixture(Accounts(("google", TestAccounts.GmailOAuth())), (GoogleSecretVariable, "GOCSPX-secret"));
        fixture.Credentials.Handler.EnqueueJson("""{"access_token":"ya29.at","refresh_token":"1//rt","expires_in":3599}""");
        using var terminal = new StubTerminalLoginInteraction();

        // Started on a thread of its own: before the fix, the blocked read held the caller too.
        var login = Task.Run(() => fixture.Administration.LoginAsync("google", terminal, Token), Token);
        var query = Query(await terminal.AuthorizationShown.WaitAsync(Token));
        await BrowseAsync(new Uri(query["redirect_uri"]), $"GET /?state={Uri.EscapeDataString(query["state"])}&code=4%2F0AbCd HTTP/1.1");
        await login.WaitAsync(TimeSpan.FromSeconds(30), Token);

        Assert.Equal("1//rt", Assert.Single(fixture.Credentials.Store.Tokens).Value.RefreshToken);
    }

    [Fact]
    public async Task Should_accept_a_pasted_redirect_When_the_browser_cannot_reach_the_listener()
    {
        using var fixture = new AdministrationFixture(Accounts(("google", TestAccounts.GmailOAuth())), (GoogleSecretVariable, "GOCSPX-secret"));
        fixture.Credentials.Handler.EnqueueJson("""{"access_token":"ya29.at","refresh_token":"1//rt","expires_in":3599}""");
        var interaction = new FakeLoginInteraction()
            .Paste(_ => "   ")
            .Paste(_ => "not a url at all")
            .Paste(_ => "http://localhost:1/?scope=only")
            .Paste(shown => $"http://localhost:1/?code=pasted-code&state={Uri.EscapeDataString(Query(shown)["state"])}");

        await fixture.Administration.LoginAsync("google", interaction, Token);

        Assert.Equal(4, interaction.PasteReads);
        var form = Assert.Single(fixture.Credentials.Handler.Requests).Form;
        Assert.Equal("pasted-code", form["code"]);
        Assert.StartsWith("http://127.0.0.1:", form["redirect_uri"], StringComparison.Ordinal);
        Assert.Single(fixture.Credentials.Store.Tokens);
    }

    [Fact]
    public async Task Should_refuse_a_redirect_whose_state_is_not_the_one_it_issued()
    {
        using var fixture = new AdministrationFixture(Accounts(("google", TestAccounts.GmailOAuth())), (GoogleSecretVariable, "GOCSPX-secret"));
        var interaction = new FakeLoginInteraction().Paste(_ => "http://127.0.0.1:1/?code=stolen&state=forged");

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Administration.LoginAsync("google", interaction, Token));

        Assert.Equal(EmailErrorCode.AuthenticationFailed, error.Code);
        Assert.Equal("The redirect did not come from this sign-in (state mismatch); run the login again.", error.Message);
        Assert.Empty(fixture.Credentials.Handler.Requests);
        Assert.Empty(fixture.Credentials.Store.Tokens);
    }

    [Fact]
    public async Task Should_not_take_a_refusal_that_carries_another_sign_in_s_state()
    {
        using var fixture = new AdministrationFixture(Accounts(("google", TestAccounts.GmailOAuth())), (GoogleSecretVariable, "GOCSPX-secret"));
        var interaction = new FakeLoginInteraction().Paste(_ => "http://127.0.0.1:1/?error=access_denied&state=from-an-earlier-attempt");

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Administration.LoginAsync("google", interaction, Token));

        Assert.Equal(EmailErrorCode.AuthenticationFailed, error.Code);
        Assert.StartsWith("The redirect did not come from this sign-in (state mismatch)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_report_a_sign_in_the_user_refused()
    {
        using var fixture = new AdministrationFixture(Accounts(("google", TestAccounts.GmailOAuth())), (GoogleSecretVariable, "GOCSPX-secret"));
        var interaction = new FakeLoginInteraction().Paste(shown => $"http://127.0.0.1:1/?error=access_denied&state={Uri.EscapeDataString(Query(shown)["state"])}");

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Administration.LoginAsync("google", interaction, Token));

        Assert.Equal(EmailErrorCode.LoginRequired, error.Code);
        Assert.Equal("The sign-in was refused: access_denied.", error.Message);
    }

    [Fact]
    public async Task Should_refuse_a_sign_in_that_brings_no_refresh_token()
    {
        using var fixture = new AdministrationFixture(Accounts(("hotmail", TestAccounts.Outlook())));
        fixture.Credentials.Handler.EnqueueJson("""{"device_code":"d","user_code":"u","verification_uri":"https://microsoft.com/devicelogin"}""");
        fixture.Credentials.Handler.EnqueueJson("""{"access_token":"short-lived","expires_in":3600}""");

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await fixture.Administration.LoginAsync("hotmail", new FakeLoginInteraction(), Token));

        Assert.Equal(EmailErrorCode.LoginRequired, error.Code);
        Assert.StartsWith("The provider issued no refresh token", error.Message, StringComparison.Ordinal);
        Assert.Contains("offline_access", error.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Credentials.Store.Tokens);
    }

    [Fact]
    public async Task Should_find_nothing_to_sign_in_to_for_a_password_account()
    {
        using var fixture = new AdministrationFixture(Accounts(("perso", TestAccounts.Gmail())));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await fixture.Administration.LoginAsync("perso", new FakeLoginInteraction(), Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.Equal("E-mail account 'perso' signs in with a password (Auth:PasswordEnvVar); there is nothing to log in to.", error.Message);
    }

    [Fact]
    public async Task Should_ask_for_the_client_secret_before_opening_any_sign_in()
    {
        using var fixture = new AdministrationFixture(Accounts(("google", TestAccounts.GmailOAuth())));
        var interaction = new FakeLoginInteraction();

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Administration.LoginAsync("google", interaction, Token));

        Assert.Equal(EmailErrorCode.CredentialMissing, error.Code);
        Assert.False(interaction.AuthorizationShown.IsCompleted);
        Assert.Empty(fixture.Credentials.Handler.Requests);
    }

    [Fact]
    public async Task Should_forget_the_tokens_on_logout_and_say_when_there_were_none()
    {
        using var fixture = new AdministrationFixture(Accounts(("hotmail", TestAccounts.Outlook())));
        fixture.Credentials.SeedFreshToken(fixture.Resolve("hotmail"));

        Assert.True(await fixture.Administration.LogoutAsync("hotmail", Token));
        Assert.False(await fixture.Administration.LogoutAsync("hotmail", Token));
        Assert.Empty(fixture.Credentials.Store.Tokens);
    }

    [Fact]
    public async Task Should_find_no_tokens_to_forget_for_a_password_account()
    {
        using var fixture = new AdministrationFixture(Accounts(("perso", TestAccounts.Custom())));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Administration.LogoutAsync("perso", Token));

        Assert.Equal(EmailErrorCode.InvalidRequest, error.Code);
        Assert.Equal("E-mail account 'perso' signs in with a password (Auth:PasswordEnvVar); there are no tokens to forget.", error.Message);
    }

    [Fact]
    public async Task Should_check_an_account_by_listing_its_folders()
    {
        using var fixture = new AdministrationFixture(Accounts(("perso", TestAccounts.Gmail())));
        fixture.Mailboxes.MailboxOf("perso").Folders.Add(new MailFolderInfo("Archive", "Archive", FolderRoles.Archive, 10, 0));

        var check = await fixture.Administration.CheckAsync("perso", Token);

        Assert.Equal(new EmailCheckResult { Account = "perso", Folders = 2, InboxTotal = 3, InboxUnread = 1 }, check);
    }

    private static EmailToolsOptions Accounts(params (string Name, EmailAccountOptions Options)[] accounts)
    {
        var options = new EmailToolsOptions();
        foreach (var (name, account) in accounts)
            options.Accounts[name] = account;
        return options;
    }

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<string> BrowseAsync(Uri redirect, string requestLine)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", redirect.Port, Token);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(requestLine + "\r\nHost: 127.0.0.1\r\n\r\n"), Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(Token);
    }

    /// <summary>The administration over the real registry, credential provider and OAuth client, with fakes underneath.</summary>
    private sealed class AdministrationFixture : IDisposable
    {
        private readonly EmailAccountRegistry _registry;

        public AdministrationFixture(EmailToolsOptions options, params (string Name, string Value)[] variables)
            : this(options, TestAccounts.Environment([(TestAccounts.PasswordVariable, TestAccounts.Password), .. variables]))
        {
        }

        public AdministrationFixture(EmailToolsOptions options, EmailEnvironment environment)
        {
            Credentials = new CredentialsFixture(environment);
            _registry = new EmailAccountRegistry(Microsoft.Extensions.Options.Options.Create(options));
            Administration = new EmailAccountAdministration(_registry, Credentials.Provider, Credentials.OAuth, Mailboxes);
        }

        public CredentialsFixture Credentials { get; }

        public FakeMailboxProvider Mailboxes { get; } = new();

        public EmailAccountAdministration Administration { get; }

        public ResolvedEmailAccount Resolve(string name) => _registry.Resolve(name);

        public void Dispose() => Credentials.Dispose();
    }
}
