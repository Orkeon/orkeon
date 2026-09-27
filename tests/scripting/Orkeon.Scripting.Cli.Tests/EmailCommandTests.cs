using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon email accounts | login | logout | check</c> driven in-process (MAIL-05). The runner
/// host is built for real over a settings file in a scratch directory, credentials directory
/// included; only the Microsoft endpoints are replaced, through the named HTTP client, so the
/// whole path — device-code sign-in, token written through the privileged VFS view, connection
/// check over Graph, readiness, sign-out — runs offline.
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
    public async Task DeviceCodeLogin_StoresTheTokens_ThenCheckConnects_ThenTheAccountIsReady_ThenLogoutForgetsThem()
    {
        using var scratch = new ScriptScratch();
        var credentials = Path.Combine(scratch.Root, "credentials");
        WriteOutlookSettings(scratch, credentials);
        using var identity = new StubOutlookHttpMessageHandler();
        var interaction = new FakeEmailLoginInteraction();

        using (var console = new TestConsole())
        {
            var exit = await EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root,
                Account = "hotmail",
                Interaction = interaction,
                ConfigureTestServices = Answering(identity),
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

        using (var graph = new StubOutlookHttpMessageHandler())
        using (var console = new TestConsole())
        {
            var exit = await EmailCommand.ExecuteCheckAsync(new EmailCheckCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root,
                Account = "hotmail",
                ConfigureTestServices = Answering(graph),
            });

            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("E-mail account 'hotmail' is reachable: 2 folder(s), inbox 12 message(s), 3 unread.", console.Stdout, StringComparison.Ordinal);
            // The stored access token was still fresh: the check went to Graph only.
            Assert.All(graph.Requests, path => Assert.StartsWith("/v1.0/me/mailFolders", path, StringComparison.Ordinal));
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

    [Fact]
    public async Task Check_WhenTheServerFails_ExitsWithTheRuntimeCode()
    {
        using var scratch = new ScriptScratch();
        WriteOutlookSettings(scratch, Path.Combine(scratch.Root, "credentials"));
        using (var identity = new StubOutlookHttpMessageHandler())
        using (new TestConsole())
        {
            Assert.Equal(Program.ExitOk, await EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root,
                Account = "hotmail",
                Interaction = new FakeEmailLoginInteraction(),
                ConfigureTestServices = Answering(identity),
            }));
        }

        using var graph = new StubOutlookHttpMessageHandler { GraphStatus = HttpStatusCode.ServiceUnavailable };
        using var console = new TestConsole();

        var exit = await EmailCommand.ExecuteCheckAsync(new EmailCheckCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root,
            Account = "hotmail",
            ConfigureTestServices = Answering(graph),
        });

        Assert.Equal(Program.ExitRuntimeError, exit);
        Assert.Contains("orkeon email check: Microsoft Graph is throttling or unavailable", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(console.Stdout);
    }

    /// <summary>One Outlook account signing in with OAuth, its tokens kept under <paramref name="credentials"/>.</summary>
    private static void WriteOutlookSettings(ScriptScratch scratch, string credentials) =>
        scratch.WriteFile("appsettings.json", $$"""
            { "Orkeon": { "Tools": { "Email": {
                "CredentialsDirectory": {{JsonSerializer.Serialize(credentials)}},
                "Accounts": {
                  "hotmail": { "Provider": "Outlook", "Address": "me@hotmail.com", "Rights": "Read",
                               "Auth": { "ClientId": "client-123" } }
                } } } } }
            """);

    /// <summary>Routes the e-mail family's named HTTP client — identity platform and Graph alike — to <paramref name="handler"/>.</summary>
    private static Action<HostBuilderContext, IServiceCollection> Answering(HttpMessageHandler handler) =>
        (_, services) => services.AddHttpClient("orkeon.email").ConfigurePrimaryHttpMessageHandler(() => handler);
}
