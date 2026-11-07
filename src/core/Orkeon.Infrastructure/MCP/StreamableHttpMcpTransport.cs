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
    /// <param name="authenticator">Optional authenticator applied to every request.</param>
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
            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            });
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
                var body = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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

    private async Task<HttpResponseMessage> PostOnceAsync(string json, string method, JsonElement? parameters, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, HttpDefaults.JsonContentType)
        };
        message.Headers.TryAddWithoutValidation("Accept", $"{HttpDefaults.JsonContentType}, {SseMediaType}");
        AddMcpHeaders(message, method, parameters);
        if (_authenticator is not null)
            await _authenticator.ApplyAsync(message, ct).ConfigureAwait(false);

        var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        CaptureSession(response);
        return response;
    }

    private void CaptureSession(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode && response.Headers.TryGetValues(SessionHeader, out var values))
        {
            var id = values.FirstOrDefault();
            if (!string.IsNullOrEmpty(id))
            {
                lock (_state)
                    _sessionId = id;
            }
        }
    }

    private void RememberNegotiatedVersion(JsonRpcResponse response)
    {
        if (response.Result is { ValueKind: JsonValueKind.Object } result
            && result.TryGetProperty("protocolVersion", out var version) && version.ValueKind == JsonValueKind.String)
        {
            lock (_state)
                _protocolVersion = version.GetString();
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
                _sessionId = null;
                _protocolVersion = null;
                initializeJson = _initializeJson;
            }

            LogSessionExpired(_url);
            using var initialize = await PostOnceAsync(initializeJson!, "initialize", null, ct).ConfigureAwait(false);
            initialize.EnsureSuccessStatusCode();
            var body = await initialize.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.Equals(initialize.Content.Headers.ContentType?.MediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
                body = LastData(body);
            if (JsonSerializer.Deserialize<JsonRpcResponse>(body) is { Error: null } answer)
                RememberNegotiatedVersion(answer);

            var initialized = JsonSerializer.Serialize(new JsonRpcNotification { Method = InitializedMethod });
            using var done = await PostOnceAsync(initialized, InitializedMethod, null, ct).ConfigureAwait(false);
            done.EnsureSuccessStatusCode();
            Resumed?.Invoke();
        }
        finally
        {
            _reinitialize.Release();
        }
    }

    private static string LastData(string sseBody)
    {
        var last = sseBody;
        foreach (var rawLine in sseBody.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("data:", StringComparison.Ordinal))
                last = line["data:".Length..].TrimStart();
        }

        return last;
    }

    /// <summary>
    /// Adds the Streamable HTTP request headers, derived from the JSON-RPC message itself:
    /// MCP-Protocol-Version (from `params._meta`, or the revision a legacy session negotiated), Mcp-Method,
    /// and Mcp-Name (from `params.name`, when present); and the session id once there is one.
    /// </summary>
    private void AddMcpHeaders(HttpRequestMessage message, string method, JsonElement? parameters)
    {
        if (method.Length > 0)
            message.Headers.TryAddWithoutValidation(McpProtocol.MethodHeader, method);

        var version = McpProtocol.TryReadRequestedVersion(parameters);
        string? session;
        lock (_state)
        {
            session = _sessionId;
            version ??= method == "initialize" ? null : _protocolVersion;
        }

        if (version != null)
            message.Headers.TryAddWithoutValidation(McpProtocol.ProtocolVersionHeader, version);
        if (session != null)
            message.Headers.TryAddWithoutValidation(SessionHeader, session);

        // The entity the request names: a tool or prompt by name, a resource by uri (the reference server refuses a
        // resources/read without it, -32020).
        if (parameters is { ValueKind: JsonValueKind.Object } p)
        {
            var property = method == "resources/read" ? "uri" : "name";
            if (p.TryGetProperty(property, out var entity) && entity.ValueKind == JsonValueKind.String)
                message.Headers.TryAddWithoutValidation(McpProtocol.NameHeader, entity.GetString());
        }
    }

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

    [SuppressMessage("Design", "CA1031", Justification = "Fault barrier of a background stream: any failure re-opens it after the backoff, and is logged.")]
    private async Task GetLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        var reinitializations = 0;
        var opened = false;
        while (!ct.IsCancellationRequested)
        {
            TimeSpan? serverRetry = null;
            try
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

                using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
                {
                    LogNoGetStream(_url);
                    return;
                }

                if (response.StatusCode == HttpStatusCode.NotFound && sentSession is not null)
                {
                    // A server that takes initialize and then refuses the stream again would otherwise make this
                    // a hot loop: the session is initialised again a bounded number of times, each time after the
                    // backoff, and the stream is given up when that does not help.
                    if (++reinitializations > MaxConsecutiveReinitializations)
                    {
                        LogGetStreamGivenUp(_url, MaxConsecutiveReinitializations);
                        return;
                    }

                    await ReinitializeAsync(sentSession, ct).ConfigureAwait(false);
                    throw new HttpRequestException("The server forgot the session of the GET stream: it was initialised again.", null, HttpStatusCode.NotFound);
                }

                response.EnsureSuccessStatusCode();
                if (!string.Equals(response.Content.Headers.ContentType?.MediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
                {
                    LogNoGetStream(_url);
                    return;
                }

                attempt = 0;
                reinitializations = 0;
                if (opened)
                    Resumed?.Invoke();
                opened = true;
                var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    await foreach (var sse in SseEventReader.ReadAsync(stream, ct).ConfigureAwait(false))
                    {
                        if (sse.Id is not null)
                        {
                            lock (_state)
                                _lastEventId = sse.Id;
                        }

                        if (sse.RetryMilliseconds is { } retry)
                            serverRetry = TimeSpan.FromMilliseconds(retry);

                        var frame = ReadFrame(sse.Data);
                        if (frame.Kind == FrameKind.Notification)
                            _notifications.Writer.TryWrite(frame.Notification!);
                        else if (frame.Kind == FrameKind.ServerRequest)
                            await AnswerServerRequestAsync(frame.RequestId!.Value, frame.Method, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogGetStreamFailed(ex, _url);
            }

            var delay = serverRetry is { } hinted ? TimeSpan.FromTicks(Math.Min(hinted.Ticks, ReconnectMaxDelay.Ticks)) : Backoff(attempt);
            attempt++;
            try
            {
                await Task.Delay(delay, _time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
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

        _stop.Dispose();
        _reinitialize.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Tells the server the session is over (DELETE with its id), once and without insisting.</summary>
    private async Task EndSessionAsync()
    {
        var session = SessionId;
        if (session is null)
            return;

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Delete, _url);
            message.Headers.TryAddWithoutValidation(SessionHeader, session);
            if (_authenticator is not null)
                await _authenticator.ApplyAsync(message, CancellationToken.None).ConfigureAwait(false);
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await _httpClient.SendAsync(message, guard.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            // The server is gone or does not take DELETE: the session ends with the connection.
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
