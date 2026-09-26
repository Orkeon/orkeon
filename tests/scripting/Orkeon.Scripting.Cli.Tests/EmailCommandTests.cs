using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Tools.Email.Administration;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon email accounts | login | logout | check</c> driven in-process (MAIL-05). The runner
/// host is built for real over a settings file in a scratch directory, credentials directory
/// included; only the identity provider is replaced, through the named HTTP client, so the
/// whole path — device-code sign-in, token written through the privileged VFS view, readiness,
/// sign-out — runs offline.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class EmailCommandTests
{
    [Fact]
    public async Task Accounts_WithoutAnyAccount_SaysWhereToDeclareOne()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", "{}");
        using var console = new TestConsole();

        var exit = await EmailCommand.ExecuteAccountsAsync(new EmailAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("No e-mail account is configured", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Tools:Email:Accounts", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accounts_ListsRightsAndSaysWhatIsMissing()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "Orkeon": { "Tools": { "Email": { "Accounts": {
                "perso":  { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read, Organize",
                            "Auth": { "PasswordEnvVar": "ORKEON_TEST_EMAIL_UNSET_VARIABLE" } },
                "broken": { "Provider": "Gmail", "Address": "me@gmail.com" }
            } } } } }
            """);
        using var console = new TestConsole();

        var exit = await EmailCommand.ExecuteAccountsAsync(new EmailAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("perso — me@gmail.com", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("rights: Read, Organize", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("ORKEON_TEST_EMAIL_UNSET_VARIABLE", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("Rights is required", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accounts_Json_WritesOneObjectPerAccount()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "Orkeon": { "Tools": { "Email": { "Accounts": {
                "perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read",
                           "Auth": { "PasswordEnvVar": "ORKEON_TEST_EMAIL_UNSET_VARIABLE" } }
            } } } } }
            """);
        using var console = new TestConsole();

        var exit = await EmailCommand.ExecuteAccountsAsync(new EmailAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root, Json = true });

        Assert.Equal(Program.ExitOk, exit);
        using var document = JsonDocument.Parse(console.Stdout);
        var account = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("perso", account.GetProperty("name").GetString());
        Assert.True(account.GetProperty("default").GetBoolean());
        Assert.False(account.GetProperty("ready").GetBoolean());
    }

    [Fact]
    public async Task Login_OfAPasswordAccount_IsRefusedAsAConfigurationMatter()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "Orkeon": { "Tools": { "Email": { "Accounts": {
                "perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read",
                           "Auth": { "PasswordEnvVar": "ORKEON_TEST_EMAIL_UNSET_VARIABLE" } }
            } } } } }
            """);
        using var console = new TestConsole();

        var exit = await EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "perso" });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("signs in with a password", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_OfAnUnknownAccount_NamesTheConfiguredOnes()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "Orkeon": { "Tools": { "Email": { "Accounts": {
                "perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read",
                           "Auth": { "PasswordEnvVar": "ORKEON_TEST_EMAIL_UNSET_VARIABLE" } }
            } } } } }
            """);
        using var console = new TestConsole();

        var exit = await EmailCommand.ExecuteCheckAsync(new EmailCheckCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "work" });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("Unknown e-mail account 'work'", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("perso", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceCodeLogin_StoresTheTokens_ThenTheAccountIsReady_ThenLogoutForgetsThem()
    {
        using var scratch = new ScriptScratch();
        var credentials = Path.Combine(scratch.Root, "credentials");
        scratch.WriteFile("appsettings.json", $$"""
            { "Orkeon": { "Tools": { "Email": {
                "CredentialsDirectory": {{JsonSerializer.Serialize(credentials)}},
                "Accounts": {
                  "hotmail": { "Provider": "Outlook", "Address": "me@hotmail.com", "Rights": "Read",
                               "Auth": { "ClientId": "client-123" } }
                } } } } }
            """);
        using var identity = new StubIdentityProvider();
        var interaction = new RecordingLoginInteraction();

        using (var console = new TestConsole())
        {
            var exit = await EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root,
                Account = "hotmail",
                Interaction = interaction,
                ConfigureTestServices = (_, services) => services
                    .AddHttpClient("orkeon.email")
                    .ConfigurePrimaryHttpMessageHandler(() => identity),
            });

            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("Signed in", console.Stdout, StringComparison.Ordinal);
        }

        Assert.Equal("WXYZ-1234", interaction.UserCode);
        Assert.Equal(new Uri("https://microsoft.com/devicelogin"), interaction.VerificationUri);
        Assert.Contains(identity.Requests, request => request.EndsWith("/consumers/oauth2/v2.0/devicecode", StringComparison.Ordinal));
        Assert.Contains(identity.Bodies, body => body.Contains("client_id=client-123", StringComparison.Ordinal)
            && body.Contains("offline_access", StringComparison.Ordinal));

        var tokenFile = Assert.Single(Directory.GetFiles(Path.Combine(credentials, "email"), "hotmail-*.json"));
        Assert.Contains("refresh-1", await File.ReadAllTextAsync(tokenFile, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(credentials));
        }

        using (var console = new TestConsole())
        {
            var exit = await EmailCommand.ExecuteAccountsAsync(new EmailAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root });
            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("hotmail (default) — me@hotmail.com", console.Stdout, StringComparison.Ordinal);
            Assert.Contains("  ready", console.Stdout, StringComparison.Ordinal);
        }

        using (var console = new TestConsole())
        {
            var exit = await EmailCommand.ExecuteLogoutAsync(new EmailLogoutCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "hotmail" });
            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("Signed out", console.Stdout, StringComparison.Ordinal);
        }

        Assert.Empty(Directory.GetFiles(Path.Combine(credentials, "email"), "hotmail-*.json"));
        using (var console = new TestConsole())
        {
            await EmailCommand.ExecuteAccountsAsync(new EmailAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root });
            Assert.Contains("orkeon email login hotmail", console.Stdout, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Dispatch_KnowsTheFourVerbs()
    {
        using var console = new TestConsole();

        var exit = await EmailCommand.DispatchAsync(["--help"]);

        Assert.Equal(Program.ExitScriptError, exit);
        foreach (var verb in new[] { "accounts", "login", "logout", "check" })
            Assert.Contains(verb, console.Stdout, StringComparison.Ordinal);
    }

    /// <summary>Hand-written double: records what the sign-in showed the user.</summary>
    private sealed class RecordingLoginInteraction : IEmailLoginInteraction
    {
        public Uri? VerificationUri { get; private set; }

        public string? UserCode { get; private set; }

        public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, CancellationToken cancellationToken)
        {
            VerificationUri = verificationUri;
            UserCode = userCode;
            return Task.CompletedTask;
        }

        public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The Outlook preset signs in with a device code.");

        public Task<string?> ReadRedirectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    /// <summary>Hand-written double: the Microsoft identity platform's device-code and token endpoints.</summary>
    private sealed class StubIdentityProvider : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            var json = path.EndsWith("/devicecode", StringComparison.Ordinal)
                ? """{ "device_code": "device-1", "user_code": "WXYZ-1234", "verification_uri": "https://microsoft.com/devicelogin", "expires_in": 900, "interval": 1 }"""
                : """{ "token_type": "Bearer", "access_token": "access-1", "refresh_token": "refresh-1", "expires_in": 3600, "scope": "https://graph.microsoft.com/Mail.ReadWrite" }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
