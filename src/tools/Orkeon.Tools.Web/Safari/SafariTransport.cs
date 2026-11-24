using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Safari;

/// <summary>
/// The channel to Safari: the WebDriver BiDi commands the sessions send, translated into the classic WebDriver
/// of safaridriver, one at a time. Safari's own BiDi has no input, no screenshot and sends no event; classic
/// WebDriver sends none either, so the tabs, the dialogs and the navigations are looked at after each command.
/// </summary>
internal sealed class SafariTransport : IBiDiTransport
{
    private const string ElementKey = "element-6066-11e4-a52e-4f735466cecf";
    private const string AlertOpen = "unexpected alert open";

    // The page says it is being left as soon as it is; a document without the mark is another one.
    private const string ArmScript = """
        const key = Symbol.for('orkeon.leaving');
        if (window[key] === undefined) window.addEventListener('beforeunload', () => { window[key] = true; });
        window[key] = false;
        """;

    private const string WatchScript = "return [window[Symbol.for('orkeon.leaving')], document.readyState];";

    // Encodes a value as WebDriver BiDi does, to the depth the session asks for.
    private const string Encoder = """
        const encode = (v, depth) => {
          if (v === undefined) return { type: 'undefined' };
          if (v === null) return { type: 'null' };
          if (typeof v === 'number') return { type: 'number', value: Number.isFinite(v) ? v : String(v) };
          if (typeof v === 'string' || typeof v === 'boolean') return { type: typeof v, value: v };
          if (typeof v === 'bigint') return { type: 'bigint', value: String(v) };
          if (typeof v === 'function' || typeof v === 'symbol') return { type: typeof v };
          if (v instanceof Node) return { type: 'node' };
          if (v instanceof Date) return { type: 'date', value: isNaN(v) ? 'Invalid Date' : v.toISOString() };
          if (v instanceof RegExp) return { type: 'regexp', value: { pattern: v.source, flags: v.flags } };
          if (v instanceof Error) return { type: 'error' };
          const list = Array.isArray(v) ? 'array' : v instanceof Set ? 'set' : v instanceof NodeList ? 'nodelist' : v instanceof HTMLCollection ? 'htmlcollection' : null;
          if (list) return depth === 0 ? { type: list } : { type: list, value: Array.from(v, item => encode(item, depth - 1)) };
          const kind = v instanceof Map ? 'map' : 'object';
          if (depth === 0) return { type: kind };
          return { type: kind, value: (kind === 'map' ? [...v] : Object.entries(v)).map(([k, item]) => [typeof k === 'string' ? k : encode(k, depth - 1), encode(item, depth - 1)]) };
        };
        """;

    /// <summary>How long a page that said it is being left is given to be replaced: a download never replaces it.</summary>
    private static readonly TimeSpan s_leaveLimit = TimeSpan.FromSeconds(10);

    /// <summary>How long after an action a page may still start to leave.</summary>
    private static readonly TimeSpan s_watchLimit = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;
    private readonly Uri _session;
    private readonly Channel<string> _commands = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _stopping = new();
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private Task _pump = Task.CompletedTask;
    private string? _current;
    private string? _watched;
    private long _watchedSince;
    private bool _leaving;
    private bool _ended;
    private int _disposed;

    private SafariTransport(HttpClient http, Uri session)
    {
        _http = http;
        _session = session;
    }

    /// <summary>Waits for the driver listening at <paramref name="driver"/> and opens its session.</summary>
    /// <exception cref="FirefoxException">The driver refused the session.</exception>
    [SuppressMessage("Reliability", "CA2000", Justification = "The client is handed to the transport returned, which disposes it; a start that fails disposes it below.")]
    public static async Task<SafariTransport> ConnectAsync(Uri driver, TimeSpan commandTimeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(driver);

        // The driver gives up on a page or a script first: a command it holds holds the ones behind it.
        var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            Timeout = commandTimeout + TimeSpan.FromSeconds(10),
        };
        try
        {
            await WaitForDriverAsync(http, driver, cancellationToken).ConfigureAwait(false);
            var created = await CallAsync(http, HttpMethod.Post, new Uri(driver, "/session"), new JsonObject
            {
                ["capabilities"] = new JsonObject { ["alwaysMatch"] = new JsonObject { ["browserName"] = "Safari" } },
            }, cancellationToken).ConfigureAwait(false);
            var id = created?["sessionId"]?.GetValue<string>()
                ?? throw new FirefoxException("safaridriver opened a session without an id.");
            var transport = new SafariTransport(http, new Uri(driver, $"/session/{Uri.EscapeDataString(id)}/"));
            try
            {
                await transport.StartAsync(commandTimeout, cancellationToken).ConfigureAwait(false);
                return transport;
            }
            catch
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (WebDriverException ex)
        {
            http.Dispose();
            throw new FirefoxException(
                $"safaridriver refused the session ({ex.Message}). Remote automation must be allowed once on this Mac (sudo safaridriver --enable), and Safari takes one session at a time.", ex);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        if (!_commands.Writer.TryWrite(message))
            await _commands.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken) =>
        await _messages.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false) && _messages.Reader.TryRead(out var message)
            ? message
            : null;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _commands.Writer.TryComplete();
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        if (!_ended)
        {
            // The session closes its window: Safari itself, which may be the user's, is left alone.
            using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await CallAsync(_http, HttpMethod.Delete, SessionAddress, null, bounded.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebDriverException or HttpRequestException or OperationCanceledException)
            {
                // Gone already.
            }
        }

