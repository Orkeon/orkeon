using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An MCP server over Streamable HTTP on the loopback (FWK-17): sessions (<c>Mcp-Session-Id</c>), a held
/// <c>GET</c> stream a test pushes frames into, a held <c>subscriptions/listen</c> stream, SSE-framed
/// answers a test can pause halfway, and a record of every request it received.
/// </summary>
internal sealed class FakeStreamableHttpServer : IAsyncDisposable
{
    internal sealed record Seen(string Verb, string? Method, string? SessionId, string? LastEventId, string? ProtocolVersion, string Body);

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly List<Seen> _seen = [];
    private readonly List<Task> _handlers = [];
    private readonly Lock _gate = new();
    private int _sessions;
    private string? _validSession;
    private Channel<string?> _getFrames = Channel.CreateUnbounded<string?>();
    private Channel<string?> _listenFrames = Channel.CreateUnbounded<string?>();
    private int _gets;
    private int _reads;

    public FakeStreamableHttpServer(params string[] tools)
    {
        Tools = tools;
        (_listener, Url) = LoopbackPorts.StartListener("/mcp/");
        _loop = Task.Run(ServeAsync);
    }

    /// <summary>Decides, from the verb and the Authorization header, whether a request is let in; a refused one gets 401.</summary>
    public Func<string, string?, bool>? Authorize { get; set; }

    public Uri Url { get; }

    public string[] Tools { get; set; }

    /// <summary>A modern (2026-07-28) server answers <c>server/discover</c>; a legacy one answers <c>initialize</c>.</summary>
    public bool Modern { get; init; }

    /// <summary>Whether <c>initialize</c> hands out a session id.</summary>
    public bool UseSessions { get; init; }

    /// <summary>Whether <c>initialize</c> is answered 500, as a server that is up but cannot start a session.</summary>
    public volatile bool FailInitialize;

    /// <summary>Whether the server announces <c>resources.subscribe</c>.</summary>
    public bool ResourceSubscribe { get; init; } = true;

    /// <summary>Whether a <c>GET</c> opens a stream; otherwise it is answered 405.</summary>
    public bool AllowGet { get; init; }

    /// <summary>Whether a <c>GET</c> on a live session is answered 404, as a server that lost the stream's session would.</summary>
    public bool GetNotFound { get; init; }

    /// <summary>When set, <c>resources/read</c> pauses before it answers until the task completes.</summary>
    public TaskCompletionSource? ReadGate { get; set; }

    /// <summary>Whether answers are framed as <c>text/event-stream</c>.</summary>
    public bool SseAnswers { get; init; }

    /// <summary>SSE frames sent ahead of the answer to <c>tools/list</c> when <see cref="SseAnswers"/> is set.</summary>
    public List<string> FramesBeforeToolsList { get; } = [];

    /// <summary>When set, <c>tools/list</c> pauses after its leading frames until the task completes.</summary>
    public TaskCompletionSource? ToolsListGate { get; set; }

    public int GetCount => Volatile.Read(ref _gets);

    public int ReadCount => Volatile.Read(ref _reads);

    public string? CurrentSession { get { lock (_gate) return _validSession; } }

    public IReadOnlyList<Seen> Requests { get { lock (_gate) return [.. _seen]; } }

    /// <summary>Makes every session id handed out so far stale: a request carrying one is answered 404.</summary>
    public void ExpireSessions() { lock (_gate) _validSession = null; }

    private static string Frame(JsonNode node, string? id) =>
        (id is null ? "" : $"id: {id}\n") + $"data: {node.ToJsonString()}\n\n";

    /// <summary>Pushes a notification on the held <c>GET</c> stream, with an event id.</summary>
    public void PushOnGet(string method, string eventId, JsonObject? parameters = null) =>
        _getFrames.Writer.TryWrite(Frame(Notification(method, parameters), eventId));

    /// <summary>Pushes a raw JSON-RPC frame (a request of the server, for instance) on the held <c>GET</c> stream.</summary>
    public void PushRawOnGet(string json, string eventId) =>
        _getFrames.Writer.TryWrite($"id: {eventId}\ndata: {json}\n\n");

    /// <summary>Closes the held <c>GET</c> stream from the server side.</summary>
    public void DropGet() => _getFrames.Writer.TryWrite(null);

    /// <summary>Pushes a notification on the held <c>subscriptions/listen</c> stream.</summary>
    public void PushOnListen(string method, JsonObject? parameters = null) =>
        _listenFrames.Writer.TryWrite(Frame(Notification(method, parameters), null));

    /// <summary>Ends the held <c>subscriptions/listen</c> stream.</summary>
    public void EndListen() => _listenFrames.Writer.TryWrite(null);

