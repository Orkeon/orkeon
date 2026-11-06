using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Resources;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>How long a session waits for a page to react; shortened by tests.</summary>
internal sealed record FirefoxSessionTimings
{
    public static FirefoxSessionTimings Default { get; } = new();

    /// <summary>After a click or a key, how long a navigation is given to start before the page is taken as unchanged.</summary>
    public TimeSpan NavigationGrace { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>After <c>back</c>, how long the page is given to show it changed.</summary>
    public TimeSpan TraversalWait { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Delay between two looks at the page while waiting.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>How long a browser asked to close is given to answer.</summary>
    public TimeSpan CloseWait { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>Where the browser is: the address and the title of the page shown.</summary>
internal sealed record PageState(string Url, string Title, bool Loaded);

/// <summary>
/// One browser and the tab an agent drives in it: the WebDriver BiDi commands of each action, the wait
/// for the page an action leaves for, and the last snapshot of the page. Not thread-safe: its slot runs
/// one action at a time.
/// </summary>
internal sealed partial class FirefoxSession : IAsyncDisposable
{
    /// <summary>Most elements the outline of one page lists.</summary>
    internal const int MaxElements = 1000;

    /// <summary>Most characters of text one section of the outline keeps.</summary>
    internal const int MaxSectionText = 6000;

    /// <summary>Most characters of rendered text a snapshot keeps.</summary>
    internal const int MaxPageText = 200_000;

    private const string StateExpression = "JSON.stringify([location.href, document.title, document.readyState])";

    private const string ReachScript = """
        (el) => {
          if (!el.isConnected) return false;
          el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
          return true;
        }
        """;

    private const string FocusScript = """
        (el) => {
          if (!el.isConnected) return false;
          el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
          el.focus();
          if (typeof el.select === 'function') {
            el.select();
          } else if (el.isContentEditable) {
            const range = document.createRange();
            range.selectNodeContents(el);
            const selection = getSelection();
            selection.removeAllRanges();
            selection.addRange(range);
          }
          return true;
        }
        """;

    private const string ClickScript = "(el) => { el.click(); }";

    private static readonly Lazy<string> s_outlineScript = new(LoadOutlineScript);

    private readonly IFirefoxBrowser _browser;
    private readonly BiDiConnection _connection;
    private readonly IFileSystemService _fileSystem;
    private readonly FirefoxSessionTimings _timings;
    private readonly TimeSpan _commandTimeout;
    private readonly ILogger? _logger;
    private readonly ConcurrentQueue<string> _dialogs = new();
    private readonly Lock _navigation = new();
    private string _context = string.Empty;
    private string? _pendingNavigation;
    private string? _pendingTarget;
    private string? _pendingOrigin;
    private volatile string? _lastUrl;
    private long _activity;
    private int _generation;
    private int _disposed;
    private bool _pageIsCurrent;

    private FirefoxSession(
        IFirefoxBrowser browser, BiDiConnection connection, IFileSystemService fileSystem,
        TimeSpan commandTimeout, FirefoxSessionTimings timings, ILogger? logger)
    {
        _browser = browser;
        _connection = connection;
        _fileSystem = fileSystem;
        _commandTimeout = commandTimeout;
        _timings = timings;
        _logger = logger;
    }

    /// <summary><c>false</c> once the browser has gone: closed by the user, crashed, or disposed.</summary>
    public bool IsAlive => _disposed == 0 && !_connection.IsClosed;

    /// <summary><c>true</c> when the browser runs without a window.</summary>
    public bool Headless => _browser.Headless;

    /// <summary>
    /// The last snapshot of the page, or <c>null</c> once the tab shows another page: its node ids
    /// name elements as long as the page is the same, whatever the actions made in it.
    /// </summary>
    public PageSnapshot? Page { get; private set; }

    /// <summary>
    /// <c>true</c> while no action ran since <see cref="Page"/> was read: what it holds is what the
    /// browser shows. An action in the page keeps the snapshot and its ids, not this.
    /// </summary>
    public bool PageIsCurrent => Page is not null && _pageIsCurrent;

    /// <summary>
    /// The address of the page its user last checked against a policy of its own. The session only
    /// keeps it, so that a page already checked is not checked again at each action.
    /// </summary>
    public string? CheckedUrl { get; set; }

    /// <summary>Starts a browser and opens its session on the tab it shows.</summary>
    /// <exception cref="FirefoxException">Firefox is not installed, or did not start.</exception>
    /// <exception cref="BiDiException">The browser refused the session.</exception>
    [SuppressMessage("Reliability", "CA2000", Justification = "The connection is handed to the session returned, which disposes it; a start that fails disposes the session.")]
    public static async Task<FirefoxSession> OpenAsync(
        IFirefoxLauncher launcher, FirefoxToolOptions options, IFileSystemService fileSystem,
        FirefoxSessionTimings? timings, ILogger? logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var browser = await launcher.LaunchAsync(cancellationToken).ConfigureAwait(false);
        var commandTimeout = TimeSpan.FromSeconds(options.CommandTimeoutSeconds);
        var connection = new BiDiConnection(browser.Transport, commandTimeout, logger);
        var session = new FirefoxSession(browser, connection, fileSystem, commandTimeout, timings ?? FirefoxSessionTimings.Default, logger);
        try
        {
            await session.StartAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Loads <paramref name="url"/> and waits for the page.</summary>
    public async Task NavigateAsync(string url, CancellationToken cancellationToken)
    {
        Page = null;
        await SendAsync("browsingContext.navigate", new JsonObject
        {
            ["context"] = _context,
            ["url"] = url,
            ["wait"] = "complete",
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Goes one page back in the history of the tab and waits for it.</summary>
    public async Task BackAsync(CancellationToken cancellationToken)
    {
        var before = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        var activity = Interlocked.Read(ref _activity);
        Page = null;
        await SendAsync("browsingContext.traverseHistory", new JsonObject
        {
            ["context"] = _context,
            ["delta"] = -1,
        }, cancellationToken).ConfigureAwait(false);
        await SettleAsync(activity, before.Url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>true</c> while a navigation the browser announced has not ended.</summary>
    public bool NavigationPending
    {
        get
        {
            lock (_navigation)
                return _pendingNavigation is not null;
        }
    }

    /// <summary>The address and the title of the page shown now.</summary>
    public async Task<PageState> ReadStateAsync(CancellationToken cancellationToken) =>
        await TryReadStateAsync(cancellationToken).ConfigureAwait(false) ?? new PageState(await ReadUrlAsync(cancellationToken).ConfigureAwait(false), string.Empty, Loaded: false);

    /// <summary>Reads the page again: a new snapshot, whose node ids replace the earlier ones.</summary>
    public async Task<PageSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        var result = await CallFunctionAsync(
            s_outlineScript.Value, [Number(MaxElements), Number(MaxSectionText), Number(MaxPageText)], cancellationToken)
            .ConfigureAwait(false);
        if (result["value"] is not JsonArray { Count: > 0 } items
            || items[0]?["value"] is not JsonValue described || !described.TryGetValue<string>(out var json))
        {
            throw new FirefoxException("The page could not be read: its outline came back empty.");
        }

        var sharedIds = items.Skip(1)
            .Select(item => item?["sharedId"] is JsonValue id && id.TryGetValue<string>(out var sharedId) ? sharedId : null)
            .ToImmutableArray();
        Page = PageSnapshot.Parse(json, sharedIds, ++_generation, _fileSystem);
        _pageIsCurrent = true;
        return Page;
    }

    /// <summary>Clicks the element in the middle, scrolled into view first, then waits for the page it may open.</summary>
    /// <exception cref="FirefoxException">The element is no longer in the page.</exception>
    public async Task ClickAsync(string sharedId, CancellationToken cancellationToken)
    {
        var activity = Interlocked.Read(ref _activity);
        await ReachAsync(ReachScript, sharedId, cancellationToken).ConfigureAwait(false);
        try
        {
            await SendAsync("input.performActions", new JsonObject
            {
                ["context"] = _context,
                ["actions"] = new JsonArray(new JsonObject
                {
                    ["type"] = "pointer",
                    ["id"] = "mouse",
                    ["parameters"] = new JsonObject { ["pointerType"] = "mouse" },
                    ["actions"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "pointerMove",
                            ["x"] = 0,
                            ["y"] = 0,
                            ["origin"] = new JsonObject { ["type"] = "element", ["element"] = Reference(sharedId) },
                        },
                        new JsonObject { ["type"] = "pointerDown", ["button"] = 0 },
                        new JsonObject { ["type"] = "pointerUp", ["button"] = 0 }),
                }),
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (BiDiException ex) when (ex.Code == BiDiException.NoSuchNode)
        {
            throw Stale(ex);
        }
        catch (BiDiException ex) when (ex.Code is not (BiDiException.ConnectionClosed or BiDiException.Timeout))
        {
            // No point of the element can be reached by the pointer (it has no box, or lies outside
            // the viewport even scrolled): the page is told of the click instead.
            await CallOnNodeAsync(ClickScript, sharedId, cancellationToken).ConfigureAwait(false);
        }

        await ReleaseAsync(cancellationToken).ConfigureAwait(false);
        await SettleAsync(activity, changedFrom: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the content of the element by <paramref name="text"/>, typed key by key: the element is
    /// focused, what it holds is selected, and the keys overwrite the selection.
    /// </summary>
    /// <exception cref="FirefoxException">The element is no longer in the page.</exception>
    public async Task TypeAsync(string sharedId, string text, CancellationToken cancellationToken)
    {
        var activity = Interlocked.Read(ref _activity);
        await ReachAsync(FocusScript, sharedId, cancellationToken).ConfigureAwait(false);
        var keys = text.Length == 0 ? [FirefoxKeys.Backspace] : FirefoxKeys.Typing(text);
        await PressKeysAsync(keys.Select(key => (IReadOnlyList<string>)[key]), cancellationToken).ConfigureAwait(false);
        await SettleAsync(activity, changedFrom: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Presses one key or chord (<see cref="FirefoxKeys.TryParse"/>) on the focused element.</summary>
    public async Task PressAsync(IReadOnlyList<string> chord, CancellationToken cancellationToken)
    {
        var activity = Interlocked.Read(ref _activity);
        await PressKeysAsync([chord], cancellationToken).ConfigureAwait(false);
        await SettleAsync(activity, changedFrom: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="script"/> in the page and returns its value as text; a promise is awaited.</summary>
    /// <exception cref="FirefoxException">The script threw.</exception>
    public async Task<string> EvaluateAsync(string script, CancellationToken cancellationToken)
    {
        var activity = Interlocked.Read(ref _activity);
        var answer = await SendAsync("script.evaluate", new JsonObject
        {
            ["expression"] = script,
            ["target"] = new JsonObject { ["context"] = _context },
            ["awaitPromise"] = true,
            ["resultOwnership"] = "none",
            ["userActivation"] = true,
            ["serializationOptions"] = new JsonObject { ["maxObjectDepth"] = 4, ["maxDomDepth"] = 0 },
        }, cancellationToken).ConfigureAwait(false);
        var value = BiDiValue.Render(ScriptResult(answer, "The script threw"));
        await SettleAsync(activity, changedFrom: null, cancellationToken).ConfigureAwait(false);
        return value;
    }

    /// <summary>The visible part of the page as a PNG image.</summary>
    public async Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken)
    {
        var answer = await SendAsync("browsingContext.captureScreenshot", new JsonObject
        {
            ["context"] = _context,
            ["origin"] = "viewport",
        }, cancellationToken).ConfigureAwait(false);
        try
        {
            return Convert.FromBase64String(answer["data"]?.GetValue<string>() ?? string.Empty);
        }
        catch (FormatException ex)
        {
            throw new FirefoxException("The browser returned a screenshot that is not an image.", ex);
        }
    }

    /// <summary>What the dialogs the page opened since the last call said; they were dismissed.</summary>
    public IReadOnlyList<string> TakeDialogs()
    {
        var said = new List<string>();
        while (_dialogs.TryDequeue(out var message))
            said.Add(message);
        return said;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Page = null;
        if (!_connection.IsClosed)
        {
            try
            {
                // A browser asked to close leaves by itself; one that does not answer is killed below.
                await _connection.SendAsync("browser.close", null, CancellationToken.None)
                    .WaitAsync(_timings.CloseWait).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is BiDiException or TimeoutException)
            {
                // Gone already, or deaf.
            }
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
        await _browser.DisposeAsync().ConfigureAwait(false);
    }

    private static string LoadOutlineScript()
    {
        const string name = "Orkeon.Tools.Web.Firefox.page-outline.js";
        using var stream = typeof(FirefoxSession).Assembly.GetManifestResourceStream(name)
            ?? throw new MissingManifestResourceException($"The embedded script '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonObject Number(int value) => new() { ["type"] = "number", ["value"] = value };

    private static JsonObject Reference(string sharedId) => new() { ["sharedId"] = sharedId };

    /// <summary>The value a script returned, or the failure it threw.</summary>
    private static JsonObject ScriptResult(JsonObject answer, string failure)
    {
        if (string.Equals(answer["type"]?.GetValue<string>(), "exception", StringComparison.Ordinal))
        {
            var text = answer["exceptionDetails"]?["text"] is JsonValue detail && detail.TryGetValue<string>(out var said) ? said : "unknown error";
            throw new FirefoxException($"{failure}: {text}");
        }

        return answer["result"] as JsonObject ?? [];
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        await SendAsync("session.new", new JsonObject
        {
            ["capabilities"] = new JsonObject { ["alwaysMatch"] = new JsonObject { ["acceptInsecureCerts"] = false } },
        }, cancellationToken).ConfigureAwait(false);

        var tree = await SendAsync("browsingContext.getTree", new JsonObject { ["maxDepth"] = 0 }, cancellationToken).ConfigureAwait(false);
        _context = (tree["contexts"] as JsonArray)?.FirstOrDefault()?["context"]?.GetValue<string>()
            ?? throw new FirefoxException("Firefox started without a tab to drive.");

        _connection.EventReceived = OnEvent;
        await SendAsync("session.subscribe", new JsonObject
        {
            ["events"] = new JsonArray("browsingContext"),
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonObject> SendAsync(string method, JsonObject parameters, CancellationToken cancellationToken) =>
        _connection.SendAsync(method, parameters, cancellationToken);

    private async Task<JsonObject> CallFunctionAsync(string function, JsonArray arguments, CancellationToken cancellationToken)
    {
        var answer = await SendAsync("script.callFunction", new JsonObject
        {
            ["functionDeclaration"] = function,
            ["target"] = new JsonObject { ["context"] = _context },
            ["arguments"] = arguments,
            ["awaitPromise"] = false,
            ["resultOwnership"] = "none",
            ["serializationOptions"] = new JsonObject { ["maxDomDepth"] = 0 },
        }, cancellationToken).ConfigureAwait(false);
        return ScriptResult(answer, "The page could not be read");
    }

    /// <summary>Calls <paramref name="function"/> on the element; a reference the page no longer knows is a stale node.</summary>
    private async Task<JsonObject> CallOnNodeAsync(string function, string sharedId, CancellationToken cancellationToken)
    {
        try
        {
            return await CallFunctionAsync(function, [Reference(sharedId)], cancellationToken).ConfigureAwait(false);
        }
        catch (BiDiException ex) when (ex.Code == BiDiException.NoSuchNode)
        {
            throw Stale(ex);
        }
    }

    /// <summary>Scrolls to the element (and focuses it, by <paramref name="function"/>); fails when it left the page.</summary>
    private async Task ReachAsync(string function, string sharedId, CancellationToken cancellationToken)
    {
        var reached = await CallOnNodeAsync(function, sharedId, cancellationToken).ConfigureAwait(false);
        if (reached["value"] is JsonValue value && value.TryGetValue<bool>(out var connected) && !connected)
            throw Stale(null);
    }

    private static FirefoxException Stale(Exception? cause) =>
        new("The node is no longer in the page: call snapshot again and use the new node ids.", cause);

    private async Task PressKeysAsync(IEnumerable<IReadOnlyList<string>> chords, CancellationToken cancellationToken)
    {
        var actions = new JsonArray();
        foreach (var chord in chords)
        {
            foreach (var key in chord)
                actions.Add(new JsonObject { ["type"] = "keyDown", ["value"] = key });
            foreach (var key in chord.Reverse())
                actions.Add(new JsonObject { ["type"] = "keyUp", ["value"] = key });
        }

        await SendAsync("input.performActions", new JsonObject
        {
            ["context"] = _context,
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "key", ["id"] = "keyboard", ["actions"] = actions }),
        }, cancellationToken).ConfigureAwait(false);
        await ReleaseAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync("input.releaseActions", new JsonObject { ["context"] = _context }, cancellationToken).ConfigureAwait(false);
        }
        catch (BiDiException ex) when (ex.Code is not (BiDiException.ConnectionClosed or BiDiException.Timeout))
        {
            // The page the keys were held in is gone: nothing is held any more.
        }
    }

    /// <summary>
    /// Waits for the page the last action may have left for. A navigation the action started is
    /// announced by the browser at once: it is waited for until its page is loaded. Without one, the
    /// page is taken as it is after a short grace - or, when <paramref name="changedFrom"/> is given,
    /// once its address is no longer that one. A page still loading when the command timeout is reached
    /// is left to load: the caller reads what is there.
    /// </summary>
    private async Task SettleAsync(long activityBefore, string? changedFrom, CancellationToken cancellationToken)
    {
        _pageIsCurrent = false;
        var state = await WaitForPageAsync(activityBefore, changedFrom, cancellationToken).ConfigureAwait(false);

        // Another page, or the same one loaded again: the elements the snapshot names are gone.
        if (Page is { } page
            && (state is null
                || Interlocked.Read(ref _activity) != activityBefore
                || !string.Equals(state.Url, page.Url, StringComparison.Ordinal)))
        {
            Page = null;
        }
    }

    /// <summary>The page once it has settled, or <c>null</c> when it was still loading at the command timeout.</summary>
    private async Task<PageState?> WaitForPageAsync(long activityBefore, string? changedFrom, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var quiet = changedFrom is null ? _timings.NavigationGrace : _timings.TraversalWait;
        while (true)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed >= _commandTimeout)
            {
                // Given up on: a navigation that never ends must not hold the next actions as well.
                lock (_navigation)
                    _pendingNavigation = null;
                return null;
            }

            if (await TryReadStateAsync(cancellationToken).ConfigureAwait(false) is { Loaded: true } state && !StillNavigating(state))
            {
                var moved = Interlocked.Read(ref _activity) != activityBefore;
                if (moved || elapsed >= quiet
                    || (changedFrom is not null && !string.Equals(state.Url, changedFrom, StringComparison.Ordinal)))
                {
                    return state;
                }
            }

            await Task.Delay(_timings.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether the navigation announced is still under way, <paramref name="shown"/> being the loaded
    /// page. A page restored from the history cache (<c>back</c> to a page left a moment ago) is
    /// announced and never reported loaded: its navigation has ended once the page shown is the one
    /// announced, when that is another address than the one left.
    /// </summary>
    private bool StillNavigating(PageState shown)
    {
        lock (_navigation)
        {
            if (_pendingNavigation is not null
                && _pendingTarget is { Length: > 0 } target
                && !string.Equals(target, _pendingOrigin, StringComparison.Ordinal)
                && string.Equals(shown.Url, target, StringComparison.Ordinal))
            {
                _pendingNavigation = null;
            }

            return _pendingNavigation is not null;
        }
    }

    /// <summary>The state of the page, or <c>null</c> while it cannot be asked: its document is being replaced.</summary>
    private async Task<PageState?> TryReadStateAsync(CancellationToken cancellationToken)
    {
        JsonObject answer;
        try
        {
            answer = await SendAsync("script.evaluate", new JsonObject
            {
                ["expression"] = StateExpression,
                ["target"] = new JsonObject { ["context"] = _context },
                ["awaitPromise"] = false,
                ["resultOwnership"] = "none",
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (BiDiException ex) when (ex.Code is not (BiDiException.ConnectionClosed or BiDiException.Timeout))
        {
            return null;
        }

        if (answer["result"]?["value"] is not JsonValue value || !value.TryGetValue<string>(out var json))
            return null;

        try
        {
            if (JsonSerializer.Deserialize<string[]>(json) is not [var url, var title, var ready])
                return null;

            _lastUrl = url;
            return new PageState(url, title, string.Equals(ready, "complete", StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string> ReadUrlAsync(CancellationToken cancellationToken)
    {
        var tree = await SendAsync("browsingContext.getTree", new JsonObject
        {
            ["root"] = _context,
            ["maxDepth"] = 0,
        }, cancellationToken).ConfigureAwait(false);
        return (tree["contexts"] as JsonArray)?.FirstOrDefault()?["url"]?.GetValue<string>() ?? string.Empty;
    }

    /// <summary>Runs on the receive loop of the connection: never waits for a command.</summary>
    private void OnEvent(string method, JsonObject parameters)
    {
        var context = parameters["context"] is JsonValue value && value.TryGetValue<string>(out var id) ? id : null;
        if (method == "browsingContext.contextCreated")
        {
            // A link opened a tab of its own: the agent follows it, as a user's eyes would.
            if (context is not null && context != _context && parameters["parent"] is null
                && parameters["originalOpener"] is not null)
            {
                _context = context;
                lock (_navigation)
                    _pendingNavigation = null;
                Interlocked.Increment(ref _activity);
            }

            return;
        }

        if (context != _context)
            return;

        var navigation = parameters["navigation"] is JsonValue described && described.TryGetValue<string>(out var number) ? number : null;
        switch (method)
        {
            case "browsingContext.navigationStarted":
                lock (_navigation)
                {
                    _pendingNavigation = navigation ?? string.Empty;
                    _pendingTarget = parameters["url"] is JsonValue target && target.TryGetValue<string>(out var url) ? url : null;
                    _pendingOrigin = _lastUrl;
                }

                Interlocked.Increment(ref _activity);
                break;

            case "browsingContext.load":
            case "browsingContext.navigationFailed":
            case "browsingContext.navigationAborted":
            case "browsingContext.downloadWillBegin":
                lock (_navigation)
                {
                    // The end of a navigation another one replaced is not the end of the pending one.
                    if (_pendingNavigation is not null && (navigation is null || _pendingNavigation.Length == 0 || _pendingNavigation == navigation))
                        _pendingNavigation = null;
                }

                Interlocked.Increment(ref _activity);
                break;

            case "browsingContext.fragmentNavigated":
            case "browsingContext.historyUpdated":
                Interlocked.Increment(ref _activity);
                break;

            case "browsingContext.userPromptOpened":
                DismissDialog(parameters);
                break;
        }
    }

    /// <summary>A dialog freezes the page until it is answered: it is dismissed, and what it said is kept for the agent.</summary>
    private void DismissDialog(JsonObject parameters)
    {
        var type = parameters["type"] is JsonValue kind && kind.TryGetValue<string>(out var name) ? name : "alert";
        var message = parameters["message"] is JsonValue said && said.TryGetValue<string>(out var text) ? text : string.Empty;
        // Leaving a page that asks "leave?" is what the agent asked for; anything else is refused.
        var accept = type == "beforeunload";
        if (!accept)
            _dialogs.Enqueue($"{type}: {message}");

        _ = AnswerAsync();

        async Task AnswerAsync()
        {
            try
            {
                await SendAsync("browsingContext.handleUserPrompt", new JsonObject
                {
                    ["context"] = _context,
                    ["accept"] = accept,
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (BiDiException ex)
            {
                if (_logger is not null) LogDialogNotDismissed(_logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "A dialog of the page could not be dismissed.")]
    private static partial void LogDialogNotDismissed(ILogger logger, Exception exception);
}
