using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>Why a request to the tool bridge failed.</summary>
internal enum ToolBridgeFailure
{
    /// <summary>The bridge could not be reached, or the connection broke.</summary>
    Unreachable,

    /// <summary>The bridge refused the token.</summary>
    TokenRefused,

    /// <summary>The bridge answered with a refusal of another kind, or an answer the caller cannot read.</summary>
    Refused,

    /// <summary>
    /// The bridge no longer holds this caller's session: it expired at a missed heartbeat window, or the bridge
    /// restarted. The runs the session held there are ended; a run in flight fails, as for a lost link.
    /// </summary>
    SessionLost,
}

/// <summary>A request to the tool bridge that failed, with its reason in the operator's words.</summary>
[SuppressMessage("Design", "CA1032", Justification = "Internal to the bridge: it is always built with its kind and its reason.")]
[SuppressMessage("Design", "CA1064", Justification = "Internal to the bridge: the proxy and the start-up step turn it into a response or a RunnerSettingsException before it leaves the assembly.")]
internal sealed class ToolBridgeException : Exception
{
    /// <summary>Builds the failure.</summary>
    /// <param name="failure">Its kind.</param>
    /// <param name="message">Its reason.</param>
    /// <param name="inner">The exception behind it, when one.</param>
    public ToolBridgeException(ToolBridgeFailure failure, string message, Exception? inner = null)
        : base(message, inner) => Failure = failure;

    /// <summary>The kind of the failure.</summary>
    public ToolBridgeFailure Failure { get; }
}

/// <summary>
/// The caller's side of the wire (#445): one <see cref="HttpClient"/> to the bridge, the token on every request.
/// The connection is bounded by <see cref="ToolBridgeClientOptions.ConnectTimeout"/>; a call is not, since a tool
/// may run for minutes, and ends with the caller's cancellation.
/// <para>
/// The client opens a session on the bridge before its first call (<c>POST session</c>) and names it on every later
/// request (<see cref="ToolBridgeProtocol.SessionHeader"/>); while the session is open it sends a heartbeat three
/// times per <see cref="HeartbeatWindow"/>, so the bridge keeps it and the runs it holds (<see cref="BeginRun"/>,
/// <see cref="EndRunAsync"/>). When the bridge answers that it lost the session, the request fails with
/// <see cref="ToolBridgeFailure.SessionLost"/>, and so does every later one while a run that was live then is not
/// ended: its state on the bridge (a browser) is gone, and nothing falls back to a local tool. Once those runs ended,
/// the next request opens a new session.
/// </para>
/// </summary>
internal sealed class ToolBridgeClient : IDisposable
{
    // How long the end of a run may take: it is sent when the caller's run ends, which must not wait on a lost bridge.
    private static readonly TimeSpan EndBound = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly long _maxResponseBytes;
    private readonly Lock _sync = new();
    private readonly HashSet<string> _liveRuns = new(StringComparer.Ordinal);
    private readonly ITimer _heartbeat;
    private readonly SemaphoreSlim _opening = new(1, 1);
    private TimeSpan _window = TimeSpan.FromSeconds(30);
    private bool _beating;
    private string? _session;
    private bool _lost;

