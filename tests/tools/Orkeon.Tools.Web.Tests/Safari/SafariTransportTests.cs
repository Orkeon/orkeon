using System.Text.Json.Nodes;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Safari;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests.Safari;

public sealed class SafariTransportTests : IAsyncDisposable
{
    private readonly FakeSafariDriver _driver = new();
    private readonly List<JsonObject> _events = [];
    private SafariTransport? _transport;
    private int _id;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_transport is not null)
            await _transport.DisposeAsync();
        _driver.Dispose();
    }

    [Fact]
    public async Task Should_SayWhatToDo_When_TheDriverRefusesTheSession()
    {
        _driver.Refused["/session"] = "session not created";

        var refused = await Assert.ThrowsAsync<FirefoxException>(ConnectAsync);

        Assert.Contains("sudo safaridriver --enable", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ListTheTabs()
    {
        var answer = await SendAsync("browsingContext.getTree", new JsonObject { ["maxDepth"] = 0 });

        var tab = Assert.Single(answer["result"]!["contexts"]!.AsArray());
        Assert.Equal("one", tab!["context"]!.GetValue<string>());
        Assert.Equal("http://site/", tab["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task Should_GiveTheNodesOfAFunctionTheirId()
    {
        _driver.Results.Enqueue(new JsonArray("text", new JsonObject { ["element-6066-11e4-a52e-4f735466cecf"] = "n1" }));

        var answer = await SendAsync("script.callFunction", new JsonObject
        {
            ["functionDeclaration"] = "(limit, node) => []",
            ["target"] = new JsonObject { ["context"] = "one" },
            ["arguments"] = new JsonArray(
                new JsonObject { ["type"] = "number", ["value"] = 5 }, new JsonObject { ["sharedId"] = "n0" }),
        });

        var values = answer["result"]!["result"]!["value"]!.AsArray();
        Assert.Equal("text", values[0]!["value"]!.GetValue<string>());
        Assert.Equal("n1", values[1]!["sharedId"]!.GetValue<string>());
        var sent = Assert.Single(_driver.Scripts)["args"]!.AsArray();
        Assert.Equal(5, sent[0]!.GetValue<int>());
        Assert.Equal("n0", sent[1]!["element-6066-11e4-a52e-4f735466cecf"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("stale element reference", "no such node")]
    [InlineData("no such window", "no such frame")]
    [InlineData("script timeout", "timeout")]
    public async Task Should_NameTheErrorsAsBiDiDoes(string classic, string expected)
    {
        _driver.Refused["execute/async"] = classic;

        var answer = await SendAsync("script.evaluate", Evaluation("1"));

        Assert.Equal("error", answer["type"]!.GetValue<string>());
        Assert.Equal(expected, answer["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Should_ReportWhatAnAsynchronousScriptThrows()
    {
        _driver.Results.Enqueue("""{"error":"TypeError: no"}""");

        var answer = await SendAsync("script.evaluate", Evaluation("boom()"));

        Assert.Equal("exception", answer["result"]!["type"]!.GetValue<string>());
        Assert.Equal("TypeError: no", answer["result"]!["exceptionDetails"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Should_DismissTheDialogAnActionOpened_AndAnnounceIt()
    {
        _driver.OnActions = () => _driver.Dialog = "Sure?";

        await ClickAsync();

        var raised = Assert.Single(_events, e => Method(e) == "browsingContext.userPromptOpened");
        Assert.Equal("Sure?", raised["params"]!["message"]!.GetValue<string>());
        Assert.Contains("POST alert/dismiss", _driver.Calls);
    }

    [Fact]
    public async Task Should_AnnounceTheTabsOpenedAndClosed_WithTheirOpener()
    {
        _driver.OnActions = () => _driver.Handles.Add("two");
        await ClickAsync();
        _driver.Handles.Remove("two");
        await SendAsync("session.subscribe", []);

        var opened = Assert.Single(_events, e => Method(e) == "browsingContext.contextCreated");
        Assert.Equal("two", opened["params"]!["context"]!.GetValue<string>());
        Assert.Equal("one", opened["params"]!["originalOpener"]!.GetValue<string>());
        Assert.Single(_events, e => Method(e) == "browsingContext.contextDestroyed");
    }

    [Fact]
    public async Task Should_AnnounceThePageLeft_AndTheOneLoaded()
    {
        _driver.OnActions = () => _driver.Watch = new JsonArray(true, "complete");
        await ClickAsync();
        Assert.Equal("browsingContext.navigationStarted", Method(Assert.Single(_events)));

        _driver.Watch = new JsonArray(null, "complete");
        await SendAsync("browsingContext.activate", new JsonObject { ["context"] = "one" });

        Assert.Equal("browsingContext.load", Method(_events[^1]));
    }

    [Fact]
    public async Task Should_SendTheElementOfAPointer_AsClassicWebDriverNamesIt()
    {
        await ClickAsync();

        var move = Assert.Single(_driver.Actions)["actions"]![0]!["actions"]![0]!;
        Assert.Equal("n1", move["origin"]!["element-6066-11e4-a52e-4f735466cecf"]!.GetValue<string>());
    }

    [Fact]
    public async Task Should_EndTheSessionOnce_When_TheBrowserIsClosed()
    {
        await SendAsync("browser.close", []);

        Assert.Null(await _transport!.ReceiveAsync(Ct));
        await _transport.DisposeAsync();
        Assert.Single(_driver.Calls, call => call == "DELETE /session/s1");
    }

    [Fact]
    public async Task Should_EndTheSession_When_Disposed()
    {
        await ConnectAsync();

        await _transport!.DisposeAsync();

        Assert.Single(_driver.Calls, call => call == "DELETE /session/s1");
    }

    private static string Method(JsonObject message) => message["method"]!.GetValue<string>();

    private static JsonObject Evaluation(string expression) => new()
    {
        ["expression"] = expression,
        ["target"] = new JsonObject { ["context"] = "one" },
        ["awaitPromise"] = true,
    };

    private Task<JsonObject> ClickAsync() => SendAsync("input.performActions", new JsonObject
    {
        ["context"] = "one",
        ["actions"] = new JsonArray(new JsonObject
        {
            ["type"] = "pointer",
            ["id"] = "mouse",
            ["actions"] = new JsonArray(new JsonObject
            {
                ["type"] = "pointerMove",
                ["x"] = 0,
                ["y"] = 0,
                ["origin"] = new JsonObject
                {
                    ["type"] = "element",
                    ["element"] = new JsonObject { ["sharedId"] = "n1" },
                },
            }),
        }),
    });

    private async Task ConnectAsync() =>
        _transport ??= await SafariTransport.ConnectAsync(new Uri("http://driver/"), TimeSpan.FromMinutes(1), Ct, _driver);

    private async Task<JsonObject> SendAsync(string method, JsonObject parameters)
    {
        await ConnectAsync();
        var id = ++_id;
        await _transport!.SendAsync(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString(), Ct);
        while (await _transport.ReceiveAsync(Ct) is { } text)
        {
            var message = JsonNode.Parse(text)!.AsObject();
            if (message["id"] is not null)
                return message;
            _events.Add(message);
        }

        throw new InvalidOperationException("The channel closed.");
    }
}
