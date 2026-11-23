using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon chat accounts | login | logout | check</c> driven in-process (#446). The runner host
/// is built for real over a settings file in a scratch directory, credentials directory included;
/// only the Microsoft endpoints are replaced, through the chat family's named HTTP client.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ChatCommandTests
{
    private const string Authority = "https://login.microsoftonline.com/organizations/oauth2/v2.0/";

    [Fact]
    public async Task Accounts_WithoutAnyAccount_SaysWhereToDeclareOne()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", "{}");
        using var console = new TestConsole();

        var exit = await ChatCommand.ExecuteAccountsAsync(console, new ChatAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("No chat account is configured", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Tools:Chat:Accounts", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accounts_ListsRightsPlacesAndWhatIsMissing_AndJsonSeparatesTheProblems()
    {
        using var scratch = new ScriptScratch();
        WriteTeamsSettings(scratch, Path.Combine(scratch.Root, "credentials"), extra: """, "broken": { "Provider": "Teams" }""");

        using (var console = new TestConsole())
        {
            var exit = await ChatCommand.ExecuteAccountsAsync(console, new ChatAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root });

            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("Teams · cloud Global", console.Stdout, StringComparison.Ordinal);
            Assert.Contains("rights: Read", console.Stdout, StringComparison.Ordinal);
            Assert.Contains("allowed places: 1 to read, 0 to post in", console.Stdout, StringComparison.Ordinal);
            Assert.Contains("orkeon chat login pro", console.Stdout, StringComparison.Ordinal);
            Assert.Contains("Rights is required", console.Stdout, StringComparison.Ordinal);
        }

        using (var console = new TestConsole())
        {
            await ChatCommand.ExecuteAccountsAsync(console, new ChatAccountsCommandOptions { WorkingDirectoryOverride = scratch.Root, Json = true });

            using var json = JsonDocument.Parse(console.Stdout);
            var pro = json.RootElement.EnumerateArray().Single(account => account.GetProperty("name").GetString() == "pro");
            Assert.Equal(pro.GetProperty("problem").GetString(), pro.GetProperty("credential_problem").GetString());
            Assert.Empty(pro.GetProperty("configuration_problems").EnumerateArray());
            Assert.Equal(Authority + "token", pro.GetProperty("token_endpoint").GetString());
            var broken = json.RootElement.EnumerateArray().Single(account => account.GetProperty("name").GetString() == "broken");
            Assert.NotEmpty(broken.GetProperty("configuration_problems").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, broken.GetProperty("credential_problem").ValueKind);
        }
    }

    [Fact]
    public async Task DeviceCodeLogin_StoresTheTokens_ThenCheckConnects_ThenLogoutDeletesThemAndSaysTheyAreNotRevoked()
    {
        using var scratch = new ScriptScratch();
        var credentials = Path.Combine(scratch.Root, "credentials");
        WriteTeamsSettings(scratch, credentials);
        using var identity = new FakeHttpMessageHandler();
        MapSignIn(identity);
        var interaction = new FakeEmailLoginInteraction();

        using (var console = new TestConsole())
        {
            var exit = await ChatCommand.ExecuteLoginAsync(console, new ChatLoginCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root,
                Account = "pro",
                DeviceCode = true,
                Interaction = interaction,
                ConfigureTestServices = Answering(identity),
            });

            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("Signed in: the tokens of chat account 'pro' are stored.", console.Stdout, StringComparison.Ordinal);
        }

        Assert.Equal("WXYZ-1234", interaction.UserCode);
        var tokenFile = Assert.Single(Directory.GetFiles(Path.Combine(credentials, "chat"), "pro-*.json"));
        Assert.Contains("refresh-1", await File.ReadAllTextAsync(tokenFile, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        using (var graph = new FakeHttpMessageHandler())
        using (var console = new TestConsole())
        {
            MapGraph(graph);

            // No account named: the only one declared.
            var exit = await ChatCommand.ExecuteCheckAsync(console, new ChatCheckCommandOptions { WorkingDirectoryOverride = scratch.Root, ConfigureTestServices = Answering(graph) });

            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("Chat account 'pro' is reachable as ada@example.org.", console.Stdout, StringComparison.Ordinal);
            Assert.Contains("Read: ready", console.Stdout, StringComparison.Ordinal);
            Assert.All(graph.Requests, request => Assert.Equal("Bearer access-1", request.Authorization));
        }

        using (var offline = new FakeHttpMessageHandler())
        using (var console = new TestConsole())
        {
            var exit = await ChatCommand.ExecuteLogoutAsync(console, new ChatLogoutCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "pro", ConfigureTestServices = Answering(offline) });

            Assert.Equal(Program.ExitOk, exit);
            Assert.Contains("Signed out: the tokens of chat account 'pro' are deleted.", console.Stdout, StringComparison.Ordinal);
            Assert.Contains("a tenant administrator removes the application's access in the Microsoft Entra admin center", console.Stdout, StringComparison.Ordinal);
            Assert.Empty(offline.Requests);
        }

        Assert.Empty(Directory.GetFiles(Path.Combine(credentials, "chat"), "pro-*.json"));
    }

    [Fact]
    public async Task Check_OfARightThatIsNotUsable_SaysWhatIsMissing_AndExitsOnTheScriptError()
    {
        using var scratch = new ScriptScratch();
        WriteTeamsSettings(scratch, Path.Combine(scratch.Root, "credentials"), rights: "Read, Send");
        using (var identity = new FakeHttpMessageHandler())
        using (var login = new TestConsole())
        {
            MapSignIn(identity);
            Assert.Equal(Program.ExitOk, await ChatCommand.ExecuteLoginAsync(login, new ChatLoginCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root, Account = "pro", DeviceCode = true, Interaction = new FakeEmailLoginInteraction(), ConfigureTestServices = Answering(identity),
            }));
        }

        using var graph = new FakeHttpMessageHandler();
        MapGraph(graph);
        using var console = new TestConsole();

        var exit = await ChatCommand.ExecuteCheckAsync(console, new ChatCheckCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "pro", ConfigureTestServices = Answering(graph) });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("Send: NOT READY", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("Send:AllowedPlaces is empty", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_OfAnUnknownAccount_NamesTheConfiguredOnes_AndOneNeverSignedInNamesTheCommand()
    {
        using var scratch = new ScriptScratch();
        WriteTeamsSettings(scratch, Path.Combine(scratch.Root, "credentials"));

        using (var console = new TestConsole())
        {
            var exit = await ChatCommand.ExecuteCheckAsync(console, new ChatCheckCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "work" });

            Assert.Equal(Program.ExitScriptError, exit);
            Assert.Contains("Unknown chat account 'work'. Configured: pro.", console.Stderr, StringComparison.Ordinal);
        }

        using (var console = new TestConsole())
        {
            var exit = await ChatCommand.ExecuteCheckAsync(console, new ChatCheckCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "pro" });

            Assert.Equal(Program.ExitScriptError, exit);
            Assert.Contains("orkeon chat check: Chat account 'pro' needs a sign-in: run `orkeon chat login pro`", console.Stderr, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Logout_OfAnAccountWithoutTokens_SaysSo()
    {
        using var scratch = new ScriptScratch();
        WriteTeamsSettings(scratch, Path.Combine(scratch.Root, "credentials"));
        using var console = new TestConsole();

        var exit = await ChatCommand.ExecuteLogoutAsync(console, new ChatLogoutCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "pro" });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("Chat account 'pro' had no stored tokens.", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public async Task Dispatch_KnowsTheFourVerbs()
    {
        using var console = new TestConsole();

        var exit = await ChatCommand.DispatchAsync(console, ["--help"]);

        Assert.Equal(Program.ExitOk, exit);
        foreach (var verb in new[] { "accounts", "login", "logout", "check" })
            Assert.Contains(verb, console.Stdout, StringComparison.Ordinal);
    }

    private static void WriteTeamsSettings(ScriptScratch scratch, string credentials, string rights = "Read", string extra = "") =>
        scratch.WriteFile("appsettings.json", $$"""
            { "Orkeon": { "Tools": { "Chat": {
                "CredentialsDirectory": {{JsonSerializer.Serialize(credentials)}},
                "Accounts": {
                  "pro": { "Provider": "Teams", "Rights": "{{rights}}", "Auth": { "ClientId": "client-123" }, "Read": { "AllowedPlaces": ["*"] } }{{extra}}
                } } } } }
            """);

    /// <summary>Routes the chat family's named HTTP client — identity platform and Graph alike — to <paramref name="handler"/>.</summary>
    private static Action<HostBuilderContext, IServiceCollection> Answering(HttpMessageHandler handler) =>
        (_, services) => services.AddHttpClient("orkeon.chat").ConfigurePrimaryHttpMessageHandler(() => handler);

    private static void MapSignIn(FakeHttpMessageHandler identity)
    {
        identity.Map(HttpMethod.Post, Authority + "devicecode", FakeHttpResponse.Json(
            """{"device_code":"dev","user_code":"WXYZ-1234","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":1}"""));
        identity.Map(HttpMethod.Post, Authority + "token", FakeHttpResponse.Json(
            """{"access_token":"access-1","refresh_token":"refresh-1","expires_in":3600,"scope":"User.Read Team.ReadBasic.All Channel.ReadBasic.All Chat.Read User.ReadBasic.All"}"""));
    }

    private static void MapGraph(FakeHttpMessageHandler graph)
    {
        graph.Map(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me", FakeHttpResponse.Json(
            """{"id":"u1","displayName":"Ada","userPrincipalName":"ada@example.org","mail":"ada@example.org"}"""));
        graph.Map(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/chats", FakeHttpResponse.Json("""{"value":[]}"""));
    }
}
