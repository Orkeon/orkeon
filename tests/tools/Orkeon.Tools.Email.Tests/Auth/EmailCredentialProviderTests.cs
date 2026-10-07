using System.Net;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Auth;

/// <summary>Passwords from named variables, OAuth tokens from the store, refreshed and never signed in interactively.</summary>
public sealed class EmailCredentialProviderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ResolvedEmailAccount PasswordAccount => TestAccounts.Resolve("perso", TestAccounts.Gmail());

    private static ResolvedEmailAccount OutlookAccount => TestAccounts.Resolve("hotmail", TestAccounts.Outlook());

    private static ResolvedEmailAccount GoogleAccount => TestAccounts.Resolve("google", TestAccounts.GmailOAuth());

    [Fact]
    public async Task Should_read_the_password_from_the_named_environment_variable()
    {
        using var fixture = new CredentialsFixture();

        var credential = await fixture.Provider.GetAsync(PasswordAccount, Token);

        Assert.Equal(new PasswordCredential("someone@gmail.com", TestAccounts.Password), credential);
    }

    [Fact]
    public async Task Should_name_the_variable_When_the_password_is_not_set()
    {
        using var fixture = new CredentialsFixture(TestAccounts.Environment());

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Provider.GetAsync(PasswordAccount, Token));

        Assert.Equal(EmailErrorCode.CredentialMissing, error.Code);
        Assert.Equal(
            $"The password of e-mail account 'perso' is read from the environment variable {TestAccounts.PasswordVariable}, which is not set.",
            error.Message);
    }

    [Fact]
    public async Task Should_find_the_password_in_the_user_scope_When_the_process_does_not_hold_it_and_copy_nothing_into_the_process()
    {
        // Studio remembers a password in the user scope (HKCU\Environment): a terminal opened
        // before it, or a scheduled task, has no copy in its environment block. The run reads the
        // user scope and leaves the process alone — what it starts must not inherit the secret.
        var name = "EMAIL_TEST_" + Guid.NewGuid().ToString("N");
        var declared = TestAccounts.Gmail();
        declared.Auth.PasswordEnvVar = name;
        var account = TestAccounts.Resolve("perso", declared);
        using var fixture = new CredentialsFixture(new EmailEnvironment(
            Environment.GetEnvironmentVariable,
            variable => variable == name ? "user-scope-password" : null));

        var credential = await fixture.Provider.GetAsync(account, Token);
        var again = await fixture.Provider.GetAsync(account, Token);

        Assert.Equal(new PasswordCredential("someone@gmail.com", "user-scope-password"), credential);
        Assert.Equal(credential, again);
        Assert.Null(Environment.GetEnvironmentVariable(name));
    }

    [Fact]
    public async Task Should_find_the_client_secret_in_the_user_scope_When_the_process_does_not_hold_it()
    {
        using var fixture = new CredentialsFixture(TestAccounts.UserScope(("ORKEON_TEST_GOOGLE_SECRET", "GOCSPX-user-scope")));
        var account = GoogleAccount;
        fixture.Store.With(EmailCredentialProvider.TokenKey(account), new EmailTokenSet { AccessToken = "old", RefreshToken = "rt", ExpiresAt = fixture.Time.GetUtcNow() });
        fixture.Handler.EnqueueJson("""{"access_token":"new","expires_in":3600}""");

        await fixture.Provider.GetAccessTokenAsync(account, Token);

        Assert.Equal("GOCSPX-user-scope", Assert.Single(fixture.Handler.Requests).Form["client_secret"]);
    }

    [Fact]
    public async Task Should_read_the_process_environment_first_When_both_scopes_hold_the_variable()
    {
        using var fixture = new CredentialsFixture(new EmailEnvironment(
            variable => variable == TestAccounts.PasswordVariable ? "process-password" : null,
            variable => variable == TestAccounts.PasswordVariable ? "user-password" : null));

        var credential = await fixture.Provider.GetAsync(PasswordAccount, Token);

        Assert.Equal(new PasswordCredential("someone@gmail.com", "process-password"), credential);
    }

    [Fact]
    public async Task Should_read_a_user_scope_that_cannot_be_read_as_an_absent_variable()
    {
        // A registry read refused (a service account without a profile) is a password not found:
        // the call fails as it does without one, and the readiness check says so instead of throwing.
        using var fixture = new CredentialsFixture(new EmailEnvironment(
            _ => null,
            _ => throw new System.Security.SecurityException("registry access denied")));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Provider.GetAsync(PasswordAccount, Token));
        var diagnosis = await fixture.Provider.DiagnoseAsync(PasswordAccount, Token);

        Assert.Equal(EmailErrorCode.CredentialMissing, error.Code);
        Assert.Equal(error.Message, diagnosis);
        Assert.DoesNotContain("registry access denied", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_say_both_places_the_variable_was_looked_for_When_the_machine_has_a_user_scope()
    {
        using var windows = new CredentialsFixture(TestAccounts.UserScope());
        using var elsewhere = new CredentialsFixture(TestAccounts.Environment());
        var google = GoogleAccount;

        var password = await Assert.ThrowsAsync<EmailToolException>(async () => await windows.Provider.GetAsync(PasswordAccount, Token));
        var secret = Assert.Throws<EmailToolException>(() => windows.Provider.ReadClientSecret(google));
        var secretElsewhere = Assert.Throws<EmailToolException>(() => elsewhere.Provider.ReadClientSecret(google));

        Assert.Equal(
            $"The password of e-mail account 'perso' is read from the environment variable {TestAccounts.PasswordVariable}, which is set neither in the process environment nor in the user's.",
            password.Message);
        Assert.Equal(
            "The OAuth client secret of e-mail account 'google' is read from the environment variable ORKEON_TEST_GOOGLE_SECRET, which is set neither in the process environment nor in the user's.",
            secret.Message);
        // Linux and macOS have no user scope: the sentence keeps its form (the password's is pinned above).
        Assert.Equal(
            "The OAuth client secret of e-mail account 'google' is read from the environment variable ORKEON_TEST_GOOGLE_SECRET, which is not set.",
            secretElsewhere.Message);
    }

    [Fact]
    public async Task Should_serve_a_fresh_stored_token_without_calling_the_identity_provider_and_then_from_memory()
    {
        using var fixture = new CredentialsFixture();
        var account = OutlookAccount;
        fixture.SeedFreshToken(account, "graph-token");

        var credential = await fixture.Provider.GetAsync(account, Token);
        var again = await fixture.Provider.GetAccessTokenAsync(account, Token);

        Assert.Equal(new BearerCredential("someone@outlook.com", "graph-token"), credential);
        Assert.Equal("graph-token", again);
        Assert.Equal(1, fixture.Store.Reads);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_refresh_a_token_about_to_expire_and_write_the_new_one_back()
    {
        using var fixture = new CredentialsFixture();
        var account = OutlookAccount;
        var key = EmailCredentialProvider.TokenKey(account);
        fixture.Store.With(key, new EmailTokenSet { AccessToken = "old", RefreshToken = "rt-1", ExpiresAt = fixture.Time.GetUtcNow().AddMinutes(1) });
        fixture.Handler.EnqueueJson("""{"access_token":"new","expires_in":3600}""");

        var token = await fixture.Provider.GetAccessTokenAsync(account, Token);

        Assert.Equal("new", token);
        var stored = fixture.Store.Tokens[key];
        Assert.Equal(("new", "rt-1"), (stored.AccessToken, stored.RefreshToken));
        Assert.Equal(1, fixture.Store.Writes);
        var form = Assert.Single(fixture.Handler.Requests).Form;
        Assert.Equal(("refresh_token", "rt-1"), (form["grant_type"], form["refresh_token"]));
        Assert.False(form.ContainsKey("client_secret"));
    }

    [Fact]
    public async Task Should_store_a_rotated_refresh_token()
    {
        using var fixture = new CredentialsFixture();
        var account = OutlookAccount;
        var key = EmailCredentialProvider.TokenKey(account);
        fixture.Store.With(key, new EmailTokenSet { AccessToken = "old", RefreshToken = "rt-1", ExpiresAt = fixture.Time.GetUtcNow() });
        fixture.Handler.EnqueueJson("""{"access_token":"new","refresh_token":"rt-2","expires_in":3600}""");

        await fixture.Provider.GetAccessTokenAsync(account, Token);

        Assert.Equal("rt-2", fixture.Store.Tokens[key].RefreshToken);
    }

    [Fact]
    public async Task Should_send_the_Google_client_secret_read_from_its_variable_when_refreshing()
    {
        using var fixture = new CredentialsFixture(TestAccounts.Environment(("ORKEON_TEST_GOOGLE_SECRET", "GOCSPX-secret")));
        var account = GoogleAccount;
        fixture.Store.With(EmailCredentialProvider.TokenKey(account), new EmailTokenSet { AccessToken = "old", RefreshToken = "rt", ExpiresAt = fixture.Time.GetUtcNow() });
        fixture.Handler.EnqueueJson("""{"access_token":"new","expires_in":3600}""");

        await fixture.Provider.GetAccessTokenAsync(account, Token);

        Assert.Equal("GOCSPX-secret", Assert.Single(fixture.Handler.Requests).Form["client_secret"]);
    }

    [Fact]
    public async Task Should_name_the_client_secret_variable_When_it_is_not_set()
    {
        using var fixture = new CredentialsFixture();
        var account = GoogleAccount;
        fixture.Store.With(EmailCredentialProvider.TokenKey(account), new EmailTokenSet { AccessToken = "old", RefreshToken = "rt", ExpiresAt = fixture.Time.GetUtcNow() });

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Provider.GetAccessTokenAsync(account, Token));

        Assert.Equal(EmailErrorCode.CredentialMissing, error.Code);
        Assert.Contains("ORKEON_TEST_GOOGLE_SECRET", error.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_ask_for_a_terminal_sign_in_When_no_token_can_be_used()
    {
        using var fixture = new CredentialsFixture();
        var account = OutlookAccount;
        using var expiredWithoutRefresh = new CredentialsFixture();
        expiredWithoutRefresh.Store.With(EmailCredentialProvider.TokenKey(account), new EmailTokenSet { AccessToken = "old", ExpiresAt = expiredWithoutRefresh.Time.GetUtcNow() });

        var none = await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Provider.GetAccessTokenAsync(account, Token));
        var expired = await Assert.ThrowsAsync<EmailToolException>(async () => await expiredWithoutRefresh.Provider.GetAccessTokenAsync(account, Token));

        const string hint = "E-mail account 'hotmail' needs an OAuth sign-in: run `orkeon email login hotmail` in a terminal.";
        Assert.Equal((EmailErrorCode.LoginRequired, hint), (none.Code, none.Message));
        Assert.Equal((EmailErrorCode.LoginRequired, hint), (expired.Code, expired.Message));
        Assert.Empty(expiredWithoutRefresh.Handler.Requests);
    }

    [Fact]
    public async Task Should_ask_for_a_sign_in_with_the_provider_reason_When_the_refresh_is_refused()
    {
        using var fixture = new CredentialsFixture();
        var account = OutlookAccount;
        fixture.Store.With(EmailCredentialProvider.TokenKey(account), new EmailTokenSet { AccessToken = "old", RefreshToken = "revoked", ExpiresAt = fixture.Time.GetUtcNow() });
        fixture.Handler.Enqueue(new FakeHttpResponse(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"AADSTS70000: expired"}"""));

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Provider.GetAccessTokenAsync(account, Token));

        Assert.Equal(EmailErrorCode.LoginRequired, error.Code);
        Assert.Equal(
            "E-mail account 'hotmail' needs an OAuth sign-in: run `orkeon email login hotmail` in a terminal. (The token refresh was refused: invalid_grant AADSTS70000: expired)",
            error.Message);
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Fact]
    public async Task Should_refresh_once_When_several_calls_need_a_token_at_the_same_time()
    {
        using var fixture = new CredentialsFixture();
        var account = OutlookAccount;
        fixture.Store.With(EmailCredentialProvider.TokenKey(account), new EmailTokenSet { AccessToken = "old", RefreshToken = "rt", ExpiresAt = fixture.Time.GetUtcNow() });
        using var inFlight = new SemaphoreSlim(0);
        using var release = new ManualResetEventSlim();
        fixture.Handler.Enqueue(_ =>
        {
            // The refresh answers only once the other calls are waiting too: without the gate,
            // each would send its own, and find no answer queued.
            inFlight.Release();
            release.Wait(TimeSpan.FromSeconds(30));
            return FakeHttpResponse.Json("""{"access_token":"new","expires_in":3600}""");
        });

        var calls = Enumerable.Range(0, 8).Select(_ => Task.Run(() => fixture.Provider.GetAccessTokenAsync(account, Token), Token)).ToArray();
        await inFlight.WaitAsync(Token);
        await Task.Delay(200, Token);
        release.Set();
        var tokens = await Task.WhenAll(calls);

        Assert.All(tokens, token => Assert.Equal("new", token));
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_refuse_a_token_for_a_password_account()
    {
        using var fixture = new CredentialsFixture();

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Provider.GetAccessTokenAsync(PasswordAccount, Token));

        Assert.Equal(EmailErrorCode.InvalidConfiguration, error.Code);
    }

    [Fact]
    public void Should_key_tokens_by_name_and_a_digest_of_what_they_are_valid_for()
    {
        var account = OutlookAccount;
        var key = EmailCredentialProvider.TokenKey(account);

        Assert.Matches("^hotmail-[0-9a-f]{12}$", key);
        Assert.Equal(key, EmailCredentialProvider.TokenKey(OutlookAccount));
        Assert.NotEqual(key, EmailCredentialProvider.TokenKey(account with { Auth = account.Auth with { OAuth = account.Auth.OAuth! with { Scopes = ["offline_access"] } } }));
        Assert.NotEqual(key, EmailCredentialProvider.TokenKey(account with { Auth = account.Auth with { OAuth = account.Auth.OAuth! with { ClientId = "another-app" } } }));
        Assert.NotEqual(key, EmailCredentialProvider.TokenKey(account with { Address = "other@outlook.com" }));
        Assert.StartsWith("perso-", EmailCredentialProvider.TokenKey(PasswordAccount), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_diagnose_readiness_without_any_network()
    {
        using var ready = new CredentialsFixture();
        using var noPassword = new CredentialsFixture(TestAccounts.Environment());
        using var refreshable = new CredentialsFixture();
        var outlook = OutlookAccount;
        refreshable.Store.With(EmailCredentialProvider.TokenKey(outlook), new EmailTokenSet { AccessToken = "old", RefreshToken = "rt", ExpiresAt = refreshable.Time.GetUtcNow() });
        ready.SeedFreshToken(outlook);

        var missingPassword = await noPassword.Provider.DiagnoseAsync(PasswordAccount, Token);

        Assert.Null(await ready.Provider.DiagnoseAsync(PasswordAccount, Token));
        Assert.Null(await ready.Provider.DiagnoseAsync(outlook, Token));
        Assert.Null(await refreshable.Provider.DiagnoseAsync(outlook, Token));
        Assert.NotNull(missingPassword);
        Assert.Contains(TestAccounts.PasswordVariable, missingPassword, StringComparison.Ordinal);
        Assert.Equal(
            "E-mail account 'hotmail' needs an OAuth sign-in: run `orkeon email login hotmail` in a terminal.",
            await noPassword.Provider.DiagnoseAsync(outlook, Token));
        Assert.Empty(ready.Handler.Requests);
        Assert.Empty(refreshable.Handler.Requests);
    }

    [Fact]
    public async Task Should_not_call_an_account_ready_While_the_client_secret_it_names_is_unset()
    {
        using var fixture = new CredentialsFixture();
        var account = GoogleAccount;
        fixture.SeedFreshToken(account);

        var diagnosis = await fixture.Provider.DiagnoseAsync(account, Token);

        Assert.NotNull(diagnosis);
        Assert.Contains("ORKEON_TEST_GOOGLE_SECRET", diagnosis, StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Should_say_where_tokens_come_from_When_the_host_keeps_none()
    {
        using var fixture = new CredentialsFixture();
        var provider = new EmailCredentialProvider(new UnavailableEmailTokenStore(), fixture.OAuth, TestAccounts.PasswordEnvironment());

        var diagnosis = await provider.DiagnoseAsync(OutlookAccount, Token);
        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await provider.GetAccessTokenAsync(OutlookAccount, Token));

        Assert.StartsWith("This host keeps no OAuth tokens for e-mail accounts.", diagnosis, StringComparison.Ordinal);
        Assert.Equal(EmailErrorCode.NotConfigured, error.Code);
    }

    [Fact]
    public async Task Should_store_and_forget_signed_in_tokens()
    {
        using var fixture = new CredentialsFixture();
        var account = OutlookAccount;
        var tokens = new EmailTokenSet { AccessToken = "signed-in", RefreshToken = "rt", ExpiresAt = fixture.Time.GetUtcNow().AddHours(1) };

        await fixture.Provider.StoreAsync(account, tokens, Token);
        var served = await fixture.Provider.GetAccessTokenAsync(account, Token);
        var forgotten = await fixture.Provider.ForgetAsync(account, Token);
        var again = await fixture.Provider.ForgetAsync(account, Token);

        Assert.Equal("signed-in", served);
        Assert.Equal(0, fixture.Store.Reads);
        Assert.True(forgotten);
        Assert.False(again);
        Assert.Empty(fixture.Store.Tokens);
        await Assert.ThrowsAsync<EmailToolException>(async () => await fixture.Provider.GetAccessTokenAsync(account, Token));
    }

    [Fact]
    public void Should_read_no_client_secret_When_the_account_declares_none()
    {
        using var fixture = new CredentialsFixture();

        Assert.Null(fixture.Provider.ReadClientSecret(OutlookAccount));
        Assert.Null(fixture.Provider.ReadClientSecret(PasswordAccount));
    }
}
