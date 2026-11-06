using System.Text.Json.Nodes;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Firefox.BiDi;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests.Firefox;

public class FirefoxSessionTests : FirefoxSessionTestBase
{
    private FakeFirefoxBrowser Browser => Launcher.Last;

    private FakeBiDiTransport Wire => Browser.Wire;

    /// <summary>The keys a key action pressed and released, in order: <c>down:a</c>, <c>up:a</c>.</summary>
    private static string[] Keys(JsonObject performActions) =>
        [.. performActions["actions"]![0]!["actions"]!.AsArray()
            .Select(a => $"{(a!["type"]!.GetValue<string>() == "keyDown" ? "down" : "up")}:{a["value"]!.GetValue<string>()}")];

    private JsonObject LastKeys() =>
        Wire.ParamsOf("input.performActions").Last(p => p["actions"]![0]!["type"]!.GetValue<string>() == "key");

    [Fact]
    public async Task Should_OpenASessionOnTheTabTheBrowserShows()
    {
        Launcher.Configure += browser => browser.Headless = true;

        await using var session = await OpenAsync();

        Assert.Equal(["session.new", "browsingContext.getTree", "session.subscribe"], Wire.Methods);
        Assert.False(Wire.ParamsOf("session.new")[0]["capabilities"]!["alwaysMatch"]!["acceptInsecureCerts"]!.GetValue<bool>());
        Assert.Equal("""["browsingContext"]""", Wire.ParamsOf("session.subscribe")[0]["events"]!.ToJsonString());
        Assert.True(session.IsAlive);
        Assert.True(session.Headless);
        Assert.Null(session.Page);
    }

    [Fact]
    public async Task Should_CloseTheBrowser_When_ItShowsNoTab()
    {
        Launcher.Configure += browser =>
            browser.Wire.Handlers["browsingContext.getTree"] = _ => new JsonObject { ["contexts"] = new JsonArray() };

        var error = await Assert.ThrowsAsync<FirefoxException>(() => OpenAsync());

        Assert.Contains("without a tab", error.Message, StringComparison.Ordinal);
        Assert.True(Browser.Disposed);
    }

    [Fact]
    public async Task Should_CloseTheBrowser_When_ItRefusesTheSession()
    {
        Launcher.Configure += browser =>
            browser.Wire.Handlers["session.new"] = _ => throw new FakeBiDiError("session not created", "Maximum number of active sessions");

        var error = await Assert.ThrowsAsync<BiDiException>(() => OpenAsync());

        Assert.Equal("session not created", error.Code);
        Assert.True(Browser.Disposed);
    }

    [Fact]
    public async Task Should_LoadAPage_AndTellWhereItLanded()
    {
        await using var session = await OpenAsync();
        Browser.Redirects["https://example.test/old"] = Home;

        await session.NavigateAsync("https://example.test/old", Ct);
        var state = await session.ReadStateAsync(Ct);

        var sent = Assert.Single(Wire.ParamsOf("browsingContext.navigate"));
        Assert.Equal(FakeFirefoxBrowser.Tab, sent["context"]!.GetValue<string>());
        Assert.Equal("https://example.test/old", sent["url"]!.GetValue<string>());
        Assert.Equal("complete", sent["wait"]!.GetValue<string>());
        Assert.Equal(new PageState(Home, "Example", Loaded: true), state);
    }

    [Fact]
    public async Task Should_ReadTheAddressFromTheTab_When_ThePageRunsNoScript()
    {
        await using var session = await OpenAsync();
        Wire.Handlers["script.evaluate"] = _ => throw new FakeBiDiError("unknown error", "the page is not ready");

        var state = await session.ReadStateAsync(Ct);

        Assert.Equal(new PageState("about:blank", string.Empty, Loaded: false), state);
    }