    public static JsonObject Notification(string method, JsonObject? parameters = null)
    {
        var n = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (parameters is not null)
            n["params"] = parameters;
        return n;
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            var handler = Task.Run(() => HandleAsync(context));
            lock (_gate)
                _handlers.Add(handler);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            await HandleCoreAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException or OperationCanceledException or InvalidOperationException)
        {
            // The client or the test went away: nothing to answer.
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                // Already closed.
            }
        }
    }

    private async Task HandleCoreAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var session = request.Headers["Mcp-Session-Id"];
        string body = "";
        if (request.HasEntityBody)
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        JsonObject? json = body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null;
        var method = json?["method"]?.GetValue<string>();

        // The stream a test pushes into exists before the request is recorded: a test that saw the request can push.
        var getFrames = _getFrames;
        var listenFrames = _listenFrames;
        if (request.HttpMethod == "GET" && AllowGet)
            _getFrames = getFrames = Channel.CreateUnbounded<string?>();
        if (method == "subscriptions/listen")
            _listenFrames = listenFrames = Channel.CreateUnbounded<string?>();
        lock (_gate)
            _seen.Add(new Seen(request.HttpMethod, method, session, request.Headers["Last-Event-ID"], request.Headers["MCP-Protocol-Version"], body));

        if (Authorize is { } authorize && !authorize(request.HttpMethod, request.Headers["Authorization"]))
        {
            response.StatusCode = 401;
            response.Headers["WWW-Authenticate"] = "Bearer";
            return;
        }

        if (request.HttpMethod == "DELETE")
        {
            response.StatusCode = 200;
            return;
        }

        if (FailInitialize && method == "initialize")
        {
            response.StatusCode = 500;
            return;
        }

        var isHandshake = method is "initialize" or "server/discover";
        if (UseSessions && session is not null && !isHandshake)
        {
            bool valid;
            lock (_gate)
                valid = session == _validSession;
            if (!valid)
            {
                response.StatusCode = 404;
                return;
            }
        }

        if (request.HttpMethod == "GET")
        {
            Interlocked.Increment(ref _gets);
            if (GetNotFound && session is not null)
            {
                response.StatusCode = 404;
                return;
            }

            if (!AllowGet)
            {
                response.StatusCode = 405;
                return;
            }

            await StreamAsync(response, getFrames).ConfigureAwait(false);
            return;
        }

        if (json is null)
        {
            response.StatusCode = 400;
            return;
        }

        if (!json.TryGetPropertyValue("id", out var id) || id is null)
        {
            response.StatusCode = 202;
            return;
        }

        if (method == "subscriptions/listen")
        {
            await StreamAsync(response, listenFrames).ConfigureAwait(false);
            return;
        }

        if (method == "initialize" && UseSessions)
        {
            var next = $"sess-{Interlocked.Increment(ref _sessions)}";
            lock (_gate)
                _validSession = next;
            response.Headers["Mcp-Session-Id"] = next;
        }

        if (method == "resources/read" && ReadGate is { } readGate)
            await readGate.Task.WaitAsync(_stop.Token).ConfigureAwait(false);

        var result = Answer(method, json);
        var answer = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone() };
        if (result is null)
            answer["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" };
        else
            answer["result"] = result;

        if (!SseAnswers)
        {
            var bytes = Encoding.UTF8.GetBytes(answer.ToJsonString());
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
            return;
        }

        response.ContentType = "text/event-stream";
        response.SendChunked = true;
        if (method == "tools/list")
        {
            foreach (var frame in FramesBeforeToolsList)
                await WriteAsync(response, frame).ConfigureAwait(false);
            if (ToolsListGate is { } gate)
                await gate.Task.WaitAsync(_stop.Token).ConfigureAwait(false);
        }

        await WriteAsync(response, Frame(answer, null)).ConfigureAwait(false);
    }

    private async Task StreamAsync(HttpListenerResponse response, Channel<string?> frames)
    {
        response.ContentType = "text/event-stream";
        response.SendChunked = true;
        await WriteAsync(response, ": open\n\n").ConfigureAwait(false);
        await foreach (var frame in frames.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
        {
            if (frame is null)
                return;
            await WriteAsync(response, frame).ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(HttpListenerResponse response, string text)
    {
        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(text), _stop.Token).ConfigureAwait(false);
        await response.OutputStream.FlushAsync(_stop.Token).ConfigureAwait(false);
    }

    private JsonNode? Answer(string? method, JsonObject request)
    {
        var capabilities = new JsonObject
        {
            ["tools"] = new JsonObject { ["listChanged"] = true },
            ["resources"] = new JsonObject { ["subscribe"] = ResourceSubscribe },
        };
        switch (method)
        {
            case "server/discover" when Modern:
                return new JsonObject
                {
                    ["supportedVersions"] = new JsonArray("2026-07-28"),
                    ["capabilities"] = capabilities,
                };
            case "initialize" when !Modern:
                return new JsonObject
                {
                    ["protocolVersion"] = "2025-06-18",
                    ["capabilities"] = capabilities,
                    ["serverInfo"] = new JsonObject { ["name"] = "fake", ["version"] = "1.0" },
                };
            case "tools/list":
                return new JsonObject
                {
                    ["tools"] = new JsonArray(Tools.Select(name => (JsonNode)new JsonObject
                    {
                        ["name"] = name,
                        ["description"] = name,
                        ["inputSchema"] = new JsonObject { ["type"] = "object" },
                    }).ToArray()),
                };
            case "resources/subscribe":
            case "resources/unsubscribe":
                return new JsonObject();
            case "resources/read":
                Interlocked.Increment(ref _reads);
                return new JsonObject
                {
                    ["contents"] = new JsonArray(new JsonObject { ["uri"] = request["params"]?["uri"]?.GetValue<string>(), ["text"] = $"read-{ReadCount}" }),
                };
            default:
                return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        // Close() alone: after Stop(), it would bind the port again for an instant (GAP-41).
        _listener.Close();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The listener went away under the pending accept: the stop we asked for.
        }

        Task[] handlers;
        lock (_gate)
            handlers = [.. _handlers];
        await Task.WhenAll(handlers).ConfigureAwait(false);
        _stop.Dispose();
    }
}
