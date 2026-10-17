using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Constants.Protocol;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon drive login | check --events jsonl</c>: the protocol Orkeon Studio's Drive tab
/// drives, one JSON line per step on standard output and nothing else there, the vocabulary of
/// <see cref="DriveEventKinds"/>.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class DriveCommandEventsTests
{
    private const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";

    [Fact]
    public async Task Login_WithEvents_TheDeviceSignIn_WritesTheCodeThenTheCompletion_AndNothingElse()
    {
        using var scratch = new ScriptScratch();
        var credentials = Path.Combine(scratch.Root, "credentials");
        WriteOneDriveSettings(scratch, credentials);
        using var identity = new FakeHttpMessageHandler();
        identity.Map(HttpMethod.Post, Authority + "devicecode", FakeHttpResponse.Json(
            """{"device_code":"dev","user_code":"WXYZ-1234","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":1}"""));
        identity.Map(HttpMethod.Post, Authority + "token", FakeHttpResponse.Json(
            """{"access_token":"access-1","refresh_token":"refresh-1","expires_in":3600}"""));
        using var console = new ConversationConsole();

        var exit = await DriveCommand.ExecuteLoginAsync(new DriveLoginCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root,
            Account = "pro",
            Events = "jsonl",
            ConfigureTestServices = Answering(identity),
        });

        Assert.Equal(Program.ExitOk, exit);
        var events = EventLines(console.Stdout);
        Assert.Equal([DriveEventKinds.LoginDeviceCode, DriveEventKinds.LoginCompleted], events.Select(e => e.GetProperty("kind").GetString()));
        Assert.Equal("WXYZ-1234", events[0].GetProperty("user_code").GetString());
        Assert.Equal("https://microsoft.com/devicelogin", events[0].GetProperty("verification_uri").GetString());
        Assert.Equal("pro", events[1].GetProperty("account").GetString());
        Assert.DoesNotContain("Signed in", console.Stdout, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(Path.Combine(credentials, "drive"), "pro-*.json"));
    }

    [Fact]
    public async Task Check_WithEvents_WhenTheAccountNeedsASignIn_WritesOneErrorEventWithItsCode()
    {
        using var scratch = new ScriptScratch();
        WriteOneDriveSettings(scratch, Path.Combine(scratch.Root, "credentials"));
        using var console = new ConversationConsole();

        var exit = await DriveCommand.ExecuteCheckAsync(new DriveCheckCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "pro", Events = "jsonl" });

        Assert.Equal(Program.ExitScriptError, exit);
        var error = Assert.Single(EventLines(console.Stdout));
        Assert.Equal(DriveEventKinds.Error, error.GetProperty("kind").GetString());
        Assert.Equal("LoginRequired", error.GetProperty("code").GetString());
        Assert.DoesNotContain("drive check", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_WithEvents_WritesWhatTheRootFolderHolds_AndTheSentence()
    {
        using var scratch = new ScriptScratch();
        var credentials = Path.Combine(scratch.Root, "credentials");
        WriteOneDriveSettings(scratch, credentials);
        using (var identity = new FakeHttpMessageHandler())
        using (new ConversationConsole())
        {
            identity.Map(HttpMethod.Post, Authority + "devicecode", FakeHttpResponse.Json(
                """{"device_code":"dev","user_code":"C","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":1}"""));
            identity.Map(HttpMethod.Post, Authority + "token", FakeHttpResponse.Json("""{"access_token":"access-1","refresh_token":"refresh-1","expires_in":3600}"""));
            Assert.Equal(Program.ExitOk, await DriveCommand.ExecuteLoginAsync(new DriveLoginCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root, Account = "pro", Events = "jsonl", ConfigureTestServices = Answering(identity),
            }));
        }

        using var graph = new FakeHttpMessageHandler();
        graph.Map(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive/root:/Orkeon", FakeHttpResponse.Json(
            """{"id":"root-id","name":"Orkeon","folder":{},"parentReference":{"path":"/drive/root:"}}"""));
        graph.Map(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive/items/root-id/children", FakeHttpResponse.Json(
            """{"value":[{"id":"f1","name":"a.txt","file":{"mimeType":"text/plain"},"parentReference":{"id":"root-id"}}]}"""));
        using var console = new ConversationConsole();

        var exit = await DriveCommand.ExecuteCheckAsync(new DriveCheckCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root, Account = "pro", Events = "jsonl", ConfigureTestServices = Answering(graph),
        });

        Assert.Equal(Program.ExitOk, exit);
        var completed = Assert.Single(EventLines(console.Stdout));
        Assert.Equal(DriveEventKinds.CheckCompleted, completed.GetProperty("kind").GetString());
        Assert.Equal(("/Orkeon", "Orkeon", 1, false), (completed.GetProperty("root_path").GetString(), completed.GetProperty("root_name").GetString(),
            completed.GetProperty("items").GetInt32(), completed.GetProperty("more").GetBoolean()));
        Assert.Equal(DriveCommand.RenderCheck(new Orkeon.Tools.Drive.Administration.DriveCheckResult
        {
            Account = "pro", RootName = "Orkeon", RootPath = "/Orkeon", Items = 1, More = false,
        }), completed.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task An_events_format_it_does_not_know_is_refused_before_anything_else()
    {
        using var scratch = new ScriptScratch();
        WriteOneDriveSettings(scratch, Path.Combine(scratch.Root, "credentials"));
        using var console = new ConversationConsole();

        var exit = await DriveCommand.ExecuteLoginAsync(new DriveLoginCommandOptions { WorkingDirectoryOverride = scratch.Root, Account = "pro", Events = "xml" });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("unsupported --events format 'xml'", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(console.Stdout);
    }

    private static List<JsonElement> EventLines(string stdout) =>
        stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();

    private static void WriteOneDriveSettings(ScriptScratch scratch, string credentials) =>
        scratch.WriteFile("appsettings.json", $$"""
            { "Orkeon": { "Tools": { "Drive": {
                "CredentialsDirectory": {{JsonSerializer.Serialize(credentials)}},
                "Accounts": {
                  "pro": { "Provider": "OneDrive", "Rights": "Read", "Root": "Orkeon", "Auth": { "ClientId": "client-123" } }
                } } } } }
            """);

    private static Action<HostBuilderContext, IServiceCollection> Answering(HttpMessageHandler handler) =>
        (_, services) => services.AddHttpClient("orkeon.drive").ConfigurePrimaryHttpMessageHandler(() => handler);
}
