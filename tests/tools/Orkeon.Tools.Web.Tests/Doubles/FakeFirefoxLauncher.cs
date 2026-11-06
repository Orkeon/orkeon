using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>Hands out <see cref="FakeFirefoxBrowser"/>s instead of starting Firefox, and keeps each one.</summary>
internal sealed class FakeFirefoxLauncher : IFirefoxLauncher
{
    public List<FakeFirefoxBrowser> Browsers { get; } = [];

    /// <summary>Thrown by the next launches when set.</summary>
    public Exception? LaunchFailure { get; set; }

    /// <summary>Applied to each browser before it is handed out: its pages, its failures.</summary>
    public Action<FakeFirefoxBrowser>? Configure { get; set; }

    public FakeFirefoxBrowser Last => Browsers[^1];

    public Task<IFirefoxBrowser> LaunchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (LaunchFailure is not null)
            throw LaunchFailure;

        var browser = new FakeFirefoxBrowser();
        Configure?.Invoke(browser);
        lock (Browsers) Browsers.Add(browser);
        return Task.FromResult<IFirefoxBrowser>(browser);
    }
}

/// <summary>One page of a <see cref="FakeFirefoxBrowser"/>: what <c>page-outline.js</c> would read in it.</summary>
internal sealed class FakePage
{
    public string Title { get; set; } = string.Empty;

    public string Lead { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public bool Truncated { get; set; }

    public List<JsonObject> Elements { get; } = [];

    public FakePage Heading(int level, string name, string text = "", int container = -1) =>
        Add(new JsonObject { ["k"] = 0, ["r"] = "heading", ["n"] = name, ["l"] = level, ["c"] = container, ["t"] = text });

    public FakePage Region(string role, string name = "", int container = -1) =>
        Add(new JsonObject { ["k"] = 1, ["r"] = role, ["n"] = name, ["c"] = container, ["t"] = "" });

    public FakePage Link(string name, string target, int container = -1) =>
        Add(new JsonObject { ["k"] = 2, ["r"] = "link", ["n"] = name, ["c"] = container, ["t"] = "", ["h"] = target });

    public FakePage Control(string role, string name, string value = "", int container = -1, bool visible = true)
    {
        var element = new JsonObject { ["k"] = 3, ["r"] = role, ["n"] = name, ["c"] = container, ["t"] = value };
        if (!visible) element["v"] = 0;
        return Add(element);
    }

    private FakePage Add(JsonObject element)
    {
        Elements.Add(element);
        return this;
    }
}

/// <summary>
/// A browser that answers WebDriver BiDi as Firefox does, over a <see cref="FakeBiDiTransport"/>: it
/// serves the <see cref="Pages"/> it was given, keeps a history per tab, opens and closes tabs, and
/// records every command in <see cref="Wire"/>.
/// </summary>
internal sealed class FakeFirefoxBrowser : IFirefoxBrowser
{
    public const string Tab = "tab-1";

    private readonly Dictionary<string, List<string>> _histories = new(StringComparer.Ordinal) { [Tab] = ["about:blank"] };
    private readonly List<string> _tabs = [Tab];
    private int _navigations;
    private int _created = 1;

    public FakeFirefoxBrowser()
    {
        Wire.Handlers["session.new"] = _ => new JsonObject { ["sessionId"] = "s1", ["capabilities"] = new JsonObject { ["browserName"] = "firefox" } };
        Wire.Handlers["browsingContext.getTree"] = Tree;
        Wire.Handlers["browsingContext.create"] = Create;
        Wire.Handlers["browsingContext.close"] = Close;
        Wire.Handlers["browsingContext.activate"] = parameters =>
        {
            Activated.Add(ContextOf(parameters));
            return [];
        };
        Wire.Handlers["browsingContext.navigate"] = Navigate;
        Wire.Handlers["browsingContext.traverseHistory"] = Traverse;
        Wire.Handlers["script.evaluate"] = Evaluate;
        Wire.Handlers["script.callFunction"] = CallFunction;
        Wire.Handlers["input.performActions"] = PerformActions;
        Wire.Handlers["browsingContext.captureScreenshot"] = _ => new JsonObject { ["data"] = Convert.ToBase64String(Screenshot) };
    }

    public FakeBiDiTransport Wire { get; } = new();

    public IBiDiTransport Transport => Wire;

    public bool Headless { get; set; }

    public bool Disposed { get; private set; }

    /// <summary>The pages the browser can show, by address. An address it does not hold shows an empty page.</summary>
    public Dictionary<string, FakePage> Pages { get; } = new(StringComparer.Ordinal);