    [Fact]
    public async Task Should_ReadThePageAsAGraph_WithIdsOfItsOwnAtEachReading()
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);

        var first = await session.SnapshotAsync(Ct);
        var second = await session.SnapshotAsync(Ct);

        Assert.Equal("""[{"type":"number","value":1000},{"type":"number","value":6000},{"type":"number","value":200000}]""",
            Wire.ParamsOf("script.callFunction")[0]["arguments"]!.ToJsonString());
        Assert.Equal(1, first.Generation);
        Assert.Equal(Home, first.Url);
        Assert.Equal("Example", first.Title);
        Assert.Equal("Welcome to the example.", first.Node("p1#page").SourceSnippet);
        Assert.Equal("Docs", first.Node("p1#1").Name);
        Assert.Equal(FakeFirefoxBrowser.Reference(Home, 1), first.SharedId("p1#1"));
        Assert.Equal(2, second.Generation);
        Assert.Same(second, session.Page);
        Assert.Equal(FakeFirefoxBrowser.Reference(Home, 4), second.SharedId("p2#4"));
        Assert.Contains("earlier reading", Assert.Throws<FirefoxException>(() => second.Node("p1#1")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Fail_When_TheOutlineComesBackEmpty()
    {
        await using var session = await OpenAsync();
        Wire.Handlers["script.callFunction"] = _ => new JsonObject
        {
            ["type"] = "success",
            ["result"] = new JsonObject { ["type"] = "array", ["value"] = new JsonArray() },
        };

        var error = await Assert.ThrowsAsync<FirefoxException>(() => session.SnapshotAsync(Ct));

        Assert.Contains("outline came back empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ForgetTheSnapshot_When_TheBrowserShowsAnotherPage()
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);
        await session.SnapshotAsync(Ct);
        var current = session.PageIsCurrent;

        await session.NavigateAsync(Docs, Ct);

        Assert.True(current);
        Assert.Null(session.Page);
        Assert.False(session.PageIsCurrent);
    }

    [Fact]
    public async Task Should_KeepTheSnapshot_AndSayItIsNoLongerCurrent_When_AnActionLeftThePageWhereItWas()
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);
        var page = await session.SnapshotAsync(Ct);

        await session.PressAsync(["a"], Ct);

        // The node ids still name the elements of the page; what they hold may have changed.
        Assert.Same(page, session.Page);
        Assert.False(session.PageIsCurrent);
    }

    [Fact]
    public async Task Should_ForgetTheSnapshot_When_AScriptLoadsThePageAgain()
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);
        await session.SnapshotAsync(Ct);
        Browser.Script = _ =>
        {
            Browser.Emit("browsingContext.navigationStarted", new JsonObject { ["navigation"] = "reload-1", ["url"] = Home });
            Browser.Emit("browsingContext.load", new JsonObject { ["navigation"] = "reload-1", ["url"] = Home });
            return new JsonObject { ["type"] = "undefined" };
        };

        await session.EvaluateAsync("location.reload()", Ct);

        // The same address, another document: the elements the snapshot names are gone.
        Assert.Null(session.Page);
    }

    [Fact]
    public async Task Should_ClickAnElementWithThePointer_AndWaitForThePageItOpens()
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);
        var link = (await session.SnapshotAsync(Ct)).SharedId("p1#1");
        Browser.ClickLeadsTo[link] = Docs;
        var before = Wire.Methods.Count;

        await session.ClickAsync(link, Ct);

        Assert.Equal(["script.callFunction", "input.performActions", "input.releaseActions"], Wire.Methods.Skip(before).Take(3));
        Assert.Contains("scrollIntoView", Wire.ParamsOf("script.callFunction")[^1]["functionDeclaration"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal($$"""[{"sharedId":"{{link}}"}]""", Wire.ParamsOf("script.callFunction")[^1]["arguments"]!.ToJsonString());
        var pointer = Wire.ParamsOf("input.performActions")[^1]["actions"]![0]!;
        Assert.Equal("pointer", pointer["type"]!.GetValue<string>());
        Assert.Equal(["pointerMove", "pointerDown", "pointerUp"], pointer["actions"]!.AsArray().Select(a => a!["type"]!.GetValue<string>()));
        Assert.Equal(link, pointer["actions"]![0]!["origin"]!["element"]!["sharedId"]!.GetValue<string>());
        Assert.Equal(Docs, (await session.ReadStateAsync(Ct)).Url);
        Assert.Null(session.Page);
    }

    [Fact]
    public async Task Should_WaitForTheEndOfTheNavigationAClickStarted()
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);
        var link = (await session.SnapshotAsync(Ct)).SharedId("p1#1");
        Browser.ClickLeadsTo[link] = Docs;
        Browser.HoldLoad = true;

        var click = session.ClickAsync(link, Ct);
        await Polling.WaitUntilAsync(() => Wire.Methods.Contains("input.releaseActions"));
        var endedBeforeTheLoad = click.IsCompleted;
        Browser.FinishLoad();
        await click;

        Assert.False(endedBeforeTheLoad);
        Assert.Equal(new PageState(Docs, "Docs", Loaded: true), await session.ReadStateAsync(Ct));
    }

    [Fact]
    public async Task Should_NotWaitForTheNavigationOfAnotherTab()
    {
        await using var session = await OpenAsync(new FirefoxToolOptions { CommandTimeoutSeconds = 600 });

        Browser.Emit("browsingContext.navigationStarted", new JsonObject { ["context"] = "another-tab", ["navigation"] = "n9" });
        await session.ReadStateAsync(Ct);
        var press = session.PressAsync(["a"], Ct);

        await Polling.WaitUntilAsync(() => press.IsCompleted);
        await press;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_AskForANewSnapshot_When_TheElementLeftThePage(bool forgotten)
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);
        var button = (await session.SnapshotAsync(Ct)).SharedId("p1#4");
        (forgotten ? Browser.Unknown : Browser.Detached).Add(button);

        var click = await Assert.ThrowsAsync<FirefoxException>(() => session.ClickAsync(button, Ct));
        var type = await Assert.ThrowsAsync<FirefoxException>(() => session.TypeAsync(button, "x", Ct));

        Assert.Contains("no longer in the page: call snapshot again", click.Message, StringComparison.Ordinal);
        Assert.Equal(click.Message, type.Message);
        Assert.DoesNotContain("input.performActions", Wire.Methods);
        Assert.True(session.IsAlive);
    }

    [Fact]
    public async Task Should_AskForANewSnapshot_When_TheElementLeavesUnderThePointer()
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);
        var button = (await session.SnapshotAsync(Ct)).SharedId("p1#4");
        Browser.PointerFailure = BiDiException.NoSuchNode;

        var error = await Assert.ThrowsAsync<FirefoxException>(() => session.ClickAsync(button, Ct));

        Assert.Contains("no longer in the page", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ClickByScript_When_ThePointerCannotReachTheElement()
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);
        var link = (await session.SnapshotAsync(Ct)).SharedId("p1#1");
        Browser.ClickLeadsTo[link] = Docs;
        Browser.PointerFailure = "move target out of bounds";

        await session.ClickAsync(link, Ct);

        Assert.Equal("(el) => { el.click(); }", Wire.ParamsOf("script.callFunction")[^1]["functionDeclaration"]!.GetValue<string>());
        Assert.Equal(Docs, (await session.ReadStateAsync(Ct)).Url);
    }

    [Fact]
    public async Task Should_TypeATextKeyByKeyOverWhatTheElementHolds()
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);
        var field = (await session.SnapshotAsync(Ct)).SharedId("p1#3");

        await session.TypeAsync(field, "ab\n", Ct);

        var focus = Wire.ParamsOf("script.callFunction")[^1];
        Assert.Contains("el.focus()", focus["functionDeclaration"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("el.select()", focus["functionDeclaration"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(field, focus["arguments"]![0]!["sharedId"]!.GetValue<string>());
        Assert.Equal(["down:a", "up:a", "down:b", "up:b", "down:", "up:"], Keys(LastKeys()));
        Assert.Equal("input.releaseActions", Wire.Methods[Wire.Methods.ToList().LastIndexOf("input.performActions") + 1]);
        Assert.NotNull(session.Page);
        Assert.False(session.PageIsCurrent);
    }

    [Fact]
    public async Task Should_EmptyTheElement_When_TheTextIsEmpty()
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);
        var field = (await session.SnapshotAsync(Ct)).SharedId("p1#3");

        await session.TypeAsync(field, string.Empty, Ct);

        Assert.Equal(["down:", "up:"], Keys(LastKeys()));
    }

    [Fact]
    public async Task Should_PressAChord_ReleasedInReverse()
    {
        await using var session = await OpenAsync();
        Assert.True(FirefoxKeys.TryParse("Control+Shift+a", out var chord));

        await session.PressAsync(chord!, Ct);

        Assert.Equal(["down:", "down:", "down:a", "up:a", "up:", "up:"], Keys(LastKeys()));
        Assert.Equal(FakeFirefoxBrowser.Tab, LastKeys()["context"]!.GetValue<string>());
    }

    [Fact]
    public async Task Should_ReturnTheValueOfAScriptAsText()
    {
        await using var session = await OpenAsync();
        Browser.Script = _ => JsonNode.Parse("""{"type":"array","value":[{"type":"number","value":1},{"type":"string","value":"a"}]}""")!.AsObject();

        var value = await session.EvaluateAsync("[1, 'a']", Ct);

        Assert.Equal("""[1,"a"]""", value);
        var sent = Wire.ParamsOf("script.evaluate").Single(p => p["expression"]!.GetValue<string>() == "[1, 'a']");
        Assert.True(sent["awaitPromise"]!.GetValue<bool>());
        Assert.True(sent["userActivation"]!.GetValue<bool>());
        Assert.Equal(FakeFirefoxBrowser.Tab, sent["target"]!["context"]!.GetValue<string>());
    }

    [Fact]
    public async Task Should_SayWhatAScriptThrew_AndKeepTheBrowser()
    {
        await using var session = await OpenAsync();
        Browser.Script = _ => null;

        var error = await Assert.ThrowsAsync<FirefoxException>(() => session.EvaluateAsync("nope()", Ct));

        Assert.Equal("The script threw: ReferenceError: nope is not defined", error.Message);
        Assert.True(session.IsAlive);
    }

    [Fact]
    public async Task Should_CaptureTheVisiblePartOfThePage()
    {
        await using var session = await OpenAsync();
        Browser.Screenshot = [1, 2, 3, 4, 5];

        var image = await session.ScreenshotAsync(Ct);

        Assert.Equal([1, 2, 3, 4, 5], image);
        Assert.Equal("viewport", Wire.ParamsOf("browsingContext.captureScreenshot")[0]["origin"]!.GetValue<string>());
    }

    [Fact]
    public async Task Should_Fail_When_TheScreenshotIsNotAnImage()
    {
        await using var session = await OpenAsync();
        Wire.Handlers["browsingContext.captureScreenshot"] = _ => new JsonObject { ["data"] = "not base64 !" };

        await Assert.ThrowsAsync<FirefoxException>(() => session.ScreenshotAsync(Ct));
    }

    // Firefox restores the page from its history cache: the navigation is announced, and no load ends it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_GoBackToThePageBefore(bool loadedAgain)
    {
        await using var session = await OpenAsync(new FirefoxToolOptions { CommandTimeoutSeconds = 600 });
        Browser.TraversalLoads = loadedAgain;
        await session.NavigateAsync(Home, Ct);
        await session.NavigateAsync(Docs, Ct);
        await session.SnapshotAsync(Ct);

        await session.BackAsync(Ct);

        Assert.Equal(-1, Wire.ParamsOf("browsingContext.traverseHistory")[0]["delta"]!.GetValue<int>());
        Assert.Equal(Home, (await session.ReadStateAsync(Ct)).Url);
        Assert.Null(session.Page);
        Assert.False(session.NavigationPending);
    }

    [Fact]
    public async Task Should_GiveUpOnANavigationThatNeverEnds_AndNotHoldTheNextAction()
    {
        await using var session = await OpenAsync(new FirefoxToolOptions { CommandTimeoutSeconds = 1 });
        await session.NavigateAsync(Home, Ct);
        var link = (await session.SnapshotAsync(Ct)).SharedId("p1#1");
        Browser.ClickLeadsTo[link] = Docs;
        Browser.HoldLoad = true;

        await session.ClickAsync(link, Ct);

        Assert.False(session.NavigationPending);
    }

    [Fact]
    public async Task Should_DismissADialog_AndKeepWhatItSaid()
    {
        await using var session = await OpenAsync();

        Browser.Emit("browsingContext.userPromptOpened", new JsonObject { ["type"] = "confirm", ["message"] = "Delete everything?" });
        await Polling.WaitUntilAsync(() => Wire.Methods.Contains("browsingContext.handleUserPrompt"));

        var answer = Assert.Single(Wire.ParamsOf("browsingContext.handleUserPrompt"));
        Assert.False(answer["accept"]!.GetValue<bool>());
        Assert.Equal(FakeFirefoxBrowser.Tab, answer["context"]!.GetValue<string>());
        Assert.Equal(["confirm: Delete everything?"], session.TakeDialogs());
        Assert.Empty(session.TakeDialogs());
    }

    [Fact]
    public async Task Should_LetThePageBeLeft_When_ItAsksBeforeUnloading()
    {
        await using var session = await OpenAsync();

        Browser.Emit("browsingContext.userPromptOpened", new JsonObject { ["type"] = "beforeunload", ["message"] = "" });
        await Polling.WaitUntilAsync(() => Wire.Methods.Contains("browsingContext.handleUserPrompt"));

        Assert.True(Wire.ParamsOf("browsingContext.handleUserPrompt")[0]["accept"]!.GetValue<bool>());
        Assert.Empty(session.TakeDialogs());
    }

    [Fact]
    public async Task Should_FollowTheTabALinkOpened_AndNotOneOpenedElsewhere()
    {
        await using var session = await OpenAsync();

        Browser.Emit("browsingContext.contextCreated", new JsonObject { ["context"] = "frame-1", ["parent"] = FakeFirefoxBrowser.Tab, ["originalOpener"] = FakeFirefoxBrowser.Tab });
        Browser.OpenTab();
        Browser.OpenTab(opener: "tab-of-another-agent");
        var opened = Browser.OpenTab(opener: FakeFirefoxBrowser.Tab);
        // The answer to this command comes after the events on the channel: they were all read.
        await session.ReadStateAsync(Ct);
        await session.NavigateAsync(Docs, Ct);

        Assert.Equal(opened, Wire.ParamsOf("browsingContext.navigate")[0]["context"]!.GetValue<string>());
        Assert.Equal("t2", session.CurrentTab);
        Assert.Equal(["t1", "t2"], (await session.ListTabsAsync(Ct)).Select(tab => tab.Handle));
    }

    [Fact]
    public async Task Should_OpenATab_ListThem_AndSwitchBetweenThem()
    {
        await using var session = await OpenAsync();
        await session.NavigateAsync(Home, Ct);

        var handle = await session.NewTabAsync(Ct);
        await session.NavigateAsync(Docs, Ct);
        var tabs = await session.ListTabsAsync(Ct);

        Assert.Equal("t2", handle);
        Assert.Equal(handle, session.CurrentTab);
        Assert.Equal([new TabState("t1", Home, "Example", false), new TabState("t2", Docs, "Docs", true)], tabs);
        Assert.Equal(["tab-1", "tab-2"], Browser.Tabs);
        Assert.False(Wire.ParamsOf("browsingContext.create")[0]["background"]!.GetValue<bool>());

        await session.SwitchTabAsync("T1", Ct);

        Assert.Equal("t1", session.CurrentTab);
        Assert.Equal(["tab-1"], Browser.Activated);
        Assert.Equal(Home, (await session.ReadStateAsync(Ct)).Url);
        Assert.Null(session.Page);
    }

    [Fact]
    public async Task Should_CloseATab_AndDriveTheOneBeforeIt()
    {
        await using var session = await OpenAsync();
        await session.NewTabAsync(Ct);
        await session.NewTabAsync(Ct);

        await session.CloseTabAsync(null, Ct);
        Assert.Equal("t2", session.CurrentTab);

        await session.CloseTabAsync("t1", Ct);
        Assert.Equal("t2", session.CurrentTab);
        Assert.Equal(["tab-2"], Browser.Tabs);

        var refused = await Assert.ThrowsAsync<FirefoxException>(() => session.CloseTabAsync("t2", Ct));
        Assert.Contains("last tab", refused.Message, StringComparison.Ordinal);
        var unknown = await Assert.ThrowsAsync<FirefoxException>(() => session.SwitchTabAsync("t9", Ct));
        Assert.Contains("'t9' is not a tab", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_DropTheTabsTheUserCloses_AndSaySoWhenNoneIsLeft()
    {
        await using var session = await OpenAsync();
        await session.NewTabAsync(Ct);

        Browser.CloseTab("tab-2");
        await session.ReadStateAsync(Ct);
        Assert.Equal("t1", session.CurrentTab);

        Browser.CloseTab("tab-1");
        await Polling.WaitUntilAsync(() => session.CurrentTab is null);

        var error = await Assert.ThrowsAsync<FirefoxException>(() => session.NavigateAsync(Home, Ct));
        Assert.Contains("Every tab of this agent was closed", error.Message, StringComparison.Ordinal);
        Assert.Equal("t3", await session.NewTabAsync(Ct));
        await session.NavigateAsync(Home, Ct);
    }

    [Fact]
    public async Task Should_DismissTheDialogOfATabItDoesNotDrive()
    {
        await using var session = await OpenAsync();
        await session.NewTabAsync(Ct);

        Browser.Emit("browsingContext.userPromptOpened", new JsonObject { ["context"] = "tab-1", ["type"] = "alert", ["message"] = "Still here" });
        await Polling.WaitUntilAsync(() => Wire.Methods.Contains("browsingContext.handleUserPrompt"));

        Assert.Equal("tab-1", Wire.ParamsOf("browsingContext.handleUserPrompt")[0]["context"]!.GetValue<string>());
        Assert.Equal(["alert: Still here"], session.TakeDialogs());
    }

    [Fact]
    public async Task Should_ShareABrowserBetweenSessions_WhichClosesWithTheLast()
    {
        var options = new FirefoxToolOptions();
        var browser = await FirefoxBrowser.OpenAsync(Launcher, options, Timings, logger: null, Ct);
        var first = await FirefoxSession.AttachAsync(browser, options, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(), logger: null, Ct);
        var second = await FirefoxSession.AttachAsync(browser, options, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(), logger: null, Ct);
        await first.NavigateAsync(Home, Ct);
        await second.NavigateAsync(Docs, Ct);

        // The events of one session's tab are not the other's.
        Browser.Emit("browsingContext.navigationStarted", new JsonObject { ["context"] = "tab-2", ["navigation"] = "n9", ["url"] = Home });
        await first.ReadStateAsync(Ct);
        Assert.False(first.NavigationPending);
        Assert.Equal(2, browser.SessionCount);
        Assert.Equal([Home, Docs], Browser.Tabs.Select(Browser.UrlOf));

        await first.DisposeAsync();

        Assert.True(browser.IsAlive);
        Assert.Equal(["tab-2"], Browser.Tabs);
        Assert.Equal(Docs, (await second.ReadStateAsync(Ct)).Url);

        await second.DisposeAsync();

        Assert.False(browser.IsAlive);
        Assert.True(Browser.Disposed);
        Assert.Single(Wire.Methods, "browser.close");
    }

    [Fact]
    public async Task Should_AskTheBrowserToClose_AndEndIt_Once()
    {
        var session = await OpenAsync();

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Single(Wire.Methods, "browser.close");
        Assert.True(Browser.Disposed);
        Assert.False(session.IsAlive);
        Assert.Null(session.Page);
    }

    [Fact]
    public async Task Should_KnowTheBrowserHasGone_When_ItClosesTheChannel()
    {
        await using var session = await OpenAsync();

        Wire.Close();
        await Polling.WaitUntilAsync(() => !session.IsAlive);

        var error = await Assert.ThrowsAsync<BiDiException>(() => session.NavigateAsync(Home, Ct));
        Assert.Equal(BiDiException.ConnectionClosed, error.Code);
    }
}
