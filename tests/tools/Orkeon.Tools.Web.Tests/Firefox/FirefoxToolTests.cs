using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests.Firefox;

/// <summary>The <c>firefox</c> tool as an agent calls it: snake_case arguments in, a dictionary out.</summary>
public class FirefoxToolTests : FirefoxSessionTestBase
{
    private const string Intranet = "http://intranet.test/";

    private readonly StubUrlValidator _policy = new();
    private readonly FakeFileSystemService _files = new FakeFileSystemService().AddMount("/output", FileAccessRights.ReadWrite);
    private readonly FirefoxToolOptions _options = new();

    private FakeFirefoxBrowser Browser => Launcher.Last;

    private readonly FakeEphemeralCollectionSearch _search = new();

    private FirefoxTool Tool(bool withSearch = true) =>
        new(Launcher, _options, _files, _policy, withSearch ? _search : null, logger: null, Timings);

    private static async Task<ToolCallResponse> CallAsync(FirefoxTool tool, string? action, params (string Name, object? Value)[] arguments)
    {
        var parameters = new Dictionary<string, object?> { ["action"] = action };
        foreach (var (name, value) in arguments)
            parameters[name] = value;

        return await tool.CallAsync(new ToolCallRequest(ToolName: "firefox", Parameters: parameters), Ct);
    }

    /// <summary>What a successful call answered, as the JSON the model reads.</summary>
    private static JsonObject Body(ToolCallResponse response)
    {
        Assert.True(response.Success, response.Error);
        return Assert.IsType<JsonObject>(JsonSerializer.SerializeToNode(response.Result));
    }

    private static string[] Ids(JsonObject body) =>
        [.. body["nodes"]!.AsArray().Select(node => node!["id"]!.GetValue<string>())];

    /// <summary>One line per node: <c>id kind role name &lt;parent&gt; text</c>.</summary>
    private static string[] Lines(JsonObject body) =>
        [.. body["nodes"]!.AsArray().Select(node =>
            $"{node!["id"]} {node["kind"]} {node["role"]} {node["name"]} <{node["parent_id"]}> {node["text"]}".TrimEnd())];