    /// <summary>Where an address lands instead of itself.</summary>
    public Dictionary<string, string> Redirects { get; } = new(StringComparer.Ordinal);

    /// <summary>Where the click on an element (by its reference) leads.</summary>
    public Dictionary<string, string> ClickLeadsTo { get; } = new(StringComparer.Ordinal);

    /// <summary>References the page no longer knows: a command naming one fails with <c>no such node</c>.</summary>
    public HashSet<string> Unknown { get; } = new(StringComparer.Ordinal);

    /// <summary>References of elements removed from the document.</summary>
    public HashSet<string> Detached { get; } = new(StringComparer.Ordinal);

    /// <summary>The error code a pointer action fails with, when set.</summary>
    public string? PointerFailure { get; set; }

    /// <summary>When set, a click that leads elsewhere announces its navigation and does not end it.</summary>
    public bool HoldLoad { get; set; }

    /// <summary>
    /// When set, a history traversal loads its page again, with a load event. By default the page is
    /// restored from the history cache, as Firefox does: its navigation is announced and never ended.
    /// </summary>
    public bool TraversalLoads { get; set; }

    /// <summary>What a script evaluates to; an exception answer when it returns null.</summary>
    public Func<string, JsonObject?> Script { get; set; } = _ => new JsonObject { ["type"] = "undefined" };

    public byte[] Screenshot { get; set; } = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>The address the first tab shows.</summary>
    public string Url => UrlOf(Tab);

    public string ReadyState { get; set; } = "complete";

    /// <summary>The tabs open, in the order they were opened.</summary>
    public IReadOnlyList<string> Tabs => _tabs;

    /// <summary>The tabs brought in front, in order.</summary>
    public List<string> Activated { get; } = [];

    /// <summary>The reference the browser gives element <paramref name="index"/> of the page at <paramref name="url"/>.</summary>
    public static string Reference(string url, int index) => $"{url}#node-{index}";

    /// <summary>The address a tab shows; an empty one for a tab that is gone.</summary>
    public string UrlOf(string context) => _histories.TryGetValue(context, out var history) ? history[^1] : string.Empty;

    /// <summary>Sends an event of the first tab (or of <c>context</c> in <paramref name="parameters"/>), as Firefox would.</summary>
    public void Emit(string method, JsonObject? parameters = null)
    {
        parameters ??= [];
        if (!parameters.ContainsKey("context")) parameters["context"] = Tab;
        Wire.Push(new JsonObject { ["type"] = "event", ["method"] = method, ["params"] = parameters }.ToJsonString());
    }

    /// <summary>Opens a tab as the user would, or as a link with <c>target=_blank</c> of <paramref name="opener"/> would.</summary>
    public string OpenTab(string? opener = null)
    {
        var context = $"tab-{++_created}";
        _histories[context] = ["about:blank"];
        _tabs.Add(context);
        Emit("browsingContext.contextCreated", new JsonObject
        {
            ["context"] = context,
            ["url"] = "about:blank",
            ["children"] = new JsonArray(),
            ["parent"] = null,
            ["originalOpener"] = opener,
        });
        return context;
    }

    /// <summary>Closes a tab as the user would.</summary>
    public void CloseTab(string context)
    {
        if (!_tabs.Remove(context))
            return;
        _histories.Remove(context);
        Emit("browsingContext.contextDestroyed", new JsonObject { ["context"] = context, ["url"] = "about:blank", ["children"] = new JsonArray(), ["parent"] = null });
    }

    /// <summary>Ends the navigation a held click announced.</summary>
    public void FinishLoad()
    {
        ReadyState = "complete";
        Emit("browsingContext.load", new JsonObject { ["navigation"] = $"n{_navigations}", ["url"] = Url });
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return Wire.DisposeAsync();
    }

    private static string ContextOf(JsonObject parameters) =>
        parameters["context"]?.GetValue<string>() ?? parameters["target"]?["context"]?.GetValue<string>() ?? Tab;

    private FakePage PageOf(string context) => Pages.TryGetValue(UrlOf(context), out var page) ? page : new FakePage();

    private List<string> HistoryOf(string context) =>
        _histories.TryGetValue(context, out var history)
            ? history
            : throw new FakeBiDiError("no such frame", $"Browsing context with id {context} not found");

    private JsonObject Tree(JsonObject parameters)
    {
        var root = parameters["root"]?.GetValue<string>();
        var contexts = new JsonArray();
        foreach (var tab in root is null ? _tabs : [root])
        {
            HistoryOf(tab);
            contexts.Add(new JsonObject { ["context"] = tab, ["url"] = UrlOf(tab), ["children"] = new JsonArray(), ["parent"] = null });
        }

        return new JsonObject { ["contexts"] = contexts };
    }