        _messages.Writer.TryComplete();
        _http.Dispose();
        _stopping.Dispose();
    }

    private Uri SessionAddress => new(_session.AbsoluteUri.TrimEnd('/'));

    private static async Task WaitForDriverAsync(HttpClient http, Uri driver, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                var status = await CallAsync(http, HttpMethod.Get, new Uri(driver, "/status"), null, cancellationToken).ConfigureAwait(false);
                if (status?["ready"] is JsonValue ready && ready.TryGetValue<bool>(out var isReady) && isReady)
                    return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StartAsync(TimeSpan commandTimeout, CancellationToken cancellationToken)
    {
        var limit = (long)commandTimeout.TotalMilliseconds;
        await PostAsync("timeouts", new JsonObject { ["pageLoad"] = limit, ["script"] = limit }, cancellationToken).ConfigureAwait(false);
        foreach (var handle in await HandlesAsync(cancellationToken).ConfigureAwait(false))
            _known.Add(handle);
        _pump = PumpAsync();
    }

    [SuppressMessage("Design", "CA1031", Justification = "An answer of an unexpected shape is the answer of that command, not the end of the channel.")]
    private async Task PumpAsync()
    {
        try
        {
            await foreach (var message in _commands.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
            {
                if (JsonNode.Parse(message) is not JsonObject command || command["id"] is not JsonValue id || command["method"] is not JsonValue method)
                    continue;

                var parameters = command["params"] as JsonObject ?? [];
                var answer = new JsonObject { ["id"] = id.DeepClone() };
                try
                {
                    answer["result"] = await RunAsync(method.GetValue<string>(), parameters, _stopping.Token).ConfigureAwait(false);
                    answer["type"] = "success";
                }
                catch (Exception ex) when (ex is not HttpRequestException && !_stopping.IsCancellationRequested)
                {
                    answer["type"] = "error";
                    answer["error"] = ex is WebDriverException refused ? BiDiCode(refused.Code) : ex is TaskCanceledException ? BiDiException.Timeout : "unknown error";
                    answer["message"] = ex.Message;
                }

                var gone = answer["error"]?.GetValue<string>() is "invalid session id";
                if (!gone && !_ended)
                    await LookAsync(Text(parameters["context"]) ?? Text(parameters["target"]?["context"]), _stopping.Token).ConfigureAwait(false);
                _messages.Writer.TryWrite(answer.ToJsonString());
                if (gone || _ended)
                    break;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            // Stopped, or the driver is gone.
        }
        finally
        {
            _commands.Writer.TryComplete();
            _messages.Writer.TryComplete();
        }
    }

    private async Task<JsonObject> RunAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        var context = Text(parameters["context"]) ?? Text(parameters["target"]?["context"]);
        switch (method)
        {
            case "session.new" or "session.subscribe":
                return [];
            case "browser.close":
                _ended = true;
                await CallAsync(_http, HttpMethod.Delete, SessionAddress, null, cancellationToken).ConfigureAwait(false);
                return [];
            case "browsingContext.getTree":
                return await TreeAsync(Text(parameters["root"]), cancellationToken).ConfigureAwait(false);
            case "browsingContext.create":
                return await CreateAsync(cancellationToken).ConfigureAwait(false);
            case "browsingContext.handleUserPrompt":
                // Dismissed when it was seen: the driver answers nothing while a dialog is open.
                throw new WebDriverException("no such alert", "The dialog was dismissed already.");
        }

        if (context is null)
            throw new WebDriverException("unknown command", method);
        await SwitchAsync(context, cancellationToken).ConfigureAwait(false);
        switch (method)
        {
            case "browsingContext.activate":
                return [];
            case "browsingContext.close":
                await CallAsync(HttpMethod.Delete, "window", null, cancellationToken).ConfigureAwait(false);
                _current = null;
                return [];
            case "browsingContext.navigate":
                _watched = null;
                await PostAsync("url", new JsonObject { ["url"] = Text(parameters["url"]) }, cancellationToken).ConfigureAwait(false);
                return [];
            case "browsingContext.traverseHistory":
                _watched = null;
                await PostAsync("back", [], cancellationToken).ConfigureAwait(false);
                return [];
            case "browsingContext.captureScreenshot":
                return new JsonObject { ["data"] = Text(await CallAsync(HttpMethod.Get, "screenshot", null, cancellationToken).ConfigureAwait(false)) };
            case "script.evaluate":
                return await EvaluateAsync(context, parameters, cancellationToken).ConfigureAwait(false);
            case "script.callFunction":
                return await CallFunctionAsync(context, parameters, cancellationToken).ConfigureAwait(false);
            case "input.performActions":
                await ArmAsync(context, cancellationToken).ConfigureAwait(false);
                await PerformAsync(parameters, cancellationToken).ConfigureAwait(false);
                return [];
            case "input.releaseActions":
                await CallAsync(HttpMethod.Delete, "actions", null, cancellationToken).ConfigureAwait(false);
                return [];
            default:
                throw new WebDriverException("unknown command", method);
        }
    }

    private async Task<JsonObject> TreeAsync(string? root, CancellationToken cancellationToken)
    {
        var contexts = new JsonArray();
        foreach (var handle in root is null ? await HandlesAsync(cancellationToken).ConfigureAwait(false) : [root])
        {
            await SwitchAsync(handle, cancellationToken).ConfigureAwait(false);
            var url = Text(await CallAsync(HttpMethod.Get, "url", null, cancellationToken).ConfigureAwait(false));
            contexts.Add(new JsonObject { ["context"] = handle, ["url"] = url, ["parent"] = null });
        }

        return new JsonObject { ["contexts"] = contexts };
    }

    private async Task<JsonObject> CreateAsync(CancellationToken cancellationToken)
    {
        var created = await PostAsync("window/new", new JsonObject { ["type"] = "tab" }, cancellationToken).ConfigureAwait(false);
        var handle = Text(created?["handle"]) ?? throw new WebDriverException("unknown error", "The tab opened has no handle.");
        _known.Add(handle);
        await SwitchAsync(handle, cancellationToken).ConfigureAwait(false);
        return new JsonObject { ["context"] = handle };
    }

    private async Task<JsonObject> EvaluateAsync(string context, JsonObject parameters, CancellationToken cancellationToken)
    {
        var expression = Text(parameters["expression"]) ?? string.Empty;
        var depth = parameters["serializationOptions"]?["maxObjectDepth"] is JsonValue wanted && wanted.TryGetValue<int>(out var levels) ? levels : 4;
        if (parameters["awaitPromise"] is not JsonValue awaited || !awaited.GetValue<bool>())
            return Success(Encode(await ExecuteAsync($"return (\n{expression}\n);", [], cancellationToken).ConfigureAwait(false)));

        await ArmAsync(context, cancellationToken).ConfigureAwait(false);
        JsonNode? answer;
        try
        {
            answer = await ExecuteAsyncScriptAsync($"(\n{expression}\n)", depth, cancellationToken).ConfigureAwait(false);
        }
        catch (WebDriverException ex) when (ex.Code == "javascript error")
        {
            // Not an expression: a script, whose value is the one of its last statement. A page whose policy forbids eval refuses it.
            answer = await ExecuteAsyncScriptAsync($"(0, eval)({JsonValue.Create(expression).ToJsonString()})", depth, cancellationToken).ConfigureAwait(false);
        }

        return JsonNode.Parse(Text(answer) ?? "{}") is JsonObject { } outcome && outcome["error"] is { } error
            ? Thrown(Text(error) ?? "unknown error")
            : Success((JsonNode.Parse(Text(answer) ?? "{}")?["value"] as JsonObject)?.DeepClone().AsObject() ?? new JsonObject { ["type"] = "undefined" });
    }

    private Task<JsonNode?> ExecuteAsyncScriptAsync(string expression, int depth, CancellationToken cancellationToken) =>
        PostAsync("execute/async", new JsonObject
        {
            ["script"] = $$"""
                {{Encoder}}
                const done = arguments[arguments.length - 1];
                (async () => {{expression}})().then(
                  v => done(JSON.stringify({ value: encode(v, {{depth}}) })),
                  e => done(JSON.stringify({ error: String(e) })));
                """,
            ["args"] = new JsonArray(),
        }, cancellationToken);

    private async Task<JsonObject> CallFunctionAsync(string context, JsonObject parameters, CancellationToken cancellationToken)
    {
        var arguments = new JsonArray();
        foreach (var argument in parameters["arguments"] as JsonArray ?? [])
        {
            if (Text(argument?["sharedId"]) is { } node)
            {
                await ArmAsync(context, cancellationToken).ConfigureAwait(false);
                arguments.Add(new JsonObject { [ElementKey] = node });
            }
            else
            {
                arguments.Add(argument?["value"]?.DeepClone());
            }
        }

        try
        {
            return Success(Encode(await ExecuteAsync(
                $"return (\n{Text(parameters["functionDeclaration"])}\n).apply(null, arguments);", arguments, cancellationToken).ConfigureAwait(false)));
        }
        catch (WebDriverException ex) when (ex.Code == "javascript error")
        {
            return Thrown(ex.Message);
        }
    }

    private async Task PerformAsync(JsonObject parameters, CancellationToken cancellationToken)
    {
        var sources = parameters["actions"]?.DeepClone() as JsonArray ?? [];
        foreach (var action in sources.SelectMany(source => source?["actions"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (Text(action["origin"]?["element"]?["sharedId"]) is { } node)
                action["origin"] = new JsonObject { [ElementKey] = node };
            if (Text(action["type"]) == "pointerMove")
                action["duration"] = 0;
        }

        try
        {
            await PostAsync("actions", new JsonObject { ["actions"] = sources }, cancellationToken).ConfigureAwait(false);
        }
        catch (WebDriverException ex) when (ex.Code == AlertOpen)
        {
            // The action opened the dialog: it was done.
            Announce(ex.AlertText);
        }
    }

    /// <summary>Marks the page, so that the action about to run is seen to leave it.</summary>
    private async Task ArmAsync(string context, CancellationToken cancellationToken)
    {
        await ExecuteAsync(ArmScript, [], cancellationToken).ConfigureAwait(false);
        _watched = context;
        _watchedSince = Stopwatch.GetTimestamp();
        _leaving = false;
    }

    /// <summary>Looks at what a browser that sends events would have announced: dialogs, tabs, and the page left.</summary>
    private async Task LookAsync(string? context, CancellationToken cancellationToken)
    {
        try
        {
            await LookForDialogAsync(context, cancellationToken).ConfigureAwait(false);
            await LookForTabsAsync(cancellationToken).ConfigureAwait(false);
            if (context is not null && context == _watched && _known.Contains(context))
                await LookForNavigationAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (WebDriverException ex)
        {
            if (ex.Code == AlertOpen)
                Announce(ex.AlertText);

            // The page or the tab is changing: the next command looks again.
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The driver did not answer in time: the next command says so.
        }
    }

    private async Task LookForDialogAsync(string? context, CancellationToken cancellationToken)
    {
        if (context is null || !string.Equals(context, _current, StringComparison.Ordinal))
            return;

        string? said;
        try
        {
            said = Text(await CallAsync(HttpMethod.Get, "alert/text", null, cancellationToken).ConfigureAwait(false));
        }
        catch (WebDriverException)
        {
            return;
        }

        await PostAsync("alert/dismiss", [], cancellationToken).ConfigureAwait(false);
        Announce(said);
    }

    private async Task LookForTabsAsync(CancellationToken cancellationToken)
    {
        var handles = await HandlesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var gone in _known.Except(handles).ToList())
        {
            _known.Remove(gone);
            if (_current == gone)
                _current = null;
            Raise("browsingContext.contextDestroyed", new JsonObject { ["context"] = gone });
        }

        foreach (var opened in handles.Where(_known.Add))
        {
            // Nobody else opens a tab in a window under automation: the page acted on last did.
            Raise("browsingContext.contextCreated", new JsonObject
            {
                ["context"] = opened,
                ["parent"] = null,
                ["originalOpener"] = _watched,
            });
        }
    }

    private async Task LookForNavigationAsync(string context, CancellationToken cancellationToken)
    {
        await SwitchAsync(context, cancellationToken).ConfigureAwait(false);
        if (await ExecuteAsync(WatchScript, [], cancellationToken).ConfigureAwait(false) is not JsonArray { Count: 2 } page)
            return;

        var replaced = page[0] is null;
        var elapsed = Stopwatch.GetElapsedTime(_watchedSince);
        if (!_leaving && (replaced || page[0]!.GetValue<bool>()))
        {
            _leaving = true;
            Raise("browsingContext.navigationStarted", new JsonObject { ["context"] = context });
        }

        if (replaced && Text(page[1]) == "complete")
        {
            _watched = null;
            Raise("browsingContext.load", new JsonObject { ["context"] = context });
        }
        else if (!replaced && elapsed > (_leaving ? s_leaveLimit : s_watchLimit))
        {
            _watched = null;
            if (_leaving)
                Raise("browsingContext.navigationAborted", new JsonObject { ["context"] = context });
        }
    }

    private void Announce(string? said) => Raise("browsingContext.userPromptOpened", new JsonObject
    {
        ["context"] = _current,
        ["type"] = "alert",
        ["message"] = said ?? string.Empty,
    });

    private void Raise(string method, JsonObject parameters) => _messages.Writer.TryWrite(
        new JsonObject { ["type"] = "event", ["method"] = method, ["params"] = parameters }.ToJsonString());

    private async Task SwitchAsync(string context, CancellationToken cancellationToken)
    {
        if (_current == context)
            return;
        await PostAsync("window", new JsonObject { ["handle"] = context }, cancellationToken).ConfigureAwait(false);
        _current = context;
    }

    private async Task<List<string>> HandlesAsync(CancellationToken cancellationToken) =>
        [.. (await CallAsync(HttpMethod.Get, "window/handles", null, cancellationToken).ConfigureAwait(false) as JsonArray ?? []).Select(Text).OfType<string>()];

    private Task<JsonNode?> ExecuteAsync(string script, JsonArray arguments, CancellationToken cancellationToken) =>
        PostAsync("execute/sync", new JsonObject { ["script"] = script, ["args"] = arguments }, cancellationToken);

    private Task<JsonNode?> PostAsync(string command, JsonObject body, CancellationToken cancellationToken) =>
        CallAsync(HttpMethod.Post, command, body, cancellationToken);

    private Task<JsonNode?> CallAsync(HttpMethod method, string command, JsonObject? body, CancellationToken cancellationToken) =>
        CallAsync(_http, method, new Uri(_session, command), body, cancellationToken);

    private static async Task<JsonNode?> CallAsync(HttpClient http, HttpMethod method, Uri address, JsonObject? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, address);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonNode? value;
        try
        {
            value = JsonNode.Parse(text)?["value"];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new WebDriverException("unknown error", $"safaridriver answered {(int)response.StatusCode} without JSON.");
        }

        if (response.IsSuccessStatusCode)
            return value;
        var error = value as JsonObject;
        throw new WebDriverException(Text(error?["error"]) ?? "unknown error", Text(error?["message"]) ?? string.Empty)
        {
            AlertText = Text((error?["data"] as JsonObject)?["text"]),
        };
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonObject Success(JsonObject value) => new() { ["type"] = "success", ["result"] = value };

    private static JsonObject Thrown(string text) => new()
    {
        ["type"] = "exception",
        ["exceptionDetails"] = new JsonObject { ["text"] = text },
    };

    /// <summary>A value of the driver, as BiDi describes it.</summary>
    private static JsonObject Encode(JsonNode? value) => value switch
    {
        null => new JsonObject { ["type"] = "null" },
        JsonArray items => new JsonObject { ["type"] = "array", ["value"] = new JsonArray([.. items.Select(Encode)]) },
        JsonObject element when Text(element[ElementKey]) is { } id => new JsonObject { ["type"] = "node", ["sharedId"] = id },
        JsonObject entries => new JsonObject
        {
            ["type"] = "object",
            ["value"] = new JsonArray([.. entries.Select(entry => new JsonArray(entry.Key, Encode(entry.Value)))]),
        },
        _ => new JsonObject
        {
            ["type"] = value.GetValueKind() switch
            {
                JsonValueKind.String => "string",
                JsonValueKind.Number => "number",
                _ => "boolean",
            },
            ["value"] = value.DeepClone(),
        },
    };

    private static string BiDiCode(string code) => code switch
    {
        "stale element reference" or "no such element" => BiDiException.NoSuchNode,
        "no such window" => BiDiException.NoSuchFrame,
        "timeout" or "script timeout" => BiDiException.Timeout,
        _ => code,
    };

    [SuppressMessage("Design", "CA1032", Justification = "Never leaves the transport, and always carries the error code of the driver.")]
    [SuppressMessage("Design", "CA1064", Justification = "Never leaves the transport.")]
    private sealed class WebDriverException(string code, string message) : Exception(string.IsNullOrEmpty(message) ? code : message)
    {
        public string Code { get; } = code;

        public string? AlertText { get; init; }
    }
}