    private int OutlineReads() => Browser.Wire.ParamsOf("script.callFunction")
        .Count(call => call["functionDeclaration"]!.GetValue<string>().Contains("maxElements", StringComparison.Ordinal));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("scroll")]
    public async Task Should_RefuseTheCall_When_TheActionIsMissingOrUnknown(string? action)
    {
        using var tool = Tool();

        var result = await CallAsync(tool, action);

        Assert.False(result.Success);
        Assert.Contains("action", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_NameTheActions_When_TheActionIsUnknown()
    {
        using var tool = Tool();

        var result = await CallAsync(tool, "scroll");

        // The schema of the tool lists its actions: the framework refuses another one before the tool runs.
        Assert.Equal(
            "Parameter 'action' must be one of: navigate, back, snapshot, read, find, search, click, type, press, evaluate, screenshot, tabs, new_tab, switch_tab, close_tab, close",
            result.Error);
    }

    [Theory]
    [InlineData("navigate", null, null, "url is required for action 'navigate'")]
    [InlineData("navigate", "url", "ftp://example.test/", "is not an absolute http or https address")]
    [InlineData("navigate", "url", "example.test/docs", "is not an absolute http or https address")]
    [InlineData("click", null, null, "node_id is required for action 'click'")]
    [InlineData("click", "node_id", "the third link", "is not a node id")]
    [InlineData("type", "text", "hello", "node_id is required for action 'type'")]
    [InlineData("type", "node_id", "p1#3", "text is required for action 'type'")]
    [InlineData("press", null, null, "key is required for action 'press'")]
    [InlineData("press", "key", "Bogus", "'Bogus' is not a key. Enter, Tab")]
    [InlineData("evaluate", null, null, "script is required for action 'evaluate'")]
    [InlineData("screenshot", null, null, "save_to_path is required for action 'screenshot'")]
    [InlineData("switch_tab", null, null, "tab is required for action 'switch_tab'")]
    [InlineData("new_tab", "url", "ftp://example.test/", "is not an absolute http or https address")]
    [InlineData("search", null, null, "query is required for action 'search'")]
    [InlineData("find", "kind", "image", "Parameter 'kind' must be one of: section, link, control")]
    [InlineData("snapshot", "max_nodes", 0, "max_nodes must be greater than 0")]
    [InlineData("snapshot", "offset", -1, "offset must not be negative")]
    public async Task Should_RefuseTheCall_BeforeAnyBrowserStarts_When_AnArgumentIsMissingOrWrong(
        string action, string? name, object? value, string expected)
    {
        using var tool = Tool();

        var result = name is null ? await CallAsync(tool, action) : await CallAsync(tool, action, (name, value));

        Assert.False(result.Success);
        Assert.Contains(expected, result.Error, StringComparison.Ordinal);
        Assert.Empty(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_OpenAPage_AndReturnItsOutline()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();

        var result = await CallAsync(tool, "navigate", ("url", Home));

        var body = Body(result);
        Assert.True(result.AlreadyBounded);
        Assert.Equal("navigate", body["action"]!.GetValue<string>());
        Assert.Equal(Home, body["url"]!.GetValue<string>());
        Assert.Equal("Example", body["title"]!.GetValue<string>());
        Assert.Equal(5, body["total_nodes"]!.GetValue<int>());
        Assert.False(body["truncated"]!.GetValue<bool>());
        Assert.Equal(
            [
                "p1#0 section navigation Main <>",
                $"p1#1 link link Docs <p1#0> {Docs}",
                "p1#2 section h1 Guide <> Install it.",
                "p1#3 control textbox Search <p1#2> old",
                "p1#4 control button Go <p1#2>",
            ],
            Lines(body));
    }

    [Fact]
    public async Task Should_StartNoBrowser_When_TheAddressIsRefused()
    {
        _policy.RefusedHosts.Add("intranet.test");
        using var tool = Tool();

        var result = await CallAsync(tool, "navigate", ("url", $"{Intranet}admin"));

        Assert.False(result.Success);
        Assert.Equal($"'{Intranet}admin' was not opened: the host is private.", result.Error);
        Assert.Empty(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_LeaveThePageForABlankOne_When_TheAddressItLandsOnIsRefused()
    {
        _policy.RefusedHosts.Add("intranet.test");
        Launcher.Configure += browser => browser.Redirects[Home] = Intranet;
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();

        var result = await CallAsync(tool, "navigate", ("url", Home));
        var after = Body(await CallAsync(tool, "snapshot"));

        Assert.False(result.Success);
        Assert.Equal($"The browser reached '{Intranet}', which it may not show (the host is private): the page was left for a blank one.", result.Error);
        Assert.Equal("about:blank", after["url"]!.GetValue<string>());
        Assert.Empty(Ids(after));
        Assert.False(Assert.Single(Launcher.Browsers).Disposed);
    }

    [Fact]
    public async Task Should_LeaveThePageForABlankOne_When_AClickLeadsToARefusedAddress()
    {
        _policy.RefusedHosts.Add("intranet.test");
        Launcher.Configure += browser => browser.ClickLeadsTo[FakeFirefoxBrowser.Reference(Home, 1)] = Intranet;
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var result = await CallAsync(tool, "click", ("node_id", "p1#1"));
        var again = await CallAsync(tool, "click", ("node_id", "p1#1"));

        Assert.False(result.Success);
        Assert.Contains($"The browser reached '{Intranet}'", result.Error, StringComparison.Ordinal);
        Assert.Equal("about:blank", Browser.Url);
        Assert.False(again.Success);
        Assert.Equal("The page changed since it was last read: call snapshot, then use the node ids it returns.", again.Error);
    }

    [Fact]
    public async Task Should_OpenOnlyHttpPages_WhateverThePageDoes()
    {
        Launcher.Configure += browser => browser.ClickLeadsTo[FakeFirefoxBrowser.Reference(Home, 1)] = "file:///etc/passwd";
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var result = await CallAsync(tool, "click", ("node_id", "p1#1"));

        Assert.False(result.Success);
        Assert.Contains("only http and https pages are opened", result.Error, StringComparison.Ordinal);
        Assert.Equal("about:blank", Browser.Url);
        Assert.DoesNotContain("file:///etc/passwd", _policy.Checked);
    }

    [Fact]
    public async Task Should_KeepTheBrowserAndItsPage_BetweenTheCallsOfARun()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();

        await CallAsync(tool, "navigate", ("url", Home));
        var body = Body(await CallAsync(tool, "read"));

        Assert.Equal(Home, body["url"]!.GetValue<string>());
        Assert.Equal("Welcome to the example. Guide Install it. More", body["text"]!.GetValue<string>());
        Assert.False(body["truncated"]!.GetValue<bool>());
        Assert.False(Assert.Single(Launcher.Browsers).Disposed);
        Assert.Equal(1, OutlineReads());
    }

    [Fact]
    public async Task Should_CloseTheBrowserAfterTheCall_When_NoRunHoldsIt()
    {
        using var tool = Tool();

        var body = Body(await CallAsync(tool, "navigate", ("url", Home)));

        Assert.Equal(Home, body["url"]!.GetValue<string>());
        Assert.True(Assert.Single(Launcher.Browsers).Disposed);
    }

    [Fact]
    public async Task Should_SayWhy_When_FirefoxDoesNotStart()
    {
        Launcher.LaunchFailure = new FirefoxException("Firefox was not found: install it, or set Orkeon:Tools:Firefox:ExecutablePath.");
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();

        var result = await CallAsync(tool, "navigate", ("url", Home));

        Assert.False(result.Success);
        Assert.Equal("Firefox was not found: install it, or set Orkeon:Tools:Firefox:ExecutablePath.", result.Error);
    }

    [Fact]
    public async Task Should_GoBack_AndReturnTheOutlineOfThePageBefore()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));
        await CallAsync(tool, "navigate", ("url", Docs));

        var body = Body(await CallAsync(tool, "back"));

        Assert.Equal(Home, body["url"]!.GetValue<string>());
        Assert.Equal(["p3#0", "p3#1", "p3#2", "p3#3", "p3#4"], Ids(body));
    }

    [Fact]
    public async Task Should_GiveTheNextNodesOfASnapshot_WithoutReadingThePageAgain()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var first = Body(await CallAsync(tool, "snapshot", ("max_nodes", 2)));
        var next = Body(await CallAsync(tool, "snapshot", ("max_nodes", 2), ("offset", 2)));
        var last = Body(await CallAsync(tool, "snapshot", ("max_nodes", 2), ("offset", 4)));

        Assert.Equal(["p2#0", "p2#1"], Ids(first));
        Assert.True(first["truncated"]!.GetValue<bool>());
        Assert.Equal(5, first["total_nodes"]!.GetValue<int>());
        Assert.Equal(["p2#2", "p2#3"], Ids(next));
        Assert.True(next["truncated"]!.GetValue<bool>());
        Assert.Equal(["p2#4"], Ids(last));
        Assert.False(last["truncated"]!.GetValue<bool>());
        Assert.Equal(2, OutlineReads());
    }

    [Fact]
    public async Task Should_ReadThePageAgain_When_AnActionRanSinceItWasRead()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));
        await CallAsync(tool, "press", ("key", "Tab"));

        var body = Body(await CallAsync(tool, "find", ("kind", "control")));

        Assert.Equal(["p2#3", "p2#4"], Ids(body));
        Assert.Equal(2, OutlineReads());
    }

    [Fact]
    public async Task Should_SayThePageHoldsMore_When_ItsOutlineWasCut()
    {
        Launcher.Configure += browser => browser.Pages[Home].Truncated = true;
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();

        var body = Body(await CallAsync(tool, "navigate", ("url", Home)));

        Assert.True(body["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Should_ListAtMostTwoHundredNodes_WhateverIsAsked()
    {
        Launcher.Configure += browser =>
        {
            var page = new FakePage { Title = "Many" };
            for (var i = 0; i < 250; i++)
                page.Link($"Link {i}", $"{Home}{i}");
            browser.Pages[Docs] = page;
        };
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();

        var byDefault = Body(await CallAsync(tool, "navigate", ("url", Docs)));
        var asked = Body(await CallAsync(tool, "snapshot", ("max_nodes", 1000)));

        Assert.Equal(40, Ids(byDefault).Length);
        Assert.Equal(200, Ids(asked).Length);
        Assert.Equal(250, asked["total_nodes"]!.GetValue<int>());
        Assert.True(asked["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Should_LeaveOutTheElementsThePageHides()
    {
        Launcher.Configure += browser => browser.Pages[Home].Control("textbox", "Trap", visible: false);
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();

        var body = Body(await CallAsync(tool, "navigate", ("url", Home)));

        Assert.Equal(5, body["total_nodes"]!.GetValue<int>());
        Assert.DoesNotContain("p1#5", Ids(body));
    }

    [Theory]
    [InlineData("kind", "control", "p1#3,p1#4")]
    [InlineData("kind", "link", "p1#1")]
    [InlineData("name", "gui", "p1#2")]
    [InlineData("node_id", "p1#0", "p1#1")]
    [InlineData("node_id", "p1#2", "p1#3,p1#4")]
    [InlineData("node_id", "p1#page", "p1#0,p1#1,p1#2,p1#3,p1#4")]
    [InlineData("name", "nothing like it", "")]
    public async Task Should_FindTheNodesOfAKind_OfAName_OrUnderANode(string name, string value, string expected)
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var body = Body(await CallAsync(tool, "find", (name, value)));

        Assert.Equal(expected, string.Join(',', Ids(body)));
        Assert.Equal(Ids(body).Length, body["total_nodes"]!.GetValue<int>());
        Assert.Equal(1, OutlineReads());
    }

    [Fact]
    public async Task Should_ReadTheTextOfThePagePartByPart()
    {
        _options.MaxTextLength = 10;
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var first = Body(await CallAsync(tool, "read"));
        var next = Body(await CallAsync(tool, "read", ("offset", 10)));
        var end = Body(await CallAsync(tool, "read", ("offset", 40)));

        Assert.Equal("Welcome to", first["text"]!.GetValue<string>());
        Assert.True(first["truncated"]!.GetValue<bool>());
        Assert.Equal(" the examp", next["text"]!.GetValue<string>());
        Assert.Equal(". More", end["text"]!.GetValue<string>());
        Assert.False(end["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Should_ReadANode_WithTheNodesItHolds()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var section = Body(await CallAsync(tool, "read", ("node_id", "p1#2")));
        var page = Body(await CallAsync(tool, "read", ("node_id", "p1#page")));

        Assert.Equal("Install it.", section["text"]!.GetValue<string>());
        Assert.Equal(["p1#3", "p1#4"], Ids(section));
        Assert.Equal("Welcome to the example.", page["text"]!.GetValue<string>());
        Assert.Equal(["p1#0", "p1#2"], Ids(page));
    }

    private static string[] Hits(JsonObject body) =>
        [.. body["hits"]!.AsArray().Select(hit => $"{hit!["node_id"]} {hit["score"]} {hit["name"]}: {hit["excerpt"]}")];

    /// <summary>What names and fills each source of a search: its location, then its options in order.</summary>
    private static string[] Identity(Orkeon.Rag.Abstractions.Models.EphemeralSearchRequest request) =>
        [.. request.Sources.Select(source =>
            $"{source.Location}|{string.Join('|', source.Options.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => $"{o.Key}={o.Value}"))}")];

    [Fact]
    public async Task Should_FindTheSectionsClosestInMeaningToAQuery()
    {
        _search.Scores["Install"] = 0.9128;
        _search.Scores["Welcome"] = 0.4;
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var body = Body(await CallAsync(tool, "search", ("query", " how do I set it up? ")));

        Assert.Equal(
            [
                "p1#2 0.913 Guide: Guide\nInstall it.",
                "p1#page 0.4 Example: Example\nWelcome to the example.",
            ],
            Hits(body));
        var request = Assert.Single(_search.Requests);
        Assert.Equal("how do I set it up?", request.Query);
        Assert.Equal("firefox", request.CollectionPrefix);
        Assert.Equal([$"{Home}#page", $"{Home}#2"], request.Sources.Select(source => source.Location));
        Assert.Equal(1, OutlineReads());
    }

    [Fact]
    public async Task Should_NameASourceByItsPlaceInThePage_SoThatAPageReadAgainIsNotEmbeddedAgain()
    {
        _search.Scores["Install"] = 0.9;
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));
        await CallAsync(tool, "search", ("query", "install"));
        await CallAsync(tool, "snapshot");

        var body = Body(await CallAsync(tool, "search", ("query", "install")));

        Assert.Equal(["p2#2 0.9 Guide: Guide\nInstall it."], Hits(body));
        Assert.Equal(Identity(_search.Requests[0]), Identity(_search.Requests[1]));
        Assert.Equal([$"{Home}#page|content=Example\nWelcome to the example.|meta.element=page", $"{Home}#2|content=Guide\nInstall it.|meta.element=2"], Identity(_search.Requests[1]));
    }

    [Fact]
    public async Task Should_KeepTheBestPassageOfASection_AndNoMoreHitsThanAsked()
    {
        _search.Scores["Install"] = 0.5;
        _search.Scores["Guide"] = 0.8;
        _search.Scores["Welcome"] = 0.7;
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var all = Body(await CallAsync(tool, "search", ("query", "guide")));
        var one = Body(await CallAsync(tool, "search", ("query", "guide"), ("max_nodes", 1)));

        Assert.Equal(["p1#2 0.8 Guide: Guide\nInstall it.", "p1#page 0.7 Example: Example\nWelcome to the example."], Hits(all));
        Assert.Equal(["p1#2 0.8 Guide: Guide\nInstall it."], Hits(one));
    }

    [Fact]
    public async Task Should_SearchNothing_When_ThePageHoldsNoText()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();

        var body = Body(await CallAsync(tool, "search", ("query", "anything")));

        Assert.Empty(Hits(body));
        Assert.Empty(_search.Requests);
    }

    [Fact]
    public async Task Should_SayHowToGoOn_When_TheHostHasNoSemanticSearch()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool(withSearch: false);
        await CallAsync(tool, "navigate", ("url", Home));

        var result = await CallAsync(tool, "search", ("query", "install"));
        var body = Body(await CallAsync(tool, "find", ("name", "guide")));

        Assert.False(result.Success);
        Assert.Equal("search needs the RAG subsystem, which this host does not provide: use find and read instead.", result.Error);
        Assert.Equal(["p1#2"], Ids(body));
        Assert.False(Assert.Single(Launcher.Browsers).Disposed);
    }

    [Fact]
    public async Task Should_KeepTheBrowser_When_TheSearchFails()
    {
        _search.Failure = new InvalidOperationException("Ephemeral RAG search is not configured.");
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var result = await CallAsync(tool, "search", ("query", "install"));

        Assert.False(result.Success);
        Assert.Equal("The page could not be searched (Ephemeral RAG search is not configured.): use find and read instead.", result.Error);
        Assert.False(Assert.Single(Launcher.Browsers).Disposed);
    }

    [Fact]
    public async Task Should_ClickANode_AndReturnTheOutlineOfThePageItOpens()
    {
        Launcher.Configure += browser => browser.ClickLeadsTo[FakeFirefoxBrowser.Reference(Home, 1)] = Docs;
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var body = Body(await CallAsync(tool, "click", ("node_id", "p1#1")));
        var stale = await CallAsync(tool, "click", ("node_id", "p1#1"));

        Assert.Equal(Docs, body["url"]!.GetValue<string>());
        Assert.Equal("Docs", body["title"]!.GetValue<string>());
        Assert.Equal(["p2#0 section h1 Docs <> All of it."], Lines(body));
        Assert.False(stale.Success);
        Assert.Equal("The node id 'p1#1' belongs to an earlier reading of the page: call snapshot again.", stale.Error);
        Assert.False(Assert.Single(Launcher.Browsers).Disposed);
    }

    [Fact]
    public async Task Should_KeepTheNodeIds_When_AnActionLeavesThePageWhereItIs()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var typed = Body(await CallAsync(tool, "type", ("node_id", "p1#3"), ("text", "orkeon")));
        var clicked = Body(await CallAsync(tool, "click", ("node_id", "p1#4")));

        Assert.False(typed.ContainsKey("nodes"));
        Assert.Equal(Home, typed["url"]!.GetValue<string>());
        Assert.False(clicked.ContainsKey("nodes"));
        var keys = Browser.Wire.ParamsOf("input.performActions").First(p => p["actions"]![0]!["type"]!.GetValue<string>() == "key");
        Assert.Equal(
            "orkeon",
            string.Concat(keys["actions"]![0]!["actions"]!.AsArray()
                .Where(key => key!["type"]!.GetValue<string>() == "keyDown")
                .Select(key => key!["value"]!.GetValue<string>())));
        Assert.Equal(1, OutlineReads());
    }

    [Fact]
    public async Task Should_AskForASnapshot_When_NoPageWasReadYet()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();

        var result = await CallAsync(tool, "click", ("node_id", "p1#1"));

        Assert.False(result.Success);
        Assert.Equal("The page changed since it was last read: call snapshot, then use the node ids it returns.", result.Error);
    }

    [Fact]
    public async Task Should_RefuseToActOnThePageItself()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var result = await CallAsync(tool, "click", ("node_id", "p1#page"));

        Assert.False(result.Success);
        Assert.Equal("'p1#page' is the page, not one of its elements: give the id of a link or of a control.", result.Error);
    }

    [Fact]
    public async Task Should_PressAChord()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var body = Body(await CallAsync(tool, "press", ("key", "Control+a")));

        Assert.Equal("press", body["action"]!.GetValue<string>());
        var keys = Browser.Wire.ParamsOf("input.performActions")[^1]["actions"]![0]!["actions"]!.AsArray();
        Assert.Equal(4, keys.Count);
        Assert.Equal("a", keys[1]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Should_ReturnTheValueOfAScript_CutAtTheLimitOfAnAnswer()
    {
        _options.MaxTextLength = 5;
        Launcher.Configure += browser => browser.Script = script => script switch
        {
            "6 * 7" => new JsonObject { ["type"] = "number", ["value"] = 42 },
            "document.body.innerText" => new JsonObject { ["type"] = "string", ["value"] = "abcdefghij" },
            _ => null,
        };
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var number = Body(await CallAsync(tool, "evaluate", ("script", "6 * 7")));
        var text = Body(await CallAsync(tool, "evaluate", ("script", "document.body.innerText")));
        var thrown = await CallAsync(tool, "evaluate", ("script", "nope()"));

        Assert.Equal("42", number["value"]!.GetValue<string>());
        Assert.Equal("abcde...", text["value"]!.GetValue<string>());
        Assert.False(thrown.Success);
        Assert.Contains("nope is not defined", thrown.Error, StringComparison.Ordinal);
        Assert.False(Assert.Single(Launcher.Browsers).Disposed);
    }

    [Fact]
    public async Task Should_LeaveOutTheListsTheActionDidNotProduce()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();

        var navigated = Body(await CallAsync(tool, "navigate", ("url", Home)));
        var tabs = Body(await CallAsync(tool, "tabs"));

        // The outline comes with navigate; an empty tabs, hits or dialogs would read as "none" where none was asked.
        Assert.True(navigated.ContainsKey("nodes"));
        Assert.Equal(["action", "success", "url", "title", "truncated", "total_nodes", "nodes", "tab", "profile"], navigated.Select(pair => pair.Key));
        Assert.Equal(["action", "success", "url", "title", "truncated", "tab", "tabs", "profile"], tabs.Select(pair => pair.Key));
    }

    [Fact]
    public async Task Should_SayWhatTheDialogsOfThePageSaid()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));
        Browser.Script = _ =>
        {
            Browser.Emit("browsingContext.userPromptOpened", new JsonObject { ["type"] = "alert", ["message"] = "Saved." });
            return new JsonObject { ["type"] = "undefined" };
        };

        var body = Body(await CallAsync(tool, "evaluate", ("script", "save()")));

        Assert.Equal("""["alert: Saved."]""", body["dialogs"]!.ToJsonString());
    }

    [Fact]
    public async Task Should_WriteTheScreenshotThroughTheFileSystem()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var body = Body(await CallAsync(tool, "screenshot", ("save_to_path", "/output/page.png")));

        Assert.Equal("/output/page.png", body["saved_path"]!.GetValue<string>());
        Assert.Equal("PNG image, 4 bytes", body["text"]!.GetValue<string>());
        Assert.Equal(Browser.Screenshot, await _files.TryReadAllBytesAsync("/output/page.png", Ct));
    }

    [Fact]
    public async Task Should_TakeNoScreenshot_When_ThePathMayNotBeWritten()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var result = await CallAsync(tool, "screenshot", ("save_to_path", "/etc/page.png"));

        Assert.False(result.Success);
        Assert.StartsWith("The screenshot was not written to '/etc/page.png': ", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("browsingContext.captureScreenshot", Browser.Wire.Methods);
        Assert.False(Browser.Disposed);
    }

    [Fact]
    public async Task Should_CloseTheBrowserOfTheRun_AndStartAnotherAtTheNextAction()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var closed = Body(await CallAsync(tool, "close"));
        var again = Body(await CallAsync(tool, "close"));
        await CallAsync(tool, "navigate", ("url", Docs));

        Assert.Equal("The browser was closed.", closed["value"]!.GetValue<string>());
        Assert.Equal("No browser was open.", again["value"]!.GetValue<string>());
        Assert.Equal(2, Launcher.Browsers.Count);
        Assert.True(Launcher.Browsers[0].Disposed);
        Assert.False(Launcher.Browsers[1].Disposed);
    }

    [Fact]
    public async Task Should_OpenATab_ListTheTabs_AndSayWhichOneEveryAnswerDrives()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        var first = Body(await CallAsync(tool, "navigate", ("url", Home)));

        var opened = Body(await CallAsync(tool, "new_tab", ("url", Docs)));
        var tabs = Body(await CallAsync(tool, "tabs"));
        var switched = Body(await CallAsync(tool, "switch_tab", ("tab", "t1")));

        Assert.Equal("t1", first["tab"]!.GetValue<string>());
        Assert.Equal("t2", opened["tab"]!.GetValue<string>());
        Assert.Equal(Docs, opened["url"]!.GetValue<string>());
        Assert.Equal("Docs", opened["title"]!.GetValue<string>());
        Assert.Equal(
            ["t1 https://example.test/ Example false", "t2 https://example.test/docs Docs true"],
            tabs["tabs"]!.AsArray().Select(tab => $"{tab!["id"]} {tab["url"]} {tab["title"]} {tab["current"]}"));
        Assert.Equal("t1", switched["tab"]!.GetValue<string>());
        Assert.Equal(Home, switched["url"]!.GetValue<string>());
        Assert.Equal("Example", switched["title"]!.GetValue<string>());
        Assert.Equal(["tab-1"], Browser.Activated);
        Assert.Single(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_OpenABlankTab_When_NewTabNamesNoAddress()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));

        var opened = Body(await CallAsync(tool, "new_tab"));

        Assert.Equal("t2", opened["tab"]!.GetValue<string>());
        Assert.Equal("about:blank", opened["url"]!.GetValue<string>());
        Assert.Equal(["tab-1", "tab-2"], Browser.Tabs);
    }

    [Fact]
    public async Task Should_StartNoBrowser_When_NewTabNamesARefusedAddress()
    {
        _policy.RefusedHosts.Add("intranet.test");
        using var tool = Tool();

        var result = await CallAsync(tool, "new_tab", ("url", $"{Intranet}admin"));

        Assert.False(result.Success);
        Assert.Contains("was not opened", result.Error, StringComparison.Ordinal);
        Assert.Empty(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_CloseATab_TheCurrentOneByDefault_AndKeepTheLast()
    {
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home));
        await CallAsync(tool, "new_tab", ("url", Docs));
        await CallAsync(tool, "new_tab");

        var closed = Body(await CallAsync(tool, "close_tab"));
        var named = Body(await CallAsync(tool, "close_tab", ("tab", "t1")));
        var last = await CallAsync(tool, "close_tab");
        var unknown = await CallAsync(tool, "switch_tab", ("tab", "t9"));

        Assert.Equal("t2", closed["tab"]!.GetValue<string>());
        Assert.Equal(Docs, closed["url"]!.GetValue<string>());
        Assert.Equal("t2", named["tab"]!.GetValue<string>());
        Assert.False(last.Success);
        Assert.Contains("'t2' is the last tab of this agent", last.Error, StringComparison.Ordinal);
        Assert.False(unknown.Success);
        Assert.Contains("'t9' is not a tab of this agent", unknown.Error, StringComparison.Ordinal);
        Assert.Equal(["tab-2"], Browser.Tabs);
        Assert.False(Browser.Disposed);
    }

    [Fact]
    public async Task Should_StartNoBrowser_When_TheProfileIsNotOneTheHostAllows()
    {
        _options.AllowedProfiles.Add("research");
        using var tool = Tool();

        var result = await CallAsync(tool, "navigate", ("url", Home), ("profile", "default"));

        Assert.False(result.Success);
        Assert.Equal("'default' is not a profile this host lets an agent name (Orkeon:Tools:Firefox:AllowedProfiles): disposable, research.", result.Error);
        Assert.Empty(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_StartTheBrowserOnTheProfileNamed_AndSayItInEveryAnswer()
    {
        _options.AllowedProfiles.Add("research");
        await using var run = AgentRunScope.Begin("agent-1");
        using var tool = Tool();

        var first = Body(await CallAsync(tool, "navigate", ("url", Home), ("profile", "Research")));
        var next = Body(await CallAsync(tool, "snapshot"));
        var other = await CallAsync(tool, "navigate", ("url", Docs), ("profile", "disposable"));

        Assert.Equal("Research", first["profile"]!.GetValue<string>());
        Assert.Equal("Research", next["profile"]!.GetValue<string>());
        Assert.False(other.Success);
        Assert.Equal("The browser of this run runs with the profile 'Research': close it before opening one with 'disposable'.", other.Error);
        Assert.Equal(["Research"], Launcher.Profiles);
    }

    [Fact]
    public async Task Should_StartTheBrowserOnTheHostsProfile_When_TheAgentNamesNone()
    {
        _options.Profile = "work";
        using var tool = Tool();

        var body = Body(await CallAsync(tool, "navigate", ("url", Home)));

        Assert.Equal("work", body["profile"]!.GetValue<string>());
        Assert.Equal(["work"], Launcher.Profiles);
    }

    [Fact]
    public async Task Should_ShowTheNextRunTheTabsTheLastOneLeft_When_TheHostKeepsTheBrowser()
    {
        _options.Lifetime = FirefoxBrowserLifetime.Host;
        using var tool = Tool();

        await using (AgentRunScope.Begin("agent-1"))
        {
            await CallAsync(tool, "navigate", ("url", Home));
            await CallAsync(tool, "new_tab", ("url", Docs));
        }

        JsonObject tabs;
        await using (AgentRunScope.Begin("agent-2"))
            tabs = Body(await CallAsync(tool, "tabs"));

        Assert.Equal(2, tabs["tabs"]!.AsArray().Count);
        Assert.Equal(Docs, tabs["url"]!.GetValue<string>());
        Assert.Equal("t2", tabs["tab"]!.GetValue<string>());
        Assert.False(Browser.Disposed);
        Assert.DoesNotContain("browser.close", Browser.Wire.Methods);
        Assert.Single(Launcher.Browsers);
    }

    [Fact]
    public async Task Should_CloseTheHostsBrowserOfTheProfileNamed_OutsideAnyRun()
    {
        _options.Lifetime = FirefoxBrowserLifetime.Host;
        _options.AllowedProfiles.Add("research");
        using var tool = Tool();
        await CallAsync(tool, "navigate", ("url", Home), ("profile", "research"));
        await CallAsync(tool, "navigate", ("url", Home));

        var closed = Body(await CallAsync(tool, "close", ("profile", "research")));
        var again = Body(await CallAsync(tool, "close", ("profile", "research")));

        Assert.Equal("The browser was closed.", closed["value"]!.GetValue<string>());
        Assert.Equal("No browser was open.", again["value"]!.GetValue<string>());
        Assert.True(Launcher.Browsers[0].Disposed);
        Assert.False(Launcher.Browsers[1].Disposed);
    }
}
