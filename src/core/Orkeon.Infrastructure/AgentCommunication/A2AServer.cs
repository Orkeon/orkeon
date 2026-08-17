using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Domain.Agent;
using Orkeon.Infrastructure.Constants.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Constants.Serialization;
using DomainAgent = Orkeon.Domain.Agent.Agent;

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
/// R4.6 / ANT-001: this server is a singleton, so it never captures the scoped
/// <see cref="IAgentRepository"/> (captive dependency). It opens a DI scope per
/// incoming request via <see cref="IServiceScopeFactory"/> and resolves the
/// repository inside that scope; the repository hydrates from the shared
/// <c>IAgentRegistrationStore</c> singleton, which persists across requests.
/// </para>
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class A2AServer : IA2AServer, IDisposable
{
    private readonly A2AOptions _options;
    private readonly A2ASecurityOptions _security;
    private readonly IA2ATaskRouter _taskRouter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    // mTLS client trust, materialized once per StartAsync (SEC-012).
    private HashSet<string>? _trustedClientThumbprints;
    private readonly List<X509Certificate2> _trustedClientCertificateAuthorities = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    private static readonly string[] s_textPlainModes = ["text/plain"];

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <summary>Initializes a new instance of <see cref="A2AServer"/>.</summary>
    /// <param name="options">A2A server options.</param>
    /// <param name="taskRouter">The task router handling submitted tasks.</param>
    /// <param name="scopeFactory">Factory used to open one DI scope per incoming request (the scoped <see cref="IAgentRepository"/> is resolved inside it).</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="security">Optional A2A security options.</param>
    public A2AServer(
        IOptions<A2AOptions> options,
        IA2ATaskRouter taskRouter,
        IServiceScopeFactory scopeFactory,
        ILogger<A2AServer>? logger = null,
        IOptions<A2ASecurityOptions>? security = null)
    {
        _options = options?.Value ?? new A2AOptions();
        _security = security?.Value ?? new A2ASecurityOptions();
        ArgumentNullException.ThrowIfNull(taskRouter);
        _taskRouter = taskRouter;
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
        _logger = logger ?? NullLogger<A2AServer>.Instance;
    }

    /// <summary>Initializes a new instance of <see cref="A2AServer"/> with direct options (useful for testing).</summary>
    public A2AServer(
        A2AOptions options,
        IA2ATaskRouter taskRouter,
        IServiceScopeFactory scopeFactory,
        ILogger<A2AServer>? logger = null,
        A2ASecurityOptions? security = null)
    {
        _options = options ?? new A2AOptions();
        _security = security ?? new A2ASecurityOptions();
        ArgumentNullException.ThrowIfNull(taskRouter);
        _taskRouter = taskRouter;
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
        _logger = logger ?? NullLogger<A2AServer>.Instance;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("A2A server is already running.");

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

        return Task.CompletedTask;
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
                await ProcessContextAsync(context, ct).ConfigureAwait(false);
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

        // Bearer/ApiKey: require an Authorization header matching one of the allowed schemes.
        if (_security.AllowedAuthSchemes.Count > 0)
        {
            var authHeader = context.Request.Headers["Authorization"];
            if (!IsAuthSchemeAllowed(authHeader, _security.AllowedAuthSchemes))
            {
                LogA2AUnauthorized("missing or disallowed authentication scheme");
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

    /// <summary>
    /// Returns <c>true</c> when <paramref name="authorizationHeader"/> presents a non-empty
    /// credential using one of the <paramref name="allowedSchemes"/> (case-insensitive).
    /// </summary>
    internal static bool IsAuthSchemeAllowed(string? authorizationHeader, IReadOnlyList<string> allowedSchemes)
    {
        if (allowedSchemes.Count == 0)
            return true;

        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return false;

        var parts = authorizationHeader.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            return false;

        var scheme = parts[0];
        return allowedSchemes.Any(allowed => string.Equals(scheme, allowed, StringComparison.OrdinalIgnoreCase));
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
        // ANT-001: resolve the scoped repository in a dedicated scope per request —
        // a singleton must never hold on to a scoped service (captive dependency).
        // The repository hydrates from the shared registration store, so agents
        // registered by other scopes (e.g. the execution pipeline) are listed here.
        IReadOnlyList<DomainAgent> agents;
        var scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
            agents = await agentRepository.GetAvailableAgentsAsync(ct).ConfigureAwait(false);
        }

        var skills = agents.Select(a => new AgentSkill
        {
            Id = a.Id.ToString(),
            Name = a.Role.Value,
            Description = a.Goal.Value,
#pragma warning disable CA1308 // lowercase is the required wire/storage form, not a comparison normalization
            Tags = [a.Role.Value.ToLowerInvariant()],
#pragma warning restore CA1308
            InputModes = s_textPlainModes,
            OutputModes = s_textPlainModes
        }).ToList();

        var card = new AgentCard
        {
            Name = _options.AgentName,
            Description = _options.AgentDescription,
            Url = new Uri($"{_options.Host.TrimEnd('/')}:{_options.Port}"),
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

    private async Task HandleSendTaskAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = await ReadJsonBody<A2ATaskRequest>(context.Request, ct).ConfigureAwait(false);
        if (request == null)
        {
            await WriteJsonResponse(context.Response, 400,
                new { error = "Invalid request body" }, ct).ConfigureAwait(false);
            return;
        }

        var response = await _taskRouter.RouteTaskAsync(request, ct).ConfigureAwait(false);
        await WriteJsonResponse(context.Response, 200, response, ct).ConfigureAwait(false);
    }

    private async Task HandleSendSubscribeAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = await ReadJsonBody<A2ATaskRequest>(context.Request, ct).ConfigureAwait(false);
        if (request == null)
        {
            await WriteJsonResponse(context.Response, 400,
                new { error = "Invalid request body" }, ct).ConfigureAwait(false);
            return;
        }

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

            // Route the task
            var response = await _taskRouter.RouteTaskAsync(request, ct).ConfigureAwait(false);

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

    private static async Task HandleGetTaskAsync(
        HttpListenerContext context, string path, CancellationToken ct)
    {
        // Task status retrieval requires task persistence, which is not yet implemented
        // (see remediation R3.8). Return an explicit 501 instead of fabricating a 200/Pending
        // status for an unknown task id. Once persistence lands, this becomes 404 for unknown ids.
        var taskId = Uri.UnescapeDataString(path["/a2a/tasks/".Length..]);

        await WriteJsonResponse(context.Response, 501, new
        {
            error = "Task status retrieval is not implemented (no task persistence).",
            taskId
        }, ct).ConfigureAwait(false);
    }

    private static async Task HandleCancelTaskAsync(
        HttpListenerContext context, string path, CancellationToken ct)
    {
        var taskId = path["/a2a/tasks/".Length..];

        await WriteJsonResponse(context.Response, 200, new A2ATaskResponse
        {
            TaskId = Uri.UnescapeDataString(taskId),
            Status = A2ATaskStatus.Cancelled,
            Timestamp = DateTime.UtcNow
        }, ct).ConfigureAwait(false);
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
