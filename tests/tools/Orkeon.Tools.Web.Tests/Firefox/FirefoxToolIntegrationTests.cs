using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests.Firefox;

/// <summary>
/// The <c>firefox</c> tool on the Firefox of the machine, headless, against pages served on the
/// loopback: what the unit tests assume of the browser (the events it sends, the dialogs, the tabs)
/// is checked here. Skipped where Firefox is not installed.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FirefoxToolIntegrationTests : IDisposable
{
    private const string HomePage = """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>Orkeon test page</title></head><body>
        <nav aria-label="Main"><a href="second">Second page</a> <a href="third" target="_blank">Third page</a></nav>
        <h1>Welcome</h1>
        <p>This page is served to a real Firefox.</p>
        <h2>Greeting</h2>
        <form id="form"><label for="who">Name</label> <input id="who" type="text">
        <button type="button" id="greet">Greet</button></form>
        <p id="out">Nobody yet.</p>
        <h2>Questions</h2>
        <p>A button that asks before it answers.</p>
        <button type="button" id="ask">Ask</button>
        <script>
        const out = document.getElementById('out');
        const who = document.getElementById('who');
        document.getElementById('greet').addEventListener('click', () => { out.textContent = 'Hello, ' + who.value + '.'; });
        document.getElementById('form').addEventListener('submit', e => { e.preventDefault(); out.textContent = 'Submitted ' + who.value + '.'; });
        document.getElementById('ask').addEventListener('click', () => { alert('Are you sure?'); out.textContent = 'Asked.'; });
        </script>
        </body></html>
        """;

    private readonly PageServer _server = new();
    private readonly FakeFileSystemService _files = new FakeFileSystemService().AddMount("/output", FileAccessRights.ReadWrite);
    private readonly FakeEphemeralCollectionSearch _search = new();
    private readonly FirefoxToolOptions _options = new() { Headless = true };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Home => _server.BaseAddress.ToString();

    public void Dispose() => _server.Dispose();

    private FirefoxTool Tool()
    {
        Assert.SkipUnless(
            new FirefoxProcessLauncher(_options).LocateOnHost() is not null,
            "Firefox is not installed on this machine.");
        return new FirefoxTool(_options, _files, new AllowAllUrlValidator(), _search);
    }

    private static async Task<JsonObject> CallAsync(FirefoxTool tool, string action, params (string Name, object? Value)[] arguments)
    {
        var parameters = new Dictionary<string, object?> { ["action"] = action };
        foreach (var (name, value) in arguments)
            parameters[name] = value;

        var response = await tool.CallAsync(new ToolCallRequest(ToolName: "firefox", Parameters: parameters), Ct);
        Assert.True(response.Success, response.Error);
        return Assert.IsType<JsonObject>(JsonSerializer.SerializeToNode(response.Result));
    }

    private static string Text(JsonObject body, string key) => body[key]?.GetValue<string>() ?? string.Empty;

    /// <summary>One line per node: <c>kind role name</c>.</summary>
    private static string[] Lines(JsonObject body) =>
        [.. body["nodes"]!.AsArray().Select(node => $"{node!["kind"]} {node["role"]} {node["name"]}")];

    private static async Task<string> NodeAsync(FirefoxTool tool, string kind, string name)
    {
        var found = await CallAsync(tool, "find", ("kind", kind), ("name", name));
        return Assert.Single(found["nodes"]!.AsArray())!["id"]!.GetValue<string>();
    }

    [Fact]
    public async Task Should_ReadAPage_FillItsField_AndClickItsButton()
    {
        using var tool = Tool();
        await using var run = AgentRunScope.Begin("agent-1");

        var page = await CallAsync(tool, "navigate", ("url", Home));

        Assert.Equal(Home, Text(page, "url"));
        Assert.Equal("Orkeon test page", Text(page, "title"));
        var lines = Lines(page);
        Assert.Contains("section h1 Welcome", lines);
        Assert.Contains("section h2 Greeting", lines);
        Assert.Contains("link link Second page", lines);
        Assert.Contains("control textbox Name", lines);
        Assert.Contains("control button Greet", lines);

        await CallAsync(tool, "type", ("node_id", await NodeAsync(tool, "control", "Name")), ("text", "Ada"));
        await CallAsync(tool, "click", ("node_id", await NodeAsync(tool, "control", "Greet")));
        Assert.Contains("Hello, Ada.", Text(await CallAsync(tool, "read"), "text"), StringComparison.Ordinal);

        // The field kept the focus of the typing: Enter submits its form.
        await CallAsync(tool, "type", ("node_id", await NodeAsync(tool, "control", "Name")), ("text", "Grace"));
        await CallAsync(tool, "press", ("key", "Enter"));
        Assert.Contains("Submitted Grace.", Text(await CallAsync(tool, "read"), "text"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_FollowALink_GoBack_AndFollowTheTabALinkOpens()
    {
        using var tool = Tool();
        await using var run = AgentRunScope.Begin("agent-1");
        await CallAsync(tool, "navigate", ("url", Home));

        var second = await CallAsync(tool, "click", ("node_id", await NodeAsync(tool, "link", "Second page")));
        Assert.Equal(Home + "second", Text(second, "url"));
        Assert.Equal("Second page", Text(second, "title"));

        var back = await CallAsync(tool, "back");
        Assert.Equal(Home, Text(back, "url"));

        var third = await CallAsync(tool, "click", ("node_id", await NodeAsync(tool, "link", "Third page")));
        Assert.Equal(Home + "third", Text(third, "url"));
        Assert.Contains("section h1 Third page", Lines(await CallAsync(tool, "snapshot")));
    }

    [Fact]
    public async Task Should_DismissTheDialogOfAPage_AndSayWhatItAsked()
    {
        using var tool = Tool();
        await using var run = AgentRunScope.Begin("agent-1");
        await CallAsync(tool, "navigate", ("url", Home));

        var asked = await CallAsync(tool, "click", ("node_id", await NodeAsync(tool, "control", "Ask")));

        Assert.Equal(["alert: Are you sure?"], asked["dialogs"]!.AsArray().Select(dialog => dialog!.GetValue<string>()));
        Assert.Contains("Asked.", Text(await CallAsync(tool, "read"), "text"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_EvaluateAScript_SearchThePage_AndSaveAScreenshot()
    {
        _search.Scores["asks before it answers"] = 0.8;
        using var tool = Tool();
        await using var run = AgentRunScope.Begin("agent-1");
        await CallAsync(tool, "navigate", ("url", Home));

        var value = await CallAsync(tool, "evaluate", ("script", "document.querySelectorAll('h2').length"));
        Assert.Equal("2", Text(value, "value"));

        var hit = Assert.Single((await CallAsync(tool, "search", ("query", "who asks?")))["hits"]!.AsArray());
        Assert.Equal("Questions", Text(hit!.AsObject(), "name"));

        var shot = await CallAsync(tool, "screenshot", ("save_to_path", "/output/page.png"));
        Assert.Equal("/output/page.png", Text(shot, "saved_path"));
        var png = await _files.TryReadAllBytesAsync("/output/page.png", Ct);
        Assert.NotNull(png);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);

        var closed = await CallAsync(tool, "close");
        Assert.Equal("The browser was closed.", Text(closed, "value"));
    }

    /// <summary>Three pages on a loopback port: the home page, and two that only carry a heading.</summary>
    private sealed class PageServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Task _pump;

        public PageServer()
        {
            (_listener, BaseAddress) = LoopbackPorts.StartListener();
            _pump = PumpAsync();
        }

        /// <summary>The address of the home page, with a final slash.</summary>
        public Uri BaseAddress { get; }

        public void Dispose()
        {
            // Close() alone: after Stop(), it would bind the port again for an instant.
            _listener.Close();
            _pump.GetAwaiter().GetResult();
        }

        private static string PageOf(string path) => path switch
        {
            "/" => HomePage,
            "/second" => "<!doctype html><html><head><title>Second page</title></head><body><h1>Second page</h1><p>Reached by a link.</p></body></html>",
            "/third" => "<!doctype html><html><head><title>Third page</title></head><body><h1>Third page</h1><p>Opened in a tab of its own.</p></body></html>",
            _ => string.Empty,
        };

        private async Task PumpAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                var page = PageOf(context.Request.Url?.AbsolutePath ?? string.Empty);
                try
                {
                    context.Response.StatusCode = page.Length == 0 ? 404 : 200;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    var bytes = Encoding.UTF8.GetBytes(page);
                    await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                    context.Response.Close();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
                {
                    // The browser left before the answer: nothing to serve it.
                }
            }
        }
    }
}