    private JsonObject Create(JsonObject parameters)
    {
        var context = OpenTab();
        return new JsonObject { ["context"] = context };
    }

    private JsonObject Close(JsonObject parameters)
    {
        var context = ContextOf(parameters);
        HistoryOf(context);
        CloseTab(context);
        return [];
    }

    private JsonObject Navigate(JsonObject parameters)
    {
        var context = ContextOf(parameters);
        var url = parameters["url"]!.GetValue<string>();
        HistoryOf(context).Add(Redirects.GetValueOrDefault(url, url));
        return new JsonObject { ["navigation"] = $"n{++_navigations}", ["url"] = UrlOf(context) };
    }

    private JsonObject Traverse(JsonObject parameters)
    {
        var context = ContextOf(parameters);
        var history = HistoryOf(context);
        if (history.Count < 2)
            throw new FakeBiDiError("no such history entry", "History entry with delta -1 not found");
        history.RemoveAt(history.Count - 1);
        var navigation = $"n{++_navigations}";
        Emit("browsingContext.navigationStarted", new JsonObject { ["context"] = context, ["navigation"] = navigation, ["url"] = UrlOf(context) });
        if (TraversalLoads)
            Emit("browsingContext.load", new JsonObject { ["context"] = context, ["navigation"] = navigation, ["url"] = UrlOf(context) });
        return [];
    }

    private JsonObject Evaluate(JsonObject parameters)
    {
        var context = ContextOf(parameters);
        var expression = parameters["expression"]!.GetValue<string>();
        if (expression.Contains("document.readyState", StringComparison.Ordinal))
        {
            HistoryOf(context);
            return Success(new JsonObject { ["type"] = "string", ["value"] = JsonSerializer.Serialize(new[] { UrlOf(context), PageOf(context).Title, ReadyState }) });
        }

        return Script(expression) is { } value
            ? Success(value)
            : new JsonObject { ["type"] = "exception", ["exceptionDetails"] = new JsonObject { ["text"] = "ReferenceError: nope is not defined" } };
    }

    private JsonObject CallFunction(JsonObject parameters)
    {
        var context = ContextOf(parameters);
        var function = parameters["functionDeclaration"]!.GetValue<string>();
        if (function.Contains("maxElements", StringComparison.Ordinal))
        {
            var url = UrlOf(context);
            var page = PageOf(context);
            var outline = new JsonObject
            {
                ["url"] = url,
                ["title"] = page.Title,
                ["lead"] = page.Lead,
                ["text"] = page.Text,
                ["textTruncated"] = false,
                ["truncated"] = page.Truncated,
                ["elements"] = new JsonArray([.. page.Elements.Select(e => e.DeepClone())]),
            };
            var items = new JsonArray(new JsonObject { ["type"] = "string", ["value"] = outline.ToJsonString() });
            for (var i = 0; i < page.Elements.Count; i++)
                items.Add(new JsonObject { ["type"] = "node", ["sharedId"] = Reference(url, i) });
            return Success(new JsonObject { ["type"] = "array", ["value"] = items });
        }

        var reference = parameters["arguments"]?[0]?["sharedId"]?.GetValue<string>();
        if (reference is not null && Unknown.Contains(reference))
            throw new FakeBiDiError("no such node", $"The node with the reference {reference} is not known");
        if (reference is not null && function.Contains("el.click()", StringComparison.Ordinal))
            Follow(context, reference);
        return Success(new JsonObject { ["type"] = "boolean", ["value"] = reference is null || !Detached.Contains(reference) });
    }

    private JsonObject PerformActions(JsonObject parameters)
    {
        var source = parameters["actions"]![0]!;
        if (source["type"]!.GetValue<string>() != "pointer")
            return [];
        if (PointerFailure is not null)
            throw new FakeBiDiError(PointerFailure, "the pointer could not reach the element");

        Follow(ContextOf(parameters), source["actions"]![0]!["origin"]!["element"]!["sharedId"]!.GetValue<string>());
        return [];
    }

    private void Follow(string context, string reference)
    {
        if (!ClickLeadsTo.TryGetValue(reference, out var target))
            return;

        HistoryOf(context).Add(target);
        var navigation = $"n{++_navigations}";
        Emit("browsingContext.navigationStarted", new JsonObject { ["context"] = context, ["navigation"] = navigation, ["url"] = target });
        if (HoldLoad)
            ReadyState = "loading";
        else
            Emit("browsingContext.load", new JsonObject { ["context"] = context, ["navigation"] = navigation, ["url"] = target });
    }

    private static JsonObject Success(JsonObject value) => new() { ["type"] = "success", ["result"] = value };
}
