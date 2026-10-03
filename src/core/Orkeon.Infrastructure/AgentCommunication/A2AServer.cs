using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Infrastructure.Constants.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// HTTP-based A2A server that exposes local agents for remote task submission.
/// Uses <see cref="HttpListener"/> to serve:
/// <list type="bullet">
///   <item><c>GET /.well-known/agent.json</c> — Agent card discovery</item>
///   <item><c>POST /a2a/tasks/send</c> — Submit a task</item>
///   <item><c>POST /a2a/tasks/sendSubscribe</c> — Submit a task with SSE streaming</item>
///   <item><c>GET /a2a/tasks/{id}</c> — Get task status</item>
///   <item><c>DELETE /a2a/tasks/{id}</c> — Cancel a task</item>
/// </list>
/// <para>
/// Requests are served concurrently: a task the agent is still working on does not hold up
/// the listener, so <c>DELETE /a2a/tasks/{id}</c> reaches it and cancels the token its
/// execution runs under (GAP-10).
/// </para>
/// <para>
/// The card publishes the skills of its router (<see cref="IA2ATaskRouter.GetSkillsAsync"/>):
/// the key a peer reads is the key the router compares, whatever the router routes — agents
/// for the default one, crews for <c>orkeon-host</c>'s (GAP-23).
/// </para>
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class A2AServer : IA2AServer, IDisposable
{
    private readonly A2AOptions _options;
    private readonly A2ASecurityOptions _security;
    private readonly IA2ATaskRouter _taskRouter;
    private readonly IA2ATaskStore? _taskStore;
    private readonly A2ACredentialValidator _credentials;
    private readonly ILogger _logger;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    // Requests being served (the listen loop no longer awaits each one), and the tasks the
    // agent is still working on, by id — DELETE cancels the token of the matching entry.
    private readonly ConcurrentDictionary<int, Task> _requestsInFlight = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tasksInFlight = new(StringComparer.Ordinal);

    // mTLS client trust, materialized once per StartAsync (SEC-012).
    private HashSet<string>? _trustedClientThumbprints;
    private readonly List<X509Certificate2> _trustedClientCertificateAuthorities = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <summary>Initializes a new instance of <see cref="A2AServer"/>.</summary>
    /// <param name="options">A2A server options.</param>
    /// <param name="taskRouter">The task router handling submitted tasks, and listing the skills the card publishes.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="security">Optional A2A security options.</param>
    /// <param name="taskStore">Optional task persistence (lifts the 501 on GET /a2a/tasks/{id} — see AddOrkeonA2ATaskPersistence).</param>
    /// <param name="authenticationProviders">Validators a <c>Bearer</c> token is offered to.</param>
    /// <param name="secretProvider">Secret provider the <c>ApiKey</c> keys are read from.</param>
    public A2AServer(
        IOptions<A2AOptions> options,
        IA2ATaskRouter taskRouter,
        ILogger<A2AServer>? logger = null,
        IOptions<A2ASecurityOptions>? security = null,
        IA2ATaskStore? taskStore = null,
        IEnumerable<IAuthenticationProvider>? authenticationProviders = null,
        ISecretProvider? secretProvider = null)
        : this(options?.Value ?? new A2AOptions(), taskRouter, logger,
               security?.Value, taskStore, authenticationProviders, secretProvider)
    {
    }

    /// <summary>Initializes a new instance of <see cref="A2AServer"/> with direct options (useful for testing).</summary>
    public A2AServer(
        A2AOptions options,
        IA2ATaskRouter taskRouter,
        ILogger<A2AServer>? logger = null,
        A2ASecurityOptions? security = null,
        IA2ATaskStore? taskStore = null,
        IEnumerable<IAuthenticationProvider>? authenticationProviders = null,
        ISecretProvider? secretProvider = null)
    {
        _options = options ?? new A2AOptions();
        _security = security ?? new A2ASecurityOptions();
        ArgumentNullException.ThrowIfNull(taskRouter);
        _taskRouter = taskRouter;
        _taskStore = taskStore;
        _credentials = new A2ACredentialValidator(_security, authenticationProviders ?? [], secretProvider);
        _logger = logger ?? NullLogger<A2AServer>.Instance;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("A2A server is already running.");

        // Fail-closed: a declared scheme without a validator would accept any credential
        // of the right shape (GAP-09) — refuse to start instead.
        await _credentials.EnsureReadyAsync(ct).ConfigureAwait(false);

        if (_security.RequireMutualTls)
            InitializeMutualTlsTrust();

        var prefix = $"{_options.Host.TrimEnd('/')}:{_options.Port}/";

        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listenTask = ListenLoopAsync(_cts.Token);

        IsRunning = true;
        LogA2AServerStarted(prefix);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning) return;

        IsRunning = false;

        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();

        if (_listenTask != null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown: listener was cancelled
            }
            catch (HttpListenerException)
            {
                // Expected during shutdown: listener was stopped
            }
        }

        // The requests still being served saw the shutdown token; each one is its own fault
        // barrier (ProcessContextAsync), so waiting for them cannot throw.
        await Task.WhenAll(_requestsInFlight.Values).ConfigureAwait(false);

        _listener?.Close();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        DisposeTrustedClientCas();

        LogA2AServerStopped();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Synchronous fallback so synchronously-disposed DI containers and <c>using</c>
    /// blocks can tear the server down; prefer <see cref="DisposeAsync"/> (<c>await using</c>).
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Core synchronous teardown; mirrors <see cref="DisposeAsync"/>.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The only awaited work in StopAsync is the listen loop, which exits
            // promptly once the listener is stopped — safe to block here.
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Accept-loop fault barrier: an unexpected error while accepting/processing one request is logged and the loop continues so a single bad request cannot tear down the A2A listener.")]
    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
                TrackRequest(Task.Run(() => ProcessContextAsync(context, ct), CancellationToken.None));
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (HttpRequestException httpEx)
            {
                LogHttpRequestError(httpEx, httpEx.StatusCode?.ToString() ?? "N/A");
            }
            catch (TimeoutException timeoutEx)
            {
                LogTimeoutError(timeoutEx);
            }
            catch (Exception ex)
            {
                LogErrorAcceptingA2ARequest(ex);
            }
        }
    }

    private void TrackRequest(Task request)
    {
        _requestsInFlight[request.Id] = request;
        _ = request.ContinueWith(
            static (done, state) => ((ConcurrentDictionary<int, Task>)state!).TryRemove(done.Id, out _),
            _requestsInFlight,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    [SuppressMessage("Design", "CA1031", Justification = "Per-request fault barrier: any handler failure is logged and converted into a 500 response so one request cannot crash the listen loop.")]
    private async Task ProcessContextAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            await HandleRequestAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException ocEx)
        {
            LogRequestCancelled(ocEx);
        }
        catch (Exception ex)
        {
            LogErrorHandlingA2ARequest(ex);
            await TryWriteErrorResponseAsync(context, 500, "Internal server error").ConfigureAwait(false);
        }
    }

    internal async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            await RouteRequestAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown — propagate so the listen loop can break
            throw;
        }
        catch (HttpRequestException httpEx)
        {
            LogHttpRequestError(httpEx, httpEx.StatusCode?.ToString() ?? "N/A");
            await TryWriteErrorResponseAsync(context, 502, "Bad gateway").ConfigureAwait(false);
        }
        catch (TimeoutException timeoutEx)
        {
            LogTimeoutError(timeoutEx);
            await TryWriteErrorResponseAsync(context, 504, "Gateway timeout").ConfigureAwait(false);
        }
        catch (JsonException jsonEx)
        {
            LogJsonParseError(jsonEx);
            await TryWriteErrorResponseAsync(context, 400, "Invalid JSON format").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogErrorHandlingA2ARequest(ex);
            throw;
        }
    }

    private async Task RouteRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var path = context.Request.Url?.AbsolutePath ?? "";
        var method = context.Request.HttpMethod;

        var isDiscovery = method == "GET" && path == "/.well-known/agent.json";

        // Enforce A2A security on task endpoints (discovery stays public for agent cards).
        if (!isDiscovery && !await AuthorizeRequestAsync(context, ct).ConfigureAwait(false))
            return;

        if (isDiscovery)
            await HandleAgentCardAsync(context, ct).ConfigureAwait(false);
        else if (method == "POST" && path == "/a2a/tasks/send")
            await HandleSendTaskAsync(context, ct).ConfigureAwait(false);
        else if (method == "POST" && path == "/a2a/tasks/sendSubscribe")
            await HandleSendSubscribeAsync(context, ct).ConfigureAwait(false);
        else if (method == "GET" && path.StartsWith("/a2a/tasks/", StringComparison.Ordinal))
            await HandleGetTaskAsync(context, path, ct).ConfigureAwait(false);
        else if (method == "DELETE" && path.StartsWith("/a2a/tasks/", StringComparison.Ordinal))
            await HandleCancelTaskAsync(context, path, ct).ConfigureAwait(false);
        else
            await WriteJsonResponse(context.Response, 404, new { error = "Not found" }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the configured <see cref="A2ASecurityOptions"/> to an incoming request.
    /// Writes a 401/403 response and returns <c>false</c> when the request is rejected;
    /// returns <c>true</c> when the request may proceed.
    /// </summary>
    private async Task<bool> AuthorizeRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        // mTLS: require a valid client certificate when mutual TLS is mandated.
        if (_security.RequireMutualTls)
        {
            var clientCert = TryGetClientCertificate(context);
            if (clientCert == null)
            {
                LogA2AUnauthorized("missing or untrusted client certificate (mTLS required)");
                await WriteJsonResponse(context.Response, 403,
                    new { error = "Client certificate required (mTLS)" }, ct).ConfigureAwait(false);
                return false;
            }
        }

        // Bearer/ApiKey: the credential must pass its scheme's validator, not just look right.
        if (_credentials.IsRequired)
        {
            var authHeader = context.Request.Headers["Authorization"];
            var validation = await _credentials.ValidateAsync(authHeader, ct).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                LogA2AUnauthorized(validation.Error ?? "credential rejected");
                await WriteJsonResponse(context.Response, 401,
                    new { error = "Authentication required" }, ct).ConfigureAwait(false);
                return false;
            }
        }

        return true;
    }

    private X509Certificate2? TryGetClientCertificate(HttpListenerContext context)
    {
        try
        {
            var cert = context.Request.GetClientCertificate();
            if (cert == null)
                return null;

            // SEC-012: presence + date validity is not authentication — the certificate
            // must chain to a configured CA or match a pinned thumbprint.
            return IsClientCertificateTrusted(cert) ? cert : null;
        }
        catch (HttpListenerException)
        {
            return null;
        }
    }

    /// <summary>
    /// Materializes the mTLS client trust (pinned thumbprints + CA certificates loaded from
    /// <see cref="A2ASecurityOptions.TrustedCertificateAuthorities"/>). Called by
    /// <see cref="StartAsync"/> when <see cref="A2ASecurityOptions.RequireMutualTls"/> is set;
    /// throws when mutual TLS is required but no usable trust anchor exists (fail-closed:
    /// accepting any date-valid certificate is not mutual authentication).
    /// </summary>
    internal void InitializeMutualTlsTrust()
    {
        _trustedClientThumbprints = new HashSet<string>(
            _security.TrustedClientCertificateThumbprints.Where(t => !string.IsNullOrWhiteSpace(t)),
            StringComparer.OrdinalIgnoreCase);

        DisposeTrustedClientCas();
        foreach (var path in _security.TrustedCertificateAuthorities)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                // OUT-OF-SCOPE: trusted CA bundle on host, not VFS-mounted user data
                _trustedClientCertificateAuthorities.Add(X509CertificateLoader.LoadCertificateFromFile(path));
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
            {
                LogTrustedCaLoadFailed(ex, path);
            }
        }

        if (_security.RequireMutualTls
            && _trustedClientThumbprints.Count == 0
            && _trustedClientCertificateAuthorities.Count == 0)
        {
            throw new InvalidOperationException(
                "RequireMutualTls is enabled but no usable client trust is configured: set " +
                "TrustedCertificateAuthorities (CA certificate paths) and/or " +
                "TrustedClientCertificateThumbprints. Without a trust anchor every date-valid " +
                "certificate would be accepted, which is not mutual authentication. For local " +
                "development without client authentication, disable RequireMutualTls.");
        }

        LogMutualTlsTrustInitialized(_trustedClientCertificateAuthorities.Count, _trustedClientThumbprints.Count);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="certificate"/> matches a pinned thumbprint
    /// (and is within its validity window) or chains to one of the configured trusted CAs
    /// (<see cref="X509ChainTrustMode.CustomRootTrust"/> — self-signed certificates that are
    /// not pinned are rejected). Revocation is not checked: the trust anchors are private
    /// CAs without CRL/OCSP endpoints (documented limitation).
    /// </summary>
    internal bool IsClientCertificateTrusted(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (_trustedClientThumbprints is { Count: > 0 }
            && _trustedClientThumbprints.Contains(certificate.Thumbprint))
        {
            var nowUtc = DateTime.UtcNow;
            return certificate.NotBefore.ToUniversalTime() <= nowUtc
                && nowUtc <= certificate.NotAfter.ToUniversalTime();
        }

        if (_trustedClientCertificateAuthorities.Count == 0)
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        foreach (var ca in _trustedClientCertificateAuthorities)
            chain.ChainPolicy.CustomTrustStore.Add(ca);

        return chain.Build(certificate);
    }

    private void DisposeTrustedClientCas()
    {
        foreach (var ca in _trustedClientCertificateAuthorities)
            ca.Dispose();
        _trustedClientCertificateAuthorities.Clear();
    }

    [SuppressMessage("Design", "CA1031", Justification = "Best-effort error response: a failure to write the error body (e.g. client already disconnected) must not mask the primary failure being reported.")]
    private async Task TryWriteErrorResponseAsync(HttpListenerContext context, int statusCode, string error)
    {
        try
        {
            await WriteJsonResponse(context.Response, statusCode,
                new { error }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception responseEx)
        {
            LogErrorSendingA2AResponse(responseEx);
        }
    }

    private async Task HandleAgentCardAsync(HttpListenerContext context, CancellationToken ct)
    {
        // The skills the router answers, and nothing else: a card listed from anywhere else
        // would publish ids no request can reach (GAP-10, GAP-23).
        var skills = await _taskRouter.GetSkillsAsync(ct).ConfigureAwait(false);

        var card = new AgentCard
        {
            Name = _options.AgentName,
            Description = _options.AgentDescription,
            Url = CardUrl(_options, context.Request.Url),
            Version = _options.AgentVersion,
            Provider = !string.IsNullOrEmpty(_options.Organization)
                ? new AgentProvider
                {
                    Organization = _options.Organization,
                    ContactUrl = _options.ContactUrl
                }
                : null,
            Skills = skills
        };

        await WriteJsonResponse(context.Response, 200, card, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The address the card advertises: the configured listener — or, when that listener is
    /// a wildcard (<c>http://+</c>, <c>http://*</c>: every interface, an address no peer can
    /// call), the address the peer reached the card at. A wildcard used to make the card
    /// endpoint throw, so a server listening beyond the loopback served no card at all.
    /// </summary>
    /// <param name="options">The server's options.</param>
    /// <param name="reachedAt">The URL of the card request, as the peer addressed it.</param>
    internal static Uri? CardUrl(A2AOptions options, Uri? reachedAt)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (Uri.TryCreate($"{options.Host.TrimEnd('/')}:{options.Port}", UriKind.Absolute, out var configured))
            return configured;

        return reachedAt is null ? null : new Uri(reachedAt.GetLeftPart(UriPartial.Authority));
    }

    private async Task HandleSendTaskAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = await ReadJsonBody<A2ATaskRequest>(context.Request, ct).ConfigureAwait(false);
        if (request == null)
        {
            await WriteJsonResponse(context.Response, 400,
                new { error = "Invalid request body" }, ct).ConfigureAwait(false);
            return;
        }

        using var execution = BeginTask(request.Id, ct);
        if (execution is null)
        {
            await WriteTaskAlreadyRunningAsync(context, request.Id, ct).ConfigureAwait(false);
            return;
        }

        A2ATaskResponse response;
        try
        {
            await PersistTaskAsync(request, A2ATaskStatus.Working, null, null, ct).ConfigureAwait(false);
            response = await _taskRouter.RouteTaskAsync(request, progress: null, execution.Token).ConfigureAwait(false);

            // Recorded before the task leaves the in-flight registry: a DELETE never finds it
            // gone from the registry yet still Working in the store.
            await PersistTaskAsync(request, response.Status, response.Output, response.Error, ct).ConfigureAwait(false);
        }
        finally
        {
            EndTask(request.Id, execution);
        }

        await WriteJsonResponse(context.Response, 200, response, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Registers a task the agent is about to work on, under a token that both the server's
    /// shutdown and <c>DELETE /a2a/tasks/{id}</c> cancel. Returns <see langword="null"/> when a
    /// task with that id is already running.
    /// </summary>
    private CancellationTokenSource? BeginTask(string taskId, CancellationToken serverToken)
    {
        var execution = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
        if (_tasksInFlight.TryAdd(taskId, execution))
            return execution;

        execution.Dispose();
        return null;
    }

    private void EndTask(string taskId, CancellationTokenSource execution)
        => _tasksInFlight.TryRemove(new KeyValuePair<string, CancellationTokenSource>(taskId, execution));

    private static Task WriteTaskAlreadyRunningAsync(HttpListenerContext context, string taskId, CancellationToken ct)
        => WriteJsonResponse(context.Response, 409, new
        {
            error = "A task with this id is already running.",
            taskId
        }, ct);

    private async Task HandleSendSubscribeAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = await ReadJsonBody<A2ATaskRequest>(context.Request, ct).ConfigureAwait(false);
        if (request == null)
        {
            await WriteJsonResponse(context.Response, 400,
                new { error = "Invalid request body" }, ct).ConfigureAwait(false);
            return;
        }

        using var execution = BeginTask(request.Id, ct);
        if (execution is null)
        {
            await WriteTaskAlreadyRunningAsync(context, request.Id, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await StreamTaskAsync(context, request, execution.Token, ct).ConfigureAwait(false);
        }
        finally
        {
            EndTask(request.Id, execution);
        }
    }

    private async Task StreamTaskAsync(
        HttpListenerContext context, A2ATaskRequest request, CancellationToken executionToken, CancellationToken ct)
    {
        // Set SSE headers
        context.Response.ContentType = HttpDefaults.SseContentType;
        context.Response.Headers.Add("Cache-Control", "no-cache");
        context.Response.Headers.Add("Connection", "keep-alive");
        context.Response.StatusCode = 200;

        var outputStream = context.Response.OutputStream;
        var writer = new StreamWriter(outputStream, Encoding.UTF8) { AutoFlush = true };

        try
        {
            // Send initial "working" update
            var workingUpdate = new A2ATaskUpdate
            {
                TaskId = request.Id,
                Status = A2ATaskStatus.Working,
                Timestamp = DateTime.UtcNow
            };
            await WriteSseEvent(writer, workingUpdate).ConfigureAwait(false);
            await PersistTaskAsync(request, A2ATaskStatus.Working, null, null, ct).ConfigureAwait(false);

            // Route the task: the agent runs under the token DELETE /a2a/tasks/{id} cancels. What the
            // router reports as the work advances reaches the peer as Working updates, each line as
            // their message (GAP-35) — written by this method alone: the lines queue, and the queue
            // closes when the router answers, so a line reported after the answer is dropped instead
            // of reaching the peer after the final state.
            var lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            var routing = RouteReportingAsync(request, lines.Writer, executionToken);
            var peerReads = true;
            await foreach (var line in lines.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (peerReads)
                {
                    peerReads = await TryWriteProgressAsync(writer, new A2ATaskUpdate
                    {
                        TaskId = request.Id,
                        Status = A2ATaskStatus.Working,
                        Message = line,
                        Timestamp = DateTime.UtcNow
                    }).ConfigureAwait(false);
                }
            }

            var response = await routing.ConfigureAwait(false);
            await PersistTaskAsync(request, response.Status, response.Output, response.Error, ct).ConfigureAwait(false);

            // Send final update
            var finalUpdate = new A2ATaskUpdate
            {
                TaskId = response.TaskId,
                Status = response.Status,
                PartialOutput = response.Output ?? response.Error,
                Timestamp = response.Timestamp
            };
            await WriteSseEvent(writer, finalUpdate).ConfigureAwait(false);

            // Send done marker
            await writer.WriteLineAsync("data: [DONE]").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
        }
        finally
        {
            await writer.DisposeAsync().ConfigureAwait(false);
            outputStream.Close();
        }
    }

    /// <summary>
    /// Routes the task with its progress queued on <paramref name="lines"/>, and closes the queue
    /// when the router answers — or fails: the stream then stops waiting for lines either way.
    /// </summary>
    private async Task<A2ATaskResponse> RouteReportingAsync(
        A2ATaskRequest request, ChannelWriter<string> lines, CancellationToken executionToken)
    {
        try
        {
            return await _taskRouter.RouteTaskAsync(request, new QueuedProgress(lines), executionToken).ConfigureAwait(false);
        }
        finally
        {
            lines.TryComplete();
        }
    }

    /// <summary>
    /// Writes one progress update, or answers false when the peer has gone: progress is best
    /// effort, like a chat thread's, and the task still runs to its end and is recorded.
    /// </summary>
    private static async Task<bool> TryWriteProgressAsync(StreamWriter writer, A2ATaskUpdate update)
    {
        try
        {
            await WriteSseEvent(writer, update).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The router's progress, queued for the stream's one writer. Synchronous on purpose — unlike
    /// <see cref="Progress{T}"/>, which posts each line to the thread pool and loses their order —
    /// and inert once the router has answered: the queue is closed, the line goes nowhere.
    /// </summary>
    private sealed class QueuedProgress(ChannelWriter<string> lines) : IProgress<string>
    {
        public void Report(string value) => _ = lines.TryWrite(value);
    }

    private async Task HandleGetTaskAsync(
        HttpListenerContext context, string path, CancellationToken ct)
    {
        var taskId = Uri.UnescapeDataString(path["/a2a/tasks/".Length..]);

        // Without a task store, keep the explicit 501: better than fabricating a
        // 200/Pending status for an id we know nothing about (PUB-08 lifts this
        // by registering AddOrkeonA2ATaskPersistence over the checkpointing store).
        if (_taskStore == null)
        {
            await WriteJsonResponse(context.Response, 501, new
            {
                error = "Task status retrieval is not enabled: register a checkpointing state store and call AddOrkeonA2ATaskPersistence().",
                taskId
            }, ct).ConfigureAwait(false);
            return;
        }

        var record = await _taskStore.GetAsync(taskId, ct).ConfigureAwait(false);
        if (record == null)
        {
            await WriteJsonResponse(context.Response, 404, new
            {
                error = "Unknown task id.",
                taskId
            }, ct).ConfigureAwait(false);
            return;
        }

        await WriteJsonResponse(context.Response, 200, new A2ATaskResponse
        {
            TaskId = record.TaskId,
            Status = record.Status,
            Output = record.Output,
            Error = record.Error,
            Timestamp = record.UpdatedAt
        }, ct).ConfigureAwait(false);
    }

    private async Task HandleCancelTaskAsync(
        HttpListenerContext context, string path, CancellationToken ct)
    {
        var taskId = Uri.UnescapeDataString(path["/a2a/tasks/".Length..]);

        // A task the agent is still working on: cancel the token its execution runs under.
        // The send request that started it answers Cancelled and records it (GAP-10).
        if (_tasksInFlight.TryGetValue(taskId, out var execution) && await TryCancelAsync(execution).ConfigureAwait(false))
        {
            await WriteCancelledAsync(context, taskId, ct).ConfigureAwait(false);
            return;
        }

        var record = _taskStore is null ? null : await _taskStore.GetAsync(taskId, ct).ConfigureAwait(false);
        if (record is null)
        {
            // Not running here, and no record of it (or no store to hold one).
            await WriteJsonResponse(context.Response, 404, new
            {
                error = "Unknown task id, or a task that is no longer running.",
                taskId
            }, ct).ConfigureAwait(false);
            return;
        }

        if (record.Status is A2ATaskStatus.Completed or A2ATaskStatus.Failed or A2ATaskStatus.Cancelled)
        {
            // A finished task keeps the state its execution left.
            await WriteJsonResponse(context.Response, 409, new
            {
                error = $"The task already finished ({record.Status}) and cannot be cancelled.",
                taskId
            }, ct).ConfigureAwait(false);
            return;
        }

        // A record left Working/Pending by a server that stopped mid-task: nothing runs it any more.
        await _taskStore!.SaveAsync(record with
        {
            Status = A2ATaskStatus.Cancelled,
            UpdatedAt = DateTime.UtcNow
        }, ct).ConfigureAwait(false);
        await WriteCancelledAsync(context, taskId, ct).ConfigureAwait(false);
    }

    /// <summary>Cancels a running task; false when it finished (and released its token) meanwhile.</summary>
    private static async Task<bool> TryCancelAsync(CancellationTokenSource execution)
    {
        try
        {
            await execution.CancelAsync().ConfigureAwait(false);
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static Task WriteCancelledAsync(HttpListenerContext context, string taskId, CancellationToken ct)
        => WriteJsonResponse(context.Response, 200, new A2ATaskResponse
        {
            TaskId = taskId,
            Status = A2ATaskStatus.Cancelled,
            Timestamp = DateTime.UtcNow
        }, ct);

    /// <summary>
    /// Best-effort persistence of a task lifecycle transition: storage failures are
    /// logged, never allowed to fail the request that triggered them.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Persistence is an observability side-channel of the request: a store outage must not turn a successful task exchange into a 500.")]
    private async Task PersistTaskAsync(
        A2ATaskRequest request, A2ATaskStatus status, string? output, string? error, CancellationToken ct)
    {
        if (_taskStore == null) return;

        try
        {
            var existing = await _taskStore.GetAsync(request.Id, ct).ConfigureAwait(false);
            await _taskStore.SaveAsync(new A2ATaskRecord
            {
                TaskId = request.Id,
                SkillId = request.SkillId,
                Status = status,
                Output = output,
                Error = error,
                CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogTaskPersistenceFailed(ex, request.Id);
        }
    }

    private static async Task WriteSseEvent<T>(StreamWriter writer, T data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await writer.WriteLineAsync($"data: {json}").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false); // Empty line to separate events
    }

    private static async Task<T?> ReadJsonBody<T>(HttpListenerRequest request, CancellationToken ct = default) where T : class
    {
        try
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteJsonResponse<T>(HttpListenerResponse response, int statusCode, T body, CancellationToken ct = default)
    {
        response.StatusCode = statusCode;
        response.ContentType = HttpDefaults.JsonContentType;

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;

        await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        response.Close();
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "A2A server started on {Prefix}")]
    private partial void LogA2AServerStarted(string prefix);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "A2A server stopped")]
    private partial void LogA2AServerStopped();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "A2A request cancelled (expected during shutdown)")]
    private partial void LogRequestCancelled(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "A2A HTTP request error, StatusCode={StatusCode}")]
    private partial void LogHttpRequestError(Exception ex, string statusCode);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "A2A request timed out")]
    private partial void LogTimeoutError(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "A2A JSON parse error (unexpected format)")]
    private partial void LogJsonParseError(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error accepting A2A request")]
    private partial void LogErrorAcceptingA2ARequest(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "A2A task persistence failed for task {TaskId}")]
    private partial void LogTaskPersistenceFailed(Exception ex, string taskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error handling A2A request")]
    private partial void LogErrorHandlingA2ARequest(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error sending A2A error response")]
    private partial void LogErrorSendingA2AResponse(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "A2A request rejected: {Reason}")]
    private partial void LogA2AUnauthorized(string reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "A2A mTLS client trust initialized: {CaCount} CA(s) loaded, {ThumbprintCount} pinned thumbprint(s)")]
    private partial void LogMutualTlsTrustInitialized(int caCount, int thumbprintCount);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "A2A trusted CA could not be loaded and is ignored: {Path}")]
    private partial void LogTrustedCaLoadFailed(Exception ex, string path);
}