    /// <summary>Builds a client of the bridge at <paramref name="address"/>.</summary>
    /// <param name="address">The bridge's base address; its path is ignored, the protocol's path is used.</param>
    /// <param name="token">The token, read from the environment by the caller.</param>
    /// <param name="connectTimeout">How long a connection may take.</param>
    /// <param name="timeProvider">The clock of the heartbeat; the system's when null.</param>
    /// <param name="hostHeader">The <c>Host</c> header of every request; the address's own when null.</param>
    /// <param name="maxResponseBytes">The largest answer read; a larger one fails the request.</param>
    [SuppressMessage("Reliability", "CA2000", Justification = "The handler is owned by the HttpClient (disposeHandler: true), which Dispose disposes.")]
    public ToolBridgeClient(Uri address, string token, TimeSpan connectTimeout, TimeProvider? timeProvider = null, string? hostHeader = null,
        long maxResponseBytes = ToolBridgeClientOptions.DefaultMaxResponseBytes)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResponseBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResponseBytes, int.MaxValue);
        Address = address;
        _maxResponseBytes = maxResponseBytes;
        _http = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = connectTimeout,
            UseProxy = false,
            // A redirect would carry the call's arguments and files to an address the settings never named.
            AllowAutoRedirect = false,
        }, disposeHandler: true)
        {
            BaseAddress = new Uri(address, ToolBridgeProtocol.BasePath),
            Timeout = Timeout.InfiniteTimeSpan,
            // The answers this client does not read through ReadBoundedAsync (end of a run, heartbeat, cancellation)
            // are buffered under the same bound.
            MaxResponseContentBufferSize = maxResponseBytes,
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(ToolBridgeProtocol.BearerScheme, token);
        if (!string.IsNullOrWhiteSpace(hostHeader))
            _http.DefaultRequestHeaders.Host = hostHeader;
        _heartbeat = (timeProvider ?? TimeProvider.System).CreateTimer(_ => _ = BeatAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The bridge's address, as the settings give it.</summary>
    public Uri Address { get; }

    /// <summary>The id of the session the bridge issued to this client; null before the first call, or once lost.</summary>
    public string? SessionId
    {
        get
        {
            lock (_sync)
                return _session;
        }
    }

    /// <summary>
    /// The window the bridge gives (<c>GET tools</c>, <c>Orkeon:Bridge:HeartbeatWindow</c>); the client beats three
    /// times in it.
    /// </summary>
    public TimeSpan HeartbeatWindow
    {
        get
        {
            lock (_sync)
                return _window;
        }

        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The heartbeat window must be positive.");
            lock (_sync)
                _window = value;
        }
    }

    /// <summary>The caller's runs that have a scope on the bridge.</summary>
    public int LiveRuns
    {
        get
        {
            lock (_sync)
                return _liveRuns.Count;
        }
    }

    /// <summary>The heartbeats sent, answered or not.</summary>
    public int HeartbeatsSent => Volatile.Read(ref _heartbeatsSent);

    private int _heartbeatsSent;

    /// <summary>Records that the caller's run <paramref name="runId"/> has a scope on the bridge, in this client's session.</summary>
    /// <param name="runId">The run.</param>
    public void BeginRun(string runId)
    {
        lock (_sync)
            _liveRuns.Add(runId);
    }

    /// <summary>
    /// Ends the caller's run <paramref name="runId"/> on the bridge, best effort and bounded: a lost bridge ends it on
    /// its own once the heartbeat stops. Once the last run live at the loss of a session ended, the next request opens
    /// a new session.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <returns>A task that completes once the bridge answered, or failed to.</returns>
    [SuppressMessage("Design", "CA1031",
        Justification = "The end of a run is best effort: a bridge that does not answer ends the run when the heartbeat stops.")]
    public async System.Threading.Tasks.Task EndRunAsync(string runId)
    {
        string? session;
        lock (_sync)
        {
            _liveRuns.Remove(runId);
            if (_lost && _liveRuns.Count == 0)
                _lost = false;
            session = _lost ? null : _session;
        }

        if (session is null)
            return;
        try
        {
            using var bound = new CancellationTokenSource(EndBound);
            using var request = Post(ToolBridgeProtocol.EndRunEndpoint, new BridgeRunRequest { RunId = runId }, session);
            using var response = await _http.SendAsync(request, bound.Token).ConfigureAwait(false);
            if (response.StatusCode == ToolBridgeProtocol.SessionLostStatus)
                OnSessionLost(session);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    /// <summary>The tools the bridge serves.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Its answer.</returns>
    public System.Threading.Tasks.Task<BridgeToolListResponse> ListToolsAsync(CancellationToken cancellationToken) =>
        SendAsync<BridgeToolListResponse>(new HttpRequestMessage(HttpMethod.Get, ToolBridgeProtocol.ToolsEndpoint), cancellationToken);

    /// <summary>Runs one call on the bridge.</summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">Aborts the request; the caller then cancels the call with <see cref="CancelAsync"/>.</param>
    /// <returns>The tool's response.</returns>
    public System.Threading.Tasks.Task<BridgeCallResponse> CallAsync(BridgeCallRequest call, CancellationToken cancellationToken) =>
        SendAsync<BridgeCallResponse>(Post(ToolBridgeProtocol.CallEndpoint, call), cancellationToken);

    /// <summary>Asks the bridge to cancel a call in flight; a failure is ignored, the call's own request is gone already.</summary>
    /// <param name="callId">The id the call was sent with.</param>
    /// <param name="cancellationToken">Bounds the request.</param>
    /// <returns>A task that completes once the bridge answered, or failed to.</returns>
    [SuppressMessage("Design", "CA1031",
        Justification = "A cancellation is best effort: whatever fails, the call it names is abandoned by the caller already.")]
    public async System.Threading.Tasks.Task CancelAsync(string callId, CancellationToken cancellationToken)
    {
        if (SessionId is not { } session)
            return;
        try
        {
            using var request = Post(ToolBridgeProtocol.CancelEndpoint, new BridgeCancelRequest { CallId = callId }, session);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _heartbeat.Dispose();
        _http.Dispose();
        _opening.Dispose();
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "A heartbeat is best effort: a missed one is what the bridge's window is for.")]
    private async System.Threading.Tasks.Task BeatAsync()
    {
        TimeSpan window;
        string? session;
        lock (_sync)
        {
            if (!_beating || _session is null)
                return;
            window = _window;
            session = _session;
        }

        Interlocked.Increment(ref _heartbeatsSent);
        try
        {
            using var bound = new CancellationTokenSource(window);
            using var request = Post(ToolBridgeProtocol.HeartbeatEndpoint, new BridgeEmptyRequest(), session);
            using var response = await _http.SendAsync(request, bound.Token).ConfigureAwait(false);
            if (response.StatusCode == ToolBridgeProtocol.SessionLostStatus)
                OnSessionLost(session);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private static HttpRequestMessage Post<T>(string endpoint, T body, string? session = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body, options: ToolBridgeProtocol.JsonOptions),
        };
        if (session is not null)
            request.Headers.Add(ToolBridgeProtocol.SessionHeader, session);
        return request;
    }

    /// <summary>
    /// The session of this client, opened at the first need; refused with <see cref="ToolBridgeFailure.SessionLost"/>
    /// while a run live at the loss of the previous one is not ended.
    /// </summary>
    [SuppressMessage("Reliability", "CA2000", Justification = "SendRawAsync owns the request and disposes it.")]
    private async System.Threading.Tasks.Task<string> SessionAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_lost)
                throw Lost("the runs it held there are ended; this run cannot go on without them");
            if (_session is { } open)
                return open;
        }

        await _opening.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_session is { } open)
                    return open;
            }

            var opened = await SendRawAsync<BridgeSessionResponse>(Post(ToolBridgeProtocol.SessionEndpoint, new BridgeEmptyRequest()), cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(opened.SessionId))
                throw new ToolBridgeException(ToolBridgeFailure.Refused, $"the tool bridge at {Address} opened a session without an id.");
            lock (_sync)
            {
                _session = opened.SessionId;
                if (opened.HeartbeatWindowMs is > 0 and var ms)
                    _window = TimeSpan.FromMilliseconds(ms);
                _beating = true;
                var period = _window / 3;
                _heartbeat.Change(period, period);
                return _session;
            }
        }
        finally
        {
            _opening.Release();
        }
    }

    // The bridge no longer holds the session: the runs live then are lost with it, a later run gets a new session.
    private void OnSessionLost(string session)
    {
        lock (_sync)
        {
            if (!string.Equals(_session, session, StringComparison.Ordinal))
                return;
            _session = null;
            _lost = _liveRuns.Count > 0;
            _beating = false;
            _heartbeat.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private ToolBridgeException Lost(string detail) => new(ToolBridgeFailure.SessionLost,
        $"the tool bridge at {Address} lost this caller's session (a missed heartbeat window, or a restart of the bridge): {detail}.");

    private async System.Threading.Tasks.Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post)
            return await SendRawAsync<T>(request, cancellationToken).ConfigureAwait(false);
        string session;
        try
        {
            session = await SessionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            request.Dispose();
            throw;
        }

        request.Headers.Add(ToolBridgeProtocol.SessionHeader, session);
        try
        {
            return await SendRawAsync<T>(request, cancellationToken).ConfigureAwait(false);
        }
        catch (ToolBridgeException ex) when (ex.Failure == ToolBridgeFailure.SessionLost)
        {
            OnSessionLost(session);
            throw;
        }
    }

    private async System.Threading.Tasks.Task<T> SendRawAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var owned = request;
        HttpResponseMessage response;
        try
        {
            // The headers first: the body is read under this client's bound, never buffered whole beforehand.
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ToolBridgeException(ToolBridgeFailure.Unreachable, $"the tool bridge at {Address} cannot be reached ({ex.Message}).", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ToolBridgeException(ToolBridgeFailure.Unreachable, $"the tool bridge at {Address} did not accept the connection in time.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new ToolBridgeException(ToolBridgeFailure.TokenRefused, $"the tool bridge at {Address} refused the token.");
            try
            {
                var body = await ReadBoundedAsync(response, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == ToolBridgeProtocol.SessionLostStatus && request.Headers.Contains(ToolBridgeProtocol.SessionHeader))
                    throw Lost("the runs it held there are ended, and a run in flight fails");
                if (!response.IsSuccessStatusCode)
                {
                    var refusal = JsonSerializer.Deserialize<BridgeErrorResponse>(body, ToolBridgeProtocol.JsonOptions);
                    throw new ToolBridgeException(ToolBridgeFailure.Refused,
                        $"the tool bridge at {Address} refused the request ({(int)response.StatusCode}): {refusal?.Error ?? "no reason given"}.");
                }

                return JsonSerializer.Deserialize<T>(body, ToolBridgeProtocol.JsonOptions)
                    ?? throw new ToolBridgeException(ToolBridgeFailure.Refused, $"the tool bridge at {Address} sent an empty answer.");
            }
            catch (JsonException ex)
            {
                throw new ToolBridgeException(ToolBridgeFailure.Refused, $"the tool bridge at {Address} sent an answer that is not its protocol.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new ToolBridgeException(ToolBridgeFailure.Unreachable, $"the connection to the tool bridge at {Address} broke ({ex.Message}).", ex);
            }
            catch (IOException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ToolBridgeException(ToolBridgeFailure.Unreachable, $"the connection to the tool bridge at {Address} broke ({ex.Message}).", ex);
            }
        }
    }

    /// <summary>The body of <paramref name="response"/>, refused once it exceeds the bound, whatever its length says.</summary>
    private async System.Threading.Tasks.Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > _maxResponseBytes)
            throw TooLarge();
        var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = input.ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > _maxResponseBytes)
                throw TooLarge();
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private ToolBridgeException TooLarge() => new(ToolBridgeFailure.Refused,
        $"the tool bridge at {Address} sent an answer larger than {ToolBridgeClientOptions.SectionName}:MaxResponseBytes ({ToolBridgeProtocol.Describe(_maxResponseBytes)}).");
}
