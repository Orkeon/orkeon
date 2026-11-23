using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Constants.Protocol;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon chat login | check --events jsonl</c>: the protocol a client drives, one JSON line
/// per step on standard output and nothing else there, the vocabulary of <see cref="ChatEventKinds"/>.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ChatCommandEventsTests
{
    private const string Authority = "https://login.microsoftonline.com/organizations/oauth2/v2.0/";

    [Fact]
    public async Task Login_WithEvents_TheDeviceSignIn_WritesTheCodeThenTheCompletion_AndNothingElse()
    {
        using var scratch = new ScriptScratch();
        var credentials = Path.Combine(scratch.Root, "credentials");
        WriteTeamsSettings(scratch, credentials);
        using var console = new ConversationConsole();

        await LoginAsync(scratch, console);

        var events = EventLines(console.Stdout);
        Assert.Equal([ChatEventKinds.LoginDeviceCode, ChatEventKinds.LoginCompleted], events.Select(e => e.GetProperty("kind").GetString()));
        Assert.Equal("WXYZ-1234", events[0].GetProperty("user_code").GetString());
        Assert.Equal("https://microsoft.com/devicelogin", events[0].GetProperty("verification_uri").GetString());
        Assert.Equal("pro", events[1].GetProperty("account").GetString());
        Assert.Single(Directory.GetFiles(Path.Combine(credentials, "chat"), "pro-*.json"));
    }

    [Fact]
    public async Task Login_WithEvents_WithoutDeviceCode_OpensTheBrowserSignIn_AndEndsWhenTheDriverLeaves()
    {
        using var scratch = new ScriptScratch();
        WriteTeamsSettings(scratch, Path.Combine(scratch.Root, "credentials"));
        using var identity = new FakeHttpMessageHandler();
        using var console = new ConversationConsole();

        var login = ChatCommand.ExecuteLoginAsync(console, new ChatLoginCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root, Account = "pro", Events = "jsonl", ConfigureTestServices = Answering(identity),
        });
        var opened = JsonElement.Parse(await console.LineAsync(0).WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken));
        console.Input.Close();

        Assert.Equal(Program.ExitCancelled, await login.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken));
        Assert.Equal(ChatEventKinds.LoginAuthorizationUrl, opened.GetProperty("kind").GetString());
        Assert.StartsWith(Authority + "authorize?", opened.GetProperty("authorization_uri").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_WithEvents_WhenTheAccountNeedsASignIn_WritesOneErrorEventWithItsCode()
    {
        using var scratch = new ScriptScratch();
        WriteTeamsSettings(scratch, Path.Combine(scratch.Root, "credentials"));
        using var console = new ConversationConsole();

        var exit = await ChatCommand.ExecuteCheckAsync(console, new ChatCheckCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "pro", Events = "jsonl" });

        Assert.Equal(Program.ExitScriptError, exit);
        var error = Assert.Single(EventLines(console.Stdout));
        Assert.Equal(ChatEventKinds.Error, error.GetProperty("kind").GetString());
        Assert.Equal("SignInRequired", error.GetProperty("code").GetString());
        Assert.DoesNotContain("chat check", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_WithEvents_WritesWhatEachRightCanDo_AndTheText()
    {
        using var scratch = new ScriptScratch();
        WriteTeamsSettings(scratch, Path.Combine(scratch.Root, "credentials"));
        using (var login = new ConversationConsole())
            await LoginAsync(scratch, login);

        using var graph = new FakeHttpMessageHandler();
        MapGraph(graph);
        using var console = new ConversationConsole();

        var exit = await ChatCommand.ExecuteCheckAsync(console, new ChatCheckCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root, Account = "pro", Events = "jsonl", ConfigureTestServices = Answering(graph),
        });

        Assert.Equal(Program.ExitOk, exit);
        var completed = Assert.Single(EventLines(console.Stdout));
        Assert.Equal(ChatEventKinds.CheckCompleted, completed.GetProperty("kind").GetString());
        Assert.Equal(("pro", "ada@example.org", true), (completed.GetProperty("account").GetString(), completed.GetProperty("user").GetString(), completed.GetProperty("ready").GetBoolean()));
        var read = Assert.Single(completed.GetProperty("rights").EnumerateArray());
        Assert.Equal(("Read", true), (read.GetProperty("right").GetString(), read.GetProperty("ready").GetBoolean()));
        Assert.StartsWith("Chat account 'pro' is reachable as ada@example.org.", completed.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_events_format_it_does_not_know_is_refused_before_anything_else()
    {
        using var scratch = new ScriptScratch();
        WriteTeamsSettings(scratch, Path.Combine(scratch.Root, "credentials"));
        using var console = new ConversationConsole();

        var exit = await ChatCommand.ExecuteLoginAsync(console, new ChatLoginCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "pro", Events = "xml" });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("unsupported --events format 'xml'", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(console.Stdout);
    }

    private static async Task LoginAsync(ScriptScratch scratch, ConversationConsole console)
    {
        using var identity = new FakeHttpMessageHandler();
        MapSignIn(identity);
        Assert.Equal(Program.ExitOk, await ChatCommand.ExecuteLoginAsync(console, new ChatLoginCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root, Account = "pro", Events = "jsonl", DeviceCode = true, ConfigureTestServices = Answering(identity),
        }));
    }

    private static List<JsonElement> EventLines(string stdout) =>
        stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonElement.Parse(line))
            .ToList();

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
