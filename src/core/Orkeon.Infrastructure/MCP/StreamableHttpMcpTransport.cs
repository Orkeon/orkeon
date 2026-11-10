using Microsoft.Extensions.Logging;
using System.Text.Json;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.MCP.Auth;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// MCP transport over Streamable HTTP. Each message is a POST; the answer is read as JSON or as an SSE
/// stream, event by event, so the notifications that precede the final message are heard as they arrive.
/// The <c>Mcp-Session-Id</c> a server hands out is kept and sent back; a 404 on a session starts a new one
/// (the initialize handshake is replayed once). A legacy session also opens a long-lived <c>GET</c> stream
/// for the server's own messages, resumed with <c>Last-Event-ID</c> and re-opened with a bounded backoff.
/// A modern server is heard through <c>subscriptions/listen</c>, a request whose answer stream stays open.
/// The separate-endpoint HTTP+SSE transport of 2024-11-05 is not supported.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class StreamableHttpMcpTransport : IMcpTransport
{
    private const string SessionHeader = "Mcp-Session-Id";
    private const string SseMediaType = "text/event-stream";
    private const string InitializedMethod = "notifications/initialized";

    /// <summary>The wait before the first re-opening of the GET stream; each failure doubles it.</summary>
    internal static readonly TimeSpan ReconnectInitialDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>The longest wait between two openings of the GET stream, whatever the server's <c>retry:</c> asks.</summary>
    internal static readonly TimeSpan ReconnectMaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>The wait before the next opening of a stream after <paramref name="attempt"/> failures: doubled each time, bounded.</summary>
    internal static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromTicks(Math.Min(ReconnectMaxDelay.Ticks, ReconnectInitialDelay.Ticks << Math.Clamp(attempt, 0, 16)));

    /// <summary>How many times in a row the GET stream re-initialises the session on a 404 before it gives up.</summary>
    internal const int MaxConsecutiveReinitializations = 3;

    private readonly Uri _url;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IMcpRequestAuthenticator? _authenticator;
    private volatile bool _isConnected;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly System.Threading.Channels.Channel<JsonRpcNotification> _notifications;
    private readonly SemaphoreSlim _reinitialize = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _state = new();
    private string? _sessionId;
    private string? _protocolVersion;
    private string? _initializeJson;
    private string? _lastEventId;
    private Task _getLoop = Task.CompletedTask;
    private bool _getStarted;

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Raised when the server may have said something the client did not hear: the GET stream was opened again
    /// after it dropped, or the session was initialised again. A subscriber drops what it cached from the
    /// server's notifications and subscribes again.
    /// </summary>
    internal event Action? Resumed;

    /// <summary>The session id the server handed out (<c>Mcp-Session-Id</c>), or null while it has none.</summary>
    public string? SessionId
    {
        get { lock (_state) return _sessionId; }
    }

    /// <summary>
    /// The notifications the server sent: those ahead of the final message of an SSE-framed answer, those of
    /// a long-lived answer (<c>subscriptions/listen</c>) and those of the GET stream.
    /// </summary>
    public System.Threading.Channels.ChannelReader<JsonRpcNotification> Notifications => _notifications.Reader;

    /// <summary>Initializes a new instance of <see cref="StreamableHttpMcpTransport"/>.</summary>
    /// <param name="url">The HTTP endpoint URL for the MCP server.</param>
    /// <param name="httpClient">Optional HTTP client to use.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="authenticator">
    /// Optional authenticator applied to every request; the transport owns it and disposes it with itself when it is
    /// <see cref="IDisposable"/>.
    /// </param>
    /// <param name="timeProvider">The clock of the GET stream's reconnection delays; the system clock by default.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the self-created HttpClient is transferred to the _httpClient field (_ownsHttpClient=true) and disposed in DisposeAsync; the HttpClient in turn owns and disposes the SocketsHttpHandler.")]
    public StreamableHttpMcpTransport(Uri url, HttpClient? httpClient = null, ILogger<StreamableHttpMcpTransport>? logger = null,
        IMcpRequestAuthenticator? authenticator = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        _url = url;
        _authenticator = authenticator;
        _time = timeProvider ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _notifications = System.Threading.Channels.Channel.CreateBounded<JsonRpcNotification>(
            new System.Threading.Channels.BoundedChannelOptions(StdioMcpTransport.NotificationCapacity)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
            },
            dropped => LogNotificationDropped(dropped.Method, url));

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            // PooledConnectionLifetime recycles pooled connections so this self-owned
            // fallback client picks up DNS changes on long-lived agent processes (ANT-013).
            // The same settings as the shared client (McpServiceExtensions): no client-wide timeout, since a long-lived
            // stream must stay open and each call is bounded by CallTimeout; no redirect, since the headers and
            // credentials go to the configured host only.
            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _ownsHttpClient = true;
        }
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken ct = default)
    {
        _isConnected = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendRequestCoreAsync();

        async Task<JsonRpcResponse> SendRequestCoreAsync()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Transport is not connected.");

            var json = JsonSerializer.Serialize(request);
            if (request.Method == "initialize")
            {
                lock (_state)
                    _initializeJson = json;
            }

            using var httpResponse = await PostAsync(json, request.Method, request.Params, allowReinitialize: true, ct).ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();

            JsonRpcResponse? response;
            if (string.Equals(httpResponse.Content.Headers.ContentType?.MediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
            {
                response = await ReadStreamedAnswerAsync(httpResponse, request, ct).ConfigureAwait(false);
            }
            else
            {
                var body = await ReadBodyAsync(httpResponse.Content, ct).ConfigureAwait(false);
                response = JsonSerializer.Deserialize<JsonRpcResponse>(body);
            }

            if (response == null)
            {
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = new JsonRpcError(JsonRpcErrorCodes.InternalError, "Empty response from server")
                };
            }

            if (request.Method == "initialize" && response.Error is null)
                RememberNegotiatedVersion(response);

            return response;
        }
    }

    /// <inheritdoc />
    public Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return SendNotificationCoreAsync();

        async Task SendNotificationCoreAsync()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Transport is not connected.");

            var json = JsonSerializer.Serialize(notification);
            using var httpResponse = await PostAsync(json, notification.Method, notification.Params, allowReinitialize: true, ct).ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();

            // A legacy session is up: the server may now speak on its own, through the GET stream.
            if (notification.Method == InitializedMethod)
                StartGetStream();
        }
    }

    private async Task<HttpResponseMessage> PostAsync(
        string json, string method, JsonElement? parameters, bool allowReinitialize, CancellationToken ct)
    {
        var sentSession = SessionId;
        var response = await PostOnceAsync(json, method, parameters, ct).ConfigureAwait(false);

        string? initializeJson;
        lock (_state)
            initializeJson = _initializeJson;
        if (response.StatusCode == HttpStatusCode.NotFound && sentSession is not null && method != "initialize"
            && allowReinitialize && initializeJson is not null)
        {
            response.Dispose();
            await ReinitializeAsync(sentSession, ct).ConfigureAwait(false);
            response = await PostOnceAsync(json, method, parameters, ct).ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>
    /// One POST. A 401 asks the authenticator for a new credential; when it has one, the request is sent again,
    /// once: a second 401 is the answer. Requests refused together share the one renewal the authenticator makes.
    /// </summary>
    private async Task<HttpResponseMessage> PostOnceAsync(
        string json, string method, JsonElement? parameters, CancellationToken ct, PendingSession? pending = null)
    {
        var retried = false;
        while (true)
        {
            string? credential;
            HttpResponseMessage response;
            using (var message = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, HttpDefaults.JsonContentType)
            })
            {
                message.Headers.TryAddWithoutValidation("Accept", $"{HttpDefaults.JsonContentType}, {SseMediaType}");
                AddMcpHeaders(message, method, parameters, pending);
                if (_authenticator is not null)
                    await _authenticator.ApplyAsync(message, ct).ConfigureAwait(false);
                credential = message.Headers.Authorization?.ToString();

                response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && !retried && _authenticator is not null
                && await _authenticator.HandleUnauthorizedAsync(credential, ct).ConfigureAwait(false))
            {
                response.Dispose();
                retried = true;
                continue;
            }

            // A session being started again is adopted once its handshake is over, not on the first answer.
            if (pending is null)
                CaptureSession(response);
            return response;
        }
    }

    /// <summary>
    /// The session a re-initialisation is starting: its id and revision go on the requests of its handshake, and become
    /// the transport's only once <c>notifications/initialized</c> was taken.
    /// </summary>
    private sealed record PendingSession(string? SessionId, string? ProtocolVersion);

    private void CaptureSession(HttpResponseMessage response)
    {
        if (SessionOf(response) is { } id)
        {
            lock (_state)
                _sessionId = id;
        }
    }

    private static string? SessionOf(HttpResponseMessage response) =>
        response.IsSuccessStatusCode && response.Headers.TryGetValues(SessionHeader, out var values)
            && values.FirstOrDefault() is { Length: > 0 } id
            ? id
            : null;

    private void RememberNegotiatedVersion(JsonRpcResponse response)
    {
        if (NegotiatedVersion(response) is { } version)
        {
            lock (_state)
                _protocolVersion = version;
        }
    }

    private static string? NegotiatedVersion(JsonRpcResponse response) =>
        response.Result is { ValueKind: JsonValueKind.Object } result
            && result.TryGetProperty("protocolVersion", out var version) && version.ValueKind == JsonValueKind.String
            ? version.GetString()
            : null;

    /// <summary>The largest JSON body, in bytes, the transport reads: a server cannot make the client buffer without end.</summary>
    internal const int MaxBodyLength = SseEventReader.MaxLineLength;

    /// <summary>Reads a JSON answer, refused with an <see cref="InvalidDataException"/> past <paramref name="max"/> bytes.</summary>
    internal static async Task<string> ReadBodyAsync(HttpContent content, CancellationToken ct, int max = MaxBodyLength)
    {
        if (content.Headers.ContentLength > max)
            throw new InvalidDataException($"The server's answer is longer than {max} bytes: it is refused.");
        var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > max)
                    throw new InvalidDataException($"The server's answer is longer than {max} bytes: it is refused.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
    }

    /// <summary>
    /// The server forgot the session (404): starts a new one by replaying the initialize handshake, once per
    /// stale id however many requests found it stale.
    /// </summary>
    private async Task ReinitializeAsync(string staleSession, CancellationToken ct)
    {
        await _reinitialize.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string? initializeJson;
            lock (_state)
            {
                if (_sessionId != staleSession)
                    return;
                // The stale id stays until the new one is captured: a re-initialisation that fails leaves the
                // client able to meet the same 404 again, and to start over.
                initializeJson = _initializeJson;
            }

            LogSessionExpired(_url);

            // The new session is held aside until its handshake is over: an answer that cannot be read, a JSON-RPC
            // error or a refused notifications/initialized leaves the stale id in place, so the next request meets
            // the 404 again and starts over, rather than use a session that was never told initialized.
            var initializeRequest = JsonSerializer.Deserialize<JsonRpcRequest>(initializeJson!)!;
            var starting = new PendingSession(null, null);
            JsonRpcResponse? answer;
            using (var initialize = await PostOnceAsync(initializeJson!, "initialize", null, ct, starting).ConfigureAwait(false))
            {
                initialize.EnsureSuccessStatusCode();
                starting = starting with { SessionId = SessionOf(initialize) };
                answer = string.Equals(initialize.Content.Headers.ContentType?.MediaType, SseMediaType, StringComparison.OrdinalIgnoreCase)
                    ? await ReadStreamedAnswerAsync(initialize, initializeRequest, ct).ConfigureAwait(false)
                    : JsonSerializer.Deserialize<JsonRpcResponse>(await ReadBodyAsync(initialize.Content, ct).ConfigureAwait(false));
            }

            if (answer is null)
                throw new InvalidOperationException("The server answered the replayed initialize with no message: the session was not started again.");
            if (answer.Error is { } error)
                throw new InvalidOperationException($"The server refused the replayed initialize ({error.Code}: {error.Message}): the session was not started again.");
            starting = starting with { ProtocolVersion = NegotiatedVersion(answer) };

            var initialized = JsonSerializer.Serialize(new JsonRpcNotification { Method = InitializedMethod });
            using (var done = await PostOnceAsync(initialized, InitializedMethod, null, ct, starting).ConfigureAwait(false))
                done.EnsureSuccessStatusCode();

            lock (_state)
            {
                // A server that no longer hands out a session id answers the stale one's end: the client is sessionless.
                _sessionId = starting.SessionId;
                if (starting.ProtocolVersion is not null)
                    _protocolVersion = starting.ProtocolVersion;
                _lastEventId = null;
            }

            Resumed?.Invoke();
        }
        finally
        {
            _reinitialize.Release();
        }
    }

    /// <summary>
    /// Adds the Streamable HTTP request headers, derived from the JSON-RPC message itself:
    /// MCP-Protocol-Version (from `params._meta`, or the revision a legacy session negotiated), Mcp-Method,
    /// and Mcp-Name (from `params.name`, when present); and the session id once there is one.
    /// </summary>
    private void AddMcpHeaders(HttpRequestMessage message, string method, JsonElement? parameters, PendingSession? pending = null)
    {
        if (method.Length > 0)
            message.Headers.TryAddWithoutValidation(McpProtocol.MethodHeader, method);

        var version = McpProtocol.TryReadRequestedVersion(parameters);
        string? session;
        lock (_state)
        {
            session = pending is null ? _sessionId : pending.SessionId;
            version ??= method == "initialize" ? null : pending?.ProtocolVersion ?? _protocolVersion;
        }

        if (version != null)
            message.Headers.TryAddWithoutValidation(McpProtocol.ProtocolVersionHeader, version);
        // An initialize starts a session: it never carries the id of one that ended.
        if (session != null && method != "initialize")
            message.Headers.TryAddWithoutValidation(SessionHeader, session);

        // The entity the request names: a tool or prompt by name, a resource by uri (the reference server refuses a
        // resources/read without it, -32020).
        if (parameters is { ValueKind: JsonValueKind.Object } p)
        {
            var property = method == "resources/read" ? "uri" : "name";
            if (p.TryGetProperty(property, out var entity) && entity.ValueKind == JsonValueKind.String)
                message.Headers.TryAddWithoutValidation(McpProtocol.NameHeader, HeaderSafe(entity.GetString()!));
        }
    }

    /// <summary>
    /// A name or uri as a header value: printable ASCII as it is, anything else (a line break, a control character,
    /// a non-ASCII letter) percent-encoded, so that no value a server or a model chose can split the header or fail the send.
    /// </summary>
    internal static string HeaderSafe(string value) =>
        value.All(c => c is >= ' ' and <= '~') ? value : Uri.EscapeDataString(value);

    /// <summary>
    /// Reads an SSE-framed answer event by event: notifications go to <see cref="Notifications"/> as they
    /// arrive, a request of the server is refused, and the message answering <paramref name="request"/> ends
    /// the read. A stream that ends without it fails; for <c>subscriptions/listen</c>, whose stream is meant
    /// to stay open, the caller treats that end as the end of the subscription.
    /// </summary>
    private async Task<JsonRpcResponse?> ReadStreamedAnswerAsync(HttpResponseMessage httpResponse, JsonRpcRequest request, CancellationToken ct)
    {
        var sawNotification = false;
        var stream = await httpResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await foreach (var sse in SseEventReader.ReadAsync(stream, ct).ConfigureAwait(false))
            {
                // The priming event (an id, no data) carries nothing to dispatch.
                if (sse.Data.Length == 0)
                    continue;

                var frame = ReadFrame(sse.Data);
                switch (frame.Kind)
                {
                    case FrameKind.Notification:
                        _notifications.Writer.TryWrite(frame.Notification!);
                        sawNotification = true;
                        break;
                    case FrameKind.ServerRequest:
                        await AnswerServerRequestAsync(frame.RequestId!.Value, frame.Method, ct).ConfigureAwait(false);
                        break;
                    default:
                        var response = JsonSerializer.Deserialize<JsonRpcResponse>(sse.Data);
                        if (response is not null && AnswersRequest(response, request))
                            return response;
                        break;
                }
            }
        }

        throw new McpStreamEndedException(sawNotification
            ? $"The server answered {request.Method} with notifications only: its stream ended without the response to the request."
            : $"The server's stream for {request.Method} ended without the response to the request.");
    }

    private static bool AnswersRequest(JsonRpcResponse response, JsonRpcRequest request) =>
        response.Id is null || request.Id is null || response.Id.Value.GetRawText() == request.Id.Value.GetRawText();

    private enum FrameKind { Response, Notification, ServerRequest }

    private readonly record struct Frame(FrameKind Kind, JsonRpcNotification? Notification, JsonElement? RequestId, string? Method = null);

    private static Frame ReadFrame(string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String)
            {
                if (root.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null)
                    return new Frame(FrameKind.ServerRequest, null, id.Clone(), method.GetString());
                return new Frame(FrameKind.Notification, JsonSerializer.Deserialize<JsonRpcNotification>(data), null);
            }
        }
        catch (JsonException)
        {
            // Not a JSON-RPC frame: left to the response deserializer, which names the failure.
        }

        return new Frame(FrameKind.Response, null, null);
    }

    /// <summary>
    /// A <c>ping</c> of the server gets an empty result, as the specification requires. Sampling, roots and
    /// elicitation are not offered: a request of the server for one is answered -32601.
    /// </summary>
    private async Task AnswerServerRequestAsync(JsonElement id, string? method, CancellationToken ct)
    {
        var reply = method == "ping"
            ? JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result = new { } })
            : JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = JsonRpcErrorCodes.MethodNotFound, message = "Method not found" },
            });
        try
        {
            using var response = await PostOnceAsync(reply, "", null, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            LogRefusalFailed(ex, _url);
        }
    }

    // The GET stream.

    private void StartGetStream()
    {
        lock (_state)
        {
            if (_getStarted || _stop.IsCancellationRequested)
                return;
            _getStarted = true;
            _getLoop = Task.Run(() => GetLoopAsync(_stop.Token), CancellationToken.None);
        }
    }

    /// <summary>How one opening of the GET stream ended.</summary>
    private enum GetOutcome
    {
        /// <summary>The stream ended or failed: open it again after the backoff.</summary>
        Backoff,

        /// <summary>Open it again at once (a renewed credential).</summary>
        RetryNow,

        /// <summary>Give the stream up.</summary>
        Stop,
    }

    /// <summary>What the GET loop remembers from one opening to the next.</summary>
    private sealed class GetLoopState
    {
        public int Attempt { get; set; }

        public int Reinitializations { get; set; }

        public bool UnauthorizedRetried { get; set; }

        public bool Opened { get; set; }

        public TimeSpan? ServerRetry { get; set; }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Fault barrier of a background stream: any failure re-opens it after the backoff, and is logged.")]
    private async Task GetLoopAsync(CancellationToken ct)
    {
        var state = new GetLoopState();
        while (!ct.IsCancellationRequested)
        {
            state.ServerRetry = null;
            GetOutcome outcome;
            try
            {
                outcome = await OpenGetStreamAsync(state, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogGetStreamFailed(ex, _url);
                outcome = GetOutcome.Backoff;
            }

            if (outcome == GetOutcome.Stop)
                return;
            if (outcome == GetOutcome.RetryNow)
                continue;

            var delay = state.ServerRetry is { } hinted ? TimeSpan.FromTicks(Math.Min(hinted.Ticks, ReconnectMaxDelay.Ticks)) : Backoff(state.Attempt);
            state.Attempt++;
            try
            {
                await Task.Delay(delay, _time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // The token may have expired during the wait: the next 401 is renewed again.
            state.UnauthorizedRetried = false;
        }
    }

    private async Task<GetOutcome> OpenGetStreamAsync(GetLoopState state, CancellationToken ct)
    {
        var sentSession = SessionId;
        using var message = new HttpRequestMessage(HttpMethod.Get, _url);
        message.Headers.TryAddWithoutValidation("Accept", SseMediaType);
        AddMcpHeaders(message, "", null);
        string? lastEventId;
        lock (_state)
            lastEventId = _lastEventId;
        if (lastEventId is not null)
            message.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        if (_authenticator is not null)
            await _authenticator.ApplyAsync(message, ct).ConfigureAwait(false);
        var credential = message.Headers.Authorization?.ToString();

        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var early = await TriageGetResponseAsync(response, sentSession, credential, state, ct).ConfigureAwait(false);
        if (early is { } outcome)
            return outcome;

        response.EnsureSuccessStatusCode();
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
        {
            LogNoGetStream(_url);
            return GetOutcome.Stop;
        }

        state.Attempt = 0;
        state.Reinitializations = 0;
        if (state.Opened)
            Resumed?.Invoke();
        state.Opened = true;
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await PumpGetStreamAsync(stream, state, ct).ConfigureAwait(false);
        }

        return GetOutcome.Backoff;
    }

    /// <summary>Decides what a GET response that is not a stream means; null when it may be one.</summary>
    private async Task<GetOutcome?> TriageGetResponseAsync(
        HttpResponseMessage response, string? sentSession, string? credential, GetLoopState state, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized && _authenticator is not null)
        {
            // A refused credential is renewed and the stream opens again at once, once in a row: a server
            // that refuses the new one too is backed off like any other failure.
            if (!state.UnauthorizedRetried && await _authenticator.HandleUnauthorizedAsync(credential, ct).ConfigureAwait(false))
            {
                state.UnauthorizedRetried = true;
                return GetOutcome.RetryNow;
            }
        }
        else
        {
            state.UnauthorizedRetried = false;
        }

        if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
        {
            LogNoGetStream(_url);
            return GetOutcome.Stop;
        }

        if (response.StatusCode == HttpStatusCode.NotFound && sentSession is not null)
        {
            // A server that takes initialize and then refuses the stream again would otherwise make this
            // a hot loop: the session is initialised again a bounded number of times, each time after the
            // backoff, and the stream is given up when that does not help.
            if (++state.Reinitializations > MaxConsecutiveReinitializations)
            {
                LogGetStreamGivenUp(_url, MaxConsecutiveReinitializations);
                return GetOutcome.Stop;
            }

            await ReinitializeAsync(sentSession, ct).ConfigureAwait(false);
            throw new HttpRequestException("The server forgot the session of the GET stream: it was initialised again.", null, HttpStatusCode.NotFound);
        }

        return null;
    }

    private async Task PumpGetStreamAsync(Stream stream, GetLoopState state, CancellationToken ct)
    {
        await foreach (var sse in SseEventReader.ReadAsync(stream, ct).ConfigureAwait(false))
        {
            // An empty id: field resets the last event id, as the SSE specification says.
            if (sse.Id is not null)
            {
                lock (_state)
                    _lastEventId = sse.Id.Length == 0 ? null : sse.Id;
            }

            if (sse.RetryMilliseconds is { } retry)
                state.ServerRetry = TimeSpan.FromMilliseconds(retry);

            if (sse.Data.Length == 0)
                continue;

            var frame = ReadFrame(sse.Data);
            if (frame.Kind == FrameKind.Notification)
                _notifications.Writer.TryWrite(frame.Notification!);
            else if (frame.Kind == FrameKind.ServerRequest)
                await AnswerServerRequestAsync(frame.RequestId!.Value, frame.Method, ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP server '{Url}' sent more notifications than the client reads: the oldest ('{Method}') was dropped")]
    private partial void LogNotificationDropped(string method, Uri url);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP server '{Url}' forgot the session: initializing a new one")]
    private partial void LogSessionExpired(Uri url);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP server '{Url}' offers no GET stream: its own messages are heard on answers only")]
    private partial void LogNoGetStream(Uri url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP server '{Url}' answered the GET stream 404 after {Count} new sessions in a row: the stream is given up")]
    private partial void LogGetStreamGivenUp(Uri url, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The GET stream of MCP server '{Url}' failed: it is opened again after a delay")]
    private partial void LogGetStreamFailed(Exception ex, Uri url);

    [LoggerMessage(Level = LogLevel.Debug, Message = "MCP server '{Url}' was not told the session is over")]
    private partial void LogEndSessionFailed(Exception ex, Uri url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP server '{Url}' did not take the refusal of its request")]
    private partial void LogRefusalFailed(Exception ex, Uri url);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_isConnected && _stop.IsCancellationRequested)
            return;
        _isConnected = false;
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _getLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The stream was cancelled: the stop we asked for.
        }

        await EndSessionAsync().ConfigureAwait(false);
        _notifications.Writer.TryComplete();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        (_authenticator as IDisposable)?.Dispose();
        _stop.Dispose();
        _reinitialize.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Tells the server the session is over (DELETE with its id), once and without insisting.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "Fault barrier of the disposal: whatever goes wrong, the session ends with the connection and the other servers are still disposed.")]
    private async Task EndSessionAsync()
    {
        var session = SessionId;
        if (session is null)
            return;

        try
        {
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var message = new HttpRequestMessage(HttpMethod.Delete, _url);
            message.Headers.TryAddWithoutValidation(SessionHeader, session);
            // The credential in cache, under the guard, and never a sign-in: closing must not ask a person for a code.
            if (_authenticator is IMcpSignInAuthenticator signIn)
                await signIn.ApplyWithoutSignInAsync(message, guard.Token).ConfigureAwait(false);
            else if (_authenticator is not null)
                await _authenticator.ApplyAsync(message, guard.Token).ConfigureAwait(false);
            using var response = await _httpClient.SendAsync(message, guard.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The server is gone, does not take DELETE, or the credential cannot be had: the session ends with the
            // connection, and the disposal goes on (the other servers are disposed after this one).
            LogEndSessionFailed(ex, _url);
        }
    }
}

/// <summary>A server's answer stream ended before the message that answers the request.</summary>
internal sealed class McpStreamEndedException : InvalidOperationException
{
    public McpStreamEndedException() { }

    public McpStreamEndedException(string message) : base(message) { }

    public McpStreamEndedException(string message, Exception innerException) : base(message, innerException) { }
}
