using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon drive accounts | login | logout | check</c> driven in-process (DRIVE-05). The runner
/// host is built for real over a settings file in a scratch directory, credentials directory
/// included; only the Microsoft endpoints are replaced, through the drive family's named HTTP
/// client, so the device-code sign-in, the token written through the privileged VFS view, the
/// check over Graph and the sign-out run offline.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class DriveCommandTests
{
    private const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";

    [Fact]
    public async Task Accounts_WithoutAnyAccount_SaysWhereToDeclareOne()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", "{}");
        using var console = new TestConsole();

        var exit = await DriveCommand.ExecuteAccountsAsync(console, new DriveAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("No drive account is configured", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Tools:Drive:Accounts", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accounts_ListsRootRightsAndWhatIsMissing_AndJsonCarriesNoSecret()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "Orkeon": { "Tools": { "Drive": { "Accounts": {
                "perso":  { "Provider": "GoogleDrive", "Rights": "Read, Write", "Root": "Orkeon/agents",
                            "Auth": { "ClientId": "id", "ClientSecretEnvVar": "ORKEON_TEST_DRIVE_UNSET_VARIABLE" } },
                "broken": { "Provider": "OneDrive" }
            } } } } }
            """);

        using (var console = new TestConsole())
        {
            var exit = await DriveCommand.ExecuteAccountsAsync(console, new DriveAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root });

            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("GoogleDrive · root /Orkeon/agents", console.Stdout, StringComparison.Ordinal);
            Assert.Contains("rights: Read, Write", console.Stdout, StringComparison.Ordinal);
            Assert.Contains("ORKEON_TEST_DRIVE_UNSET_VARIABLE", console.Stdout, StringComparison.Ordinal);
            Assert.Contains("Rights is required", console.Stdout, StringComparison.Ordinal);
        }

        using (var console = new TestConsole())
        {
            await DriveCommand.ExecuteAccountsAsync(console, new DriveAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root, Json = true });

            using var json = JsonDocument.Parse(console.Stdout);
            var perso = json.RootElement.EnumerateArray().Single(account => account.GetProperty("name").GetString() == "perso");
            Assert.Equal("/Orkeon/agents", perso.GetProperty("root").GetString());
            Assert.False(perso.TryGetProperty("allowedRecipients", out _));
        }
    }

    [Fact]
    public async Task DeviceCodeLogin_StoresTheTokens_ThenCheckConnects_ThenLogoutForgetsThem()
    {
        using var scratch = new ScriptScratch();
        var credentials = Path.Combine(scratch.Root, "credentials");
        WriteOneDriveSettings(scratch, credentials);
        using var identity = new FakeHttpMessageHandler();
        identity.Map(HttpMethod.Post, Authority + "devicecode", FakeHttpResponse.Json(
            """{"device_code":"dev","user_code":"WXYZ-1234","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":1}"""));
        identity.Map(HttpMethod.Post, Authority + "token", FakeHttpResponse.Json(
            """{"access_token":"access-1","refresh_token":"refresh-1","expires_in":3600}"""));
        var interaction = new FakeEmailLoginInteraction();

        using (var console = new TestConsole())
        {
            var exit = await DriveCommand.ExecuteLoginAsync(console, new DriveLoginCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root,
                Account = "pro",
                Interaction = interaction,
                ConfigureTestServices = Answering(identity),
            });

            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("Signed in: the tokens of drive account 'pro' are stored.", console.Stdout, StringComparison.Ordinal);
        }

        Assert.Equal("WXYZ-1234", interaction.UserCode);
        var tokenFile = Assert.Single(Directory.GetFiles(Path.Combine(credentials, "drive"), "pro-*.json"));
        Assert.Contains("refresh-1", await File.ReadAllTextAsync(tokenFile, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        using (var graph = new FakeHttpMessageHandler())
        using (var console = new TestConsole())
        {
            graph.Map(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive/root:/Orkeon", FakeHttpResponse.Json(
                """{"id":"root-id","name":"Orkeon","folder":{"childCount":1},"parentReference":{"path":"/drive/root:"}}"""));
            graph.Map(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive/items/root-id/children", FakeHttpResponse.Json(
                """{"value":[{"id":"f1","name":"a.txt","file":{"mimeType":"text/plain"}}]}"""));

            var exit = await DriveCommand.ExecuteCheckAsync(console, new DriveCheckCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root,
                Account = "pro",
                ConfigureTestServices = Answering(graph),
            });

            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("Drive account 'pro' is reachable: root folder /Orkeon ('Orkeon') holds 1 item(s).", console.Stdout, StringComparison.Ordinal);
            Assert.All(graph.Requests, request => Assert.Equal("Bearer access-1", request.Authorization));
        }

        using (var console = new TestConsole())
        {
            var exit = await DriveCommand.ExecuteLogoutAsync(console, new DriveLogoutCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "pro" });
            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("Signed out", console.Stdout, StringComparison.Ordinal);
        }

        Assert.Empty(Directory.GetFiles(Path.Combine(credentials, "drive"), "pro-*.json"));
        using (var console = new TestConsole())
        {
            await DriveCommand.ExecuteAccountsAsync(console, new DriveAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root });
            Assert.Contains("orkeon drive login pro", console.Stdout, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Check_OfAnUnknownAccount_NamesTheConfiguredOnes_AndAThrottledApiExitsWithTheRuntimeCode()
    {
        using var scratch = new ScriptScratch();
        WriteOneDriveSettings(scratch, Path.Combine(scratch.Root, "credentials"));

        using (var console = new TestConsole())
        {
            var exit = await DriveCommand.ExecuteCheckAsync(console, new DriveCheckCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "work" });

            Assert.Equal(Program.ExitScriptError, exit);
            Assert.Contains("Unknown drive account 'work'. Configured: pro.", console.Stderr, StringComparison.Ordinal);
        }

        using (var console = new TestConsole())
        {
            var exit = await DriveCommand.ExecuteCheckAsync(console, new DriveCheckCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "pro" });

            Assert.Equal(Program.ExitScriptError, exit);
            Assert.Contains("orkeon drive check: Drive account 'pro' needs an OAuth sign-in", console.Stderr, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public async Task Dispatch_KnowsTheFourVerbs()
    {
        using var console = new TestConsole();

        var exit = await DriveCommand.DispatchAsync(console, ["--help"]);

        Assert.Equal(Program.ExitOk, exit);
        foreach (var verb in new[] { "accounts", "login", "logout", "check" })
            Assert.Contains(verb, console.Stdout, StringComparison.Ordinal);
    }

    /// <summary>One OneDrive account confined to <c>/Orkeon</c>, its tokens kept under <paramref name="credentials"/>.</summary>
    private static void WriteOneDriveSettings(ScriptScratch scratch, string credentials) =>
        scratch.WriteFile("appsettings.json", $$"""
            { "Orkeon": { "Tools": { "Drive": {
                "CredentialsDirectory": {{JsonSerializer.Serialize(credentials)}},
                "Accounts": {
                  "pro": { "Provider": "OneDrive", "Rights": "Read", "Root": "Orkeon", "Auth": { "ClientId": "client-123" } }
                } } } } }
            """);

    /// <summary>Routes the drive family's named HTTP client — identity platform and Graph alike — to <paramref name="handler"/>.</summary>
    private static Action<HostBuilderContext, IServiceCollection> Answering(HttpMessageHandler handler) =>
        (_, services) => services.AddHttpClient("orkeon.drive").ConfigurePrimaryHttpMessageHandler(() => handler);
}
