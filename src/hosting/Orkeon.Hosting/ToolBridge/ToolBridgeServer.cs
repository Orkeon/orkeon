using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Security;
using Orkeon.Domain.Tools;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ProtocolToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The serving side of the tool bridge (#445, ADR-019): an <see cref="HttpListener"/> that runs, for a caller on
/// another machine, the tools both sides selected. It runs each tool raw: the caller's pipeline (its gate, its
/// guardian, its truncation, its sanitizer) judges the call and its result, and the bridge records the call in its
/// own audit under the caller <see cref="ToolBridgeProtocol.AuditCaller"/>.
/// <para>
/// A request carrying an <c>Origin</c> header (a browser page) is refused, as is one without the token, one from
/// another machine when the prefix is loopback, one whose body exceeds <see cref="ToolBridgeServerOptions.MaxRequestBytes"/>
/// or does not arrive within <see cref="ToolBridgeServerOptions.RequestReadTimeout"/>, and a <c>POST</c> that is not
/// JSON. The token is read once, at start, from the environment variable the options name, and compared in fixed time.
/// </para>
/// <para>
/// A caller opens a session (<c>POST session</c>, <see cref="ToolBridgeSessions"/>) and names it on every later
/// request; a session the bridge does not hold is answered <see cref="ToolBridgeProtocol.SessionLostStatus"/>. A call
/// of a caller's run runs in the session's scope of that run, ended by the caller's <c>POST end-run</c> or with the
/// session, at a missed heartbeat window. A call that carries the caller's mounts runs over a scratch folder presented
/// at those virtual paths (<see cref="ToolBridgeScratch"/>), and its answer carries what the tool wrote there. The
/// calls in flight, every session together and per session, and the runs of a session are bounded; one past a bound
/// is refused, never queued.
/// </para>
/// <para>
/// <c>orkeon bridge serve</c> builds one over the host's registry, calls <see cref="StartAsync"/>, reports
/// <see cref="Prefix"/>, <see cref="ServedTools"/> and <see cref="Warnings"/>, and waits; <see cref="StopAsync"/>
/// cancels the calls in flight and waits for them, up to <see cref="ToolBridgeServerOptions.StopTimeout"/>.
/// </para>
/// </summary>
[SuppressMessage("Usage", "CA2213", Justification = "The listener is disposed by HttpListener.Abort in StopAsync: Close after Stop binds the port again on Linux and macOS.")]
internal sealed partial class ToolBridgeServer : IAsyncDisposable
{
    private readonly IToolRegistry _registry;
    private readonly ToolBridgeServerOptions _options;
    private readonly Func<string, string?> _readEnvironment;
    private readonly IAuditLogger? _audit;
    private readonly ILogger _logger;
    private readonly IFileSystemScope? _fileScope;
    private readonly FileSystemRegistry? _bootMounts;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<System.Threading.Tasks.Task, byte> _inFlight = new();
    private int _activeCalls;
    private bool _loopbackOnly;
    private ImmutableDictionary<string, (IBaseTool Tool, string Hash)> _served =
        ImmutableDictionary<string, (IBaseTool Tool, string Hash)>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private byte[]? _tokenHash;
    private long _refusedTokens;
    private long _lastRefusalLog;

    /// <summary>The shortest interval between two log lines of requests refused for their token.</summary>
    internal static readonly TimeSpan RefusalLogInterval = TimeSpan.FromSeconds(10);
    private HttpListener? _listener;
    private System.Threading.Tasks.Task? _loop;
    private ToolBridgeSessions? _sessions;

    /// <summary>Builds a server over <paramref name="registry"/>; nothing listens before <see cref="StartAsync"/>.</summary>
    /// <param name="registry">The registry whose tools are served.</param>
    /// <param name="options">The section <c>Orkeon:Bridge</c>.</param>
    /// <param name="readEnvironment">Reads an environment variable; the process environment when null.</param>
    /// <param name="audit">The audit each call is recorded in; none when null.</param>
    /// <param name="logger">The server's logger.</param>
    /// <param name="fileScope">
    /// The ambient scope the bridge's file system reads, entered over the scratch of a call that carries the caller's
    /// mounts; without it (or <paramref name="bootMounts"/>) such a call fails.
    /// </param>
    /// <param name="bootMounts">The bridge's own mounts, whose internal ones a call's scratch carries forward.</param>
    /// <param name="timeProvider">The clock of the heartbeat window; the system's when null.</param>
    public ToolBridgeServer(
        IToolRegistry registry,
        ToolBridgeServerOptions options,
        Func<string, string?>? readEnvironment = null,
        IAuditLogger? audit = null,
        ILogger? logger = null,
        IFileSystemScope? fileScope = null,
        FileSystemRegistry? bootMounts = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        _registry = registry;
        _options = options;
        _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
        _audit = audit;
        _logger = logger ?? NullLogger.Instance;
        _fileScope = fileScope;
        _bootMounts = bootMounts;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The prefix the server listens on.</summary>
    public string Prefix => _options.Prefix;

    /// <summary>The names of the tools served, in the registry's order; empty before <see cref="StartAsync"/>.</summary>
    public ImmutableList<string> ServedTools { get; private set; } = [];

    /// <summary>One sentence per tool that acts on the workspace and is served because the settings name it.</summary>
    public ImmutableList<string> Warnings { get; private set; } = [];

    /// <summary>The number of runs the bridge holds for the session <paramref name="sessionId"/>.</summary>
    /// <param name="sessionId">The id the bridge issued to the caller.</param>
    /// <returns>Zero for a session without runs or unknown, or before <see cref="StartAsync"/>.</returns>
    public int RunsOf(string? sessionId) => _sessions?.RunsOf(sessionId) ?? 0;

    /// <summary>The number of sessions the bridge holds.</summary>
    public int Sessions => _sessions?.Count ?? 0;

    /// <summary>Whether the listener is running.</summary>
    public bool IsRunning => _listener is { IsListening: true };

    /// <summary>
    /// Reads the token, resolves the selection and starts listening. Refuses with
    /// <see cref="RunnerSettingsException"/> a missing token, a name that selects no tool, an unsound option.
    /// </summary>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>A task that completes once the listener runs.</returns>
    public async System.Threading.Tasks.Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
            throw new InvalidOperationException("The tool bridge is already started.");
        var problems = _options.Problems().ToList();
        if (problems.Count > 0)
            throw new RunnerSettingsException(string.Join(" ", problems));
        // HttpListener serves https through HTTP.sys alone: elsewhere it accepts the prefix and then fails every
        // handshake, so the bridge would say it listens while no caller can reach it.
        if (!OperatingSystem.IsWindows()
            && Uri.TryCreate(_options.Prefix, UriKind.Absolute, out var prefixUri) && prefixUri.Scheme == Uri.UriSchemeHttps)
            throw new RunnerSettingsException(
                $"{ToolBridgeServerOptions.SectionName}:Prefix '{_options.Prefix}' asks for https, which the tool bridge serves under Windows only: "
                + "keep the prefix on loopback over http and reach it from a container sharing the host's network (docker run --network host), "
                + "or put a TLS terminator in front of it.");
        var token = ToolBridgeProtocol.ReadToken(_readEnvironment(_options.TokenEnvVar), _options.TokenEnvVar, out var tokenProblem)
            ?? throw new RunnerSettingsException($"the tool bridge refuses the start: {tokenProblem}");
        _tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        var tools = await _registry.GetAllToolsAsync(cancellationToken).ConfigureAwait(false);
        var selection = ToolBridgeSelection.Resolve(ToolBridgeServerOptions.SectionName, tools, [.. _options.Allow], [.. _options.Deny]);
        if (selection.Problems.Count > 0)
            throw new RunnerSettingsException(string.Join(" ", selection.Problems));
        _served = selection.Tools.ToImmutableDictionary(
            t => t.Name, t => (t, ToolBridgeContract.HashOf(t)), StringComparer.OrdinalIgnoreCase);
        ServedTools = [.. selection.Tools.Select(t => t.Name)];
        Warnings = selection.Warnings;

        var listener = new HttpListener();
        listener.Prefixes.Add(_options.Prefix);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            listener.Abort();
            throw new RunnerSettingsException(
                $"the tool bridge could not listen on '{_options.Prefix}' ({ToolBridgeServerOptions.SectionName}:Prefix): {ex.Message}. "
                + "Another process holds the port, or, under Windows, a prefix other than localhost is not reserved for this account "
                + $"(netsh http add urlacl url={_options.Prefix} user=<account>).", ex);
        }

        _listener = listener;
        _loopbackOnly = Uri.TryCreate(_options.Prefix, UriKind.Absolute, out var listened) && ToolBridgeAddress.IsLoopback(listened);
        ToolBridgeScratch.HoldParent();
        _sessions = new ToolBridgeSessions(_options.HeartbeatWindow, _options.MaxSessions, _options.MaxRunsPerSession,
            _options.MaxCallsPerSession, _time, _logger);
        _loop = ListenAsync(listener);
        LogServing(_logger, _options.Prefix, ServedTools.Count == 0 ? "(none)" : string.Join(", ", ServedTools));
        foreach (var warning in Warnings)
            LogWarning(_logger, warning);
    }

    /// <summary>
    /// Stops listening, cancels the calls in flight and waits for them up to
    /// <see cref="ToolBridgeServerOptions.StopTimeout"/>; a call still running then is abandoned and logged, and every
    /// session ends with its runs.
    /// </summary>
    /// <returns>A task that completes once every request has ended or was abandoned.</returns>
    public async System.Threading.Tasks.Task StopAsync()
    {
        if (_listener is not { } listener)
            return;
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (listener.IsListening)
            listener.Stop();
        if (_loop is { } loop)
            await loop.ConfigureAwait(false);
        var pending = _inFlight.Keys.ToList();
        var all = System.Threading.Tasks.Task.WhenAll(pending);
        if (!all.IsCompleted)
        {
            using var deadline = new CancellationTokenSource();
            var expired = System.Threading.Tasks.Task.Delay(_options.StopTimeout, _time, deadline.Token);
            if (await System.Threading.Tasks.Task.WhenAny(all, expired).ConfigureAwait(false) != all)
                LogCallsAbandoned(_logger, pending.Count(t => !t.IsCompleted), _options.StopTimeout.TotalSeconds);
            await deadline.CancelAsync().ConfigureAwait(false);
        }

        if (_sessions is { } sessions)
            await sessions.DisposeAsync().ConfigureAwait(false);
        _sessions = null;
        ToolBridgeScratch.ReleaseParent();
        // Abort, never Close after Stop: Close binds the port again for an instant on Linux and macOS.
        listener.Abort();
        _listener = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "The listener ends in HttpListenerException or ObjectDisposedException depending on the platform once stopped; either ends the loop.")]
    private async System.Threading.Tasks.Task ListenAsync(HttpListener listener)
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stopping.IsCancellationRequested || !listener.IsListening)
            {
                return;
            }

            var request = HandleAsync(context);
            _inFlight.TryAdd(request, 0);
            _ = request.ContinueWith(t => _inFlight.TryRemove(t, out _), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "A request that fails in any way is answered 500 or abandoned; it never ends the server.")]
    private async System.Threading.Tasks.Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            // Null: the body did not arrive in time and the connection is closed already.
            if (await AnswerAsync(context).ConfigureAwait(false) is { } answer)
                await WriteAsync(context.Response, answer.Status, answer.Body).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogRequestFailed(_logger, ex.GetType().Name);
            try
            {
                await WriteAsync(context.Response, HttpStatusCode.InternalServerError,
                    new BridgeErrorResponse { Error = "the tool bridge failed to answer the request." }).ConfigureAwait(false);
            }
            catch (Exception)
            {
                context.Response.Abort();
            }
        }
    }

    private async System.Threading.Tasks.Task<(HttpStatusCode Status, object Body)?> AnswerAsync(HttpListenerContext context)
    {
        var request = context.Request;
        if (!string.IsNullOrEmpty(request.Headers["Origin"]))
            return Refuse(HttpStatusCode.Forbidden, "a request carrying an Origin header is refused: the tool bridge serves no browser page.");
        // HTTP.sys listens on every interface for the port and routes on the Host header: a loopback prefix answers
        // this machine alone only when the peer's address is checked as well.
        if (_loopbackOnly && !IsLoopback(request.RemoteEndPoint?.Address))
            return Refuse(HttpStatusCode.Forbidden, "the tool bridge listens on loopback and answers this machine alone.");
        if (!HoldsToken(request.Headers["Authorization"]))
        {
            NoteRefusedToken(request);
            return Refuse(HttpStatusCode.Unauthorized, "the token is missing or refused.");
        }
        var path = request.Url?.AbsolutePath ?? "";
        if (!path.StartsWith(ToolBridgeProtocol.BasePath, StringComparison.Ordinal))
            return Refuse(HttpStatusCode.NotFound, $"no endpoint at '{path}': the tool bridge serves {ToolBridgeProtocol.BasePath}.");
        var endpoint = path[ToolBridgeProtocol.BasePath.Length..];

        if (request.HttpMethod == "GET" && endpoint == ToolBridgeProtocol.ToolsEndpoint)
            return (HttpStatusCode.OK, ListTools());
        if (request.HttpMethod != "POST" || !s_postEndpoints.Contains(endpoint))
            return Refuse(HttpStatusCode.NotFound, $"no endpoint {request.HttpMethod} '{endpoint}'.");
        if (!IsJson(request.ContentType))
            return Refuse(HttpStatusCode.UnsupportedMediaType, "a POST of the tool bridge carries application/json.");
        if (request.ContentLength64 > _options.MaxRequestBytes)
            return TooLarge();
        if (_sessions is not { } sessions)
            return Refuse(HttpStatusCode.ServiceUnavailable, "the tool bridge is stopping.");
        var read = await ReadBodyAsync(context).ConfigureAwait(false);
        if (read.TimedOut)
            return null;
        if (read.Body is not { } body)
            return TooLarge();

        if (endpoint == ToolBridgeProtocol.SessionEndpoint)
            return OpenSession(sessions);
        if (string.IsNullOrWhiteSpace(request.Headers[ToolBridgeProtocol.SessionHeader]))
            return Refuse(HttpStatusCode.BadRequest, $"a request of the tool bridge other than {ToolBridgeProtocol.SessionEndpoint} names its session in the header {ToolBridgeProtocol.SessionHeader}.");
        if (sessions.Find(request.Headers[ToolBridgeProtocol.SessionHeader]) is not { } session)
            return SessionLost();

        return endpoint switch
        {
            ToolBridgeProtocol.CallEndpoint => await CallAsync(sessions, session, body).ConfigureAwait(false),
            ToolBridgeProtocol.CancelEndpoint => Cancel(sessions, session, body),
            ToolBridgeProtocol.EndRunEndpoint => await EndRunAsync(sessions, session, body).ConfigureAwait(false),
            _ => (HttpStatusCode.OK, new Dictionary<string, object?>()),
        };
    }

    private static readonly HashSet<string> s_postEndpoints = new(StringComparer.Ordinal)
    {
        ToolBridgeProtocol.SessionEndpoint, ToolBridgeProtocol.CallEndpoint, ToolBridgeProtocol.CancelEndpoint,
        ToolBridgeProtocol.EndRunEndpoint, ToolBridgeProtocol.HeartbeatEndpoint,
    };

    private static bool IsLoopback(IPAddress? address) =>
        address is not null && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    private BridgeToolListResponse ListTools() => new()
    {
        Tools = [.. ServedTools.Select(name => new BridgeServedTool { Name = name, ContractHash = _served[name].Hash })],
        HeartbeatWindowMs = (long)_options.HeartbeatWindow.TotalMilliseconds,
    };

    private (HttpStatusCode Status, object Body) OpenSession(ToolBridgeSessions sessions) =>
        sessions.Open() is { } id
            ? (HttpStatusCode.Created, new BridgeSessionResponse { SessionId = id, HeartbeatWindowMs = (long)_options.HeartbeatWindow.TotalMilliseconds })
            : Refuse(HttpStatusCode.ServiceUnavailable,
                $"the tool bridge holds {ToolBridgeServerOptions.SectionName}:MaxSessions ({_options.MaxSessions}) sessions already: one must end, or its heartbeat window pass, first.");

    private static (HttpStatusCode Status, object Body) SessionLost() =>
        Refuse(ToolBridgeProtocol.SessionLostStatus,
            "the tool bridge holds no such session: it was never opened, or it ended with its runs (a missed heartbeat window, or a restart of the bridge).");

    private static async System.Threading.Tasks.Task<(HttpStatusCode Status, object Body)> EndRunAsync(
        ToolBridgeSessions sessions, ToolBridgeSessions.Session session, byte[] body)
    {
        if (Parse<BridgeRunRequest>(body) is not { } run || string.IsNullOrWhiteSpace(run.RunId))
            return Refuse(HttpStatusCode.BadRequest, "the body of the end of a run names a run_id.");
        await sessions.EndRunAsync(session, run.RunId).ConfigureAwait(false);
        return (HttpStatusCode.OK, new Dictionary<string, object?>());
    }

    [SuppressMessage("Reliability", "CA2000",
        Justification = "The scope of a run belongs to the session, which ends it with the run, or with the session.")]
    private async System.Threading.Tasks.Task<(HttpStatusCode Status, object Body)> CallAsync(
        ToolBridgeSessions sessions, ToolBridgeSessions.Session session, byte[] body)
    {
        if (Parse<BridgeCallRequest>(body) is not { } call || string.IsNullOrWhiteSpace(call.CallId) || string.IsNullOrWhiteSpace(call.Tool)
            || call.Arguments.ValueKind != JsonValueKind.Object)
            return Refuse(HttpStatusCode.BadRequest, "the body of a call names a call_id, a tool and an object of arguments.");
        if (call.Mounts is { } mounts && mounts.Any(m => m is null || string.IsNullOrWhiteSpace(m.Path)))
            return Refuse(HttpStatusCode.BadRequest, "each mount of a call names its path.");
        if (call.Files is { } files && files.Any(f => f is null || string.IsNullOrWhiteSpace(f.Path)))
            return Refuse(HttpStatusCode.BadRequest, "each file of a call names its path.");
        if (call.Mounts is not null)
        {
            if (ToolBridgeMounts.Normalize(call.Mounts, _bootMounts, out var problem) is not { } normalized)
                return Refuse(HttpStatusCode.BadRequest, problem!);
            call = call with { Mounts = normalized };
        }
        if (!_served.TryGetValue(call.Tool, out var served))
            return Refuse(HttpStatusCode.NotFound, $"the tool bridge serves no tool '{call.Tool}'.");

        if (Interlocked.Increment(ref _activeCalls) > _options.MaxConcurrentCalls)
        {
            Interlocked.Decrement(ref _activeCalls);
            return Refuse(HttpStatusCode.ServiceUnavailable,
                $"the tool bridge runs {ToolBridgeServerOptions.SectionName}:MaxConcurrentCalls ({_options.MaxConcurrentCalls}) calls already: try again once one has ended.");
        }

        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
            switch (sessions.BeginCall(session, call.CallId, cancellation))
            {
                case SessionAdmission.Lost:
                    return SessionLost();
                case SessionAdmission.Duplicate:
                    return Refuse(HttpStatusCode.Conflict, $"a call '{call.CallId}' is already in flight in this session.");
                case SessionAdmission.Full:
                    return Refuse(HttpStatusCode.TooManyRequests,
                        $"this session runs {ToolBridgeServerOptions.SectionName}:MaxCallsPerSession ({_options.MaxCallsPerSession}) calls already: try again once one has ended.");
            }

            try
            {
                AgentRunScope? run = null;
                if (call.RunId is { Length: > 0 } runId)
                {
                    switch (sessions.ScopeOf(session, runId, out run))
                    {
                        case SessionAdmission.Lost:
                            return SessionLost();
                        case SessionAdmission.Full:
                            return Refuse(HttpStatusCode.TooManyRequests,
                                $"this session holds {ToolBridgeServerOptions.SectionName}:MaxRunsPerSession ({_options.MaxRunsPerSession}) runs already: one must end first.");
                    }
                }

                return await RunAuditedAsync(served.Tool, call, run, $"{session.Label}/{call.CallId}", cancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                sessions.EndCall(session, call.CallId);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
        }
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "A tool may throw anything; the call is answered and audited as a failure, never left unrecorded.")]
    private async System.Threading.Tasks.Task<(HttpStatusCode Status, object Body)> RunAuditedAsync(
        IBaseTool tool, BridgeCallRequest call, AgentRunScope? run, string auditId, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        BridgeCallResponse answer;
        try
        {
            answer = await RunAsync(tool, call, run, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A tool that throws is a failed call, audited as one; its message may name a physical path, so only its
            // type crosses to the caller.
            LogCallFailed(_logger, tool.Name, ex.GetType().Name);
            answer = Failure($"the tool '{tool.Name}' failed on the tool bridge ({ex.GetType().Name}).");
        }

        await AuditAsync(auditId, tool.Name, answer.Success ? AuditOutcome.Success : AuditOutcome.Failure, watch.ElapsedMilliseconds)
            .ConfigureAwait(false);
        return (HttpStatusCode.OK, answer);
    }

    /// <summary>
    /// Runs the call in the session's scope of the caller's run, over a scratch presenting the caller's mounts (none
    /// when the call names none), and answers with the tool's raw response, the bridge's physical paths scrubbed from
    /// its error and metadata, and the files it wrote. Only a tool that acts on the workspace, which the settings named,
    /// runs over the bridge's own mounts, and only for a call that sends none.
    /// </summary>
    private async System.Threading.Tasks.Task<BridgeCallResponse> RunAsync(
        IBaseTool tool, BridgeCallRequest call, AgentRunScope? run, CancellationToken cancellationToken)
    {
        var needsScratch = call.Mounts is not null || (tool.UsesFileSystem && !ToolContracts.ActsOnWorkspace(tool));
        if (needsScratch && (_fileScope is null || _bootMounts is null))
            return Failure("the tool bridge has no file system to present the caller's mounts on.");
        if (call.Files is { } sent && sent.Sum(f => (long)f.Content.Length) > _options.MaxFileBytes)
            return Failure($"the files the call sends exceed {ToolBridgeServerOptions.SectionName}:MaxFileBytes ({ToolBridgeProtocol.Describe(_options.MaxFileBytes)}).");

        using var entered = run?.Enter();
        ToolBridgeScratch? scratch = null;
        try
        {
            if (needsScratch)
                scratch = ToolBridgeScratch.Enter(call, _fileScope!, _bootMounts!, _logger);
            ProtocolToolCallResponse response;
            try
            {
                var arguments = ToolBridgeProtocol.ToPlainObject(call.Arguments);
                response = await tool.CallAsync(new ProtocolToolCallRequest(tool.Name, arguments, call.Context), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                response = new ProtocolToolCallResponse(false, null, "Operation cancelled", new Dictionary<string, object?> { ["cancelled"] = true });
            }

            var answer = ToolBridgeRedaction.Scrub(ToWire(response), ToolBridgeRedaction.PathsOf(_bootMounts, scratch));
            if (scratch is null)
                return answer;
            var written = await scratch.WrittenAsync(_options.MaxFileBytes, _stopping.Token).ConfigureAwait(false);
            return written.Count == 0 ? answer : answer with { Files = written };
        }
        catch (ToolBridgeFileException ex)
        {
            return Failure(ex.Message);
        }
        finally
        {
            scratch?.Dispose();
        }
    }

    private static BridgeCallResponse Failure(string reason) => new() { Success = false, Error = reason };

    private static (HttpStatusCode Status, object Body) Cancel(ToolBridgeSessions sessions, ToolBridgeSessions.Session session, byte[] body)
    {
        if (Parse<BridgeCancelRequest>(body) is not { } cancel || string.IsNullOrWhiteSpace(cancel.CallId))
            return Refuse(HttpStatusCode.BadRequest, "the body of a cancellation names a call_id.");
        sessions.Cancel(session, cancel.CallId);
        return (HttpStatusCode.OK, new Dictionary<string, object?>());
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "An audit store that fails must not fail the call it records; the failure is logged.")]
    private async System.Threading.Tasks.Task AuditAsync(string callId, string tool, AuditOutcome outcome, long durationMs)
    {
        if (_audit is null)
            return;
        try
        {
            await _audit.LogAsync(AuditEventBuilders.ToolExecution(callId, tool, ToolBridgeProtocol.AuditCaller, outcome, durationMs), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAuditFailed(_logger, tool, ex.GetType().Name);
        }
    }

    /// <summary>The tool's response as the wire carries it: what does not serialize is a failure, not a lost result.</summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "A result the serializer cannot write fails in NotSupportedException, InvalidOperationException or JsonException; each is the same answer.")]
    internal static BridgeCallResponse ToWire(ProtocolToolCallResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        JsonElement? result = null;
        if (response.Result is not null)
        {
            try
            {
                result = JsonSerializer.SerializeToElement(response.Result, response.Result.GetType(), ToolBridgeProtocol.JsonOptions);
            }
            catch (Exception ex)
            {
                return new BridgeCallResponse
                {
                    Success = false,
                    Error = $"the tool bridge could not send the result of type {response.Result.GetType().Name}: {ex.GetType().Name}.",
                };
            }
        }

        var metadata = ImmutableDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in response.Metadata ?? [])
        {
            try
            {
                metadata[key] = JsonSerializer.SerializeToElement(value, ToolBridgeProtocol.JsonOptions);
            }
            catch (Exception)
            {
                // An entry that does not serialize is left out: the metadata is advisory.
            }
        }

        return new BridgeCallResponse
        {
            Success = response.Success,
            Result = result,
            Error = response.Error,
            Metadata = metadata.Count > 0 ? metadata.ToImmutable() : null,
            AlreadyBounded = response.AlreadyBounded,
        };
    }

    /// <summary>
    /// Logs a request refused for its token, at most once per <see cref="RefusalLogInterval"/>, with the number refused
    /// since the last line; the token presented is never logged.
    /// </summary>
    private void NoteRefusedToken(HttpListenerRequest request)
    {
        var count = Interlocked.Increment(ref _refusedTokens);
        var now = _time.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastRefusalLog);
        if (last != 0 && now - last < RefusalLogInterval.Ticks)
            return;
        if (Interlocked.CompareExchange(ref _lastRefusalLog, now, last) != last)
            return;
        Interlocked.Exchange(ref _refusedTokens, 0);
        LogTokenRefused(_logger, request.RemoteEndPoint?.Address.ToString() ?? "(unknown)", count);
    }

    private bool HoldsToken(string? authorization)
    {
        if (_tokenHash is null || authorization is null)
            return false;
        var prefix = ToolBridgeProtocol.BearerScheme + " ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var given = SHA256.HashData(Encoding.UTF8.GetBytes(authorization[prefix.Length..].Trim()));
        return CryptographicOperations.FixedTimeEquals(given, _tokenHash);
    }

    private static bool IsJson(string? contentType) =>
        contentType is not null
        && contentType.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The body of the request, null once it exceeds <see cref="ToolBridgeServerOptions.MaxRequestBytes"/>. A body that
    /// has not arrived within <see cref="ToolBridgeServerOptions.RequestReadTimeout"/> closes the connection: a read
    /// the listener does not cancel is ended by the abort.
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "An aborted read ends in HttpListenerException, IOException or ObjectDisposedException depending on the platform; each is the timeout.")]
    private async System.Threading.Tasks.Task<(byte[]? Body, bool TimedOut)> ReadBodyAsync(HttpListenerContext context)
    {
        using var timeout = new CancellationTokenSource(_options.RequestReadTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _stopping.Token);
        var aborted = 0;
        await using var abort = timeout.Token.Register(() =>
        {
            Interlocked.Exchange(ref aborted, 1);
            context.Response.Abort();
        }).ConfigureAwait(false);
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await context.Request.InputStream.ReadAsync(chunk, linked.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > _options.MaxRequestBytes)
                    return (null, false);
                await buffer.WriteAsync(chunk.AsMemory(0, read), linked.Token).ConfigureAwait(false);
            }

            return (buffer.ToArray(), false);
        }
        catch (Exception) when (Volatile.Read(ref aborted) == 1 || timeout.IsCancellationRequested)
        {
            LogBodyTimedOut(_logger, _options.RequestReadTimeout.TotalSeconds);
            if (Volatile.Read(ref aborted) == 0)
                context.Response.Abort();
            return (null, true);
        }
    }

    private static T? Parse<T>(byte[] body) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, ToolBridgeProtocol.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private (HttpStatusCode Status, object Body) TooLarge() =>
        Refuse(HttpStatusCode.RequestEntityTooLarge,
            $"the body exceeds {ToolBridgeServerOptions.SectionName}:MaxRequestBytes ({ToolBridgeProtocol.Describe(_options.MaxRequestBytes)}).");

    private static (HttpStatusCode Status, object Body) Refuse(HttpStatusCode status, string reason) =>
        (status, new BridgeErrorResponse { Error = reason });

    private static async System.Threading.Tasks.Task WriteAsync(HttpListenerResponse response, HttpStatusCode status, object body)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), ToolBridgeProtocol.JsonOptions);
        response.StatusCode = (int)status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Tool bridge listening on {Prefix}, serving: {Tools}")]
    private static partial void LogServing(ILogger logger, string prefix, string tools);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "{Warning}")]
    private static partial void LogWarning(ILogger logger, string warning);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Tool bridge request failed: {Error}")]
    private static partial void LogRequestFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Tool bridge audit of {Tool} failed: {Error}")]
    private static partial void LogAuditFailed(ILogger logger, string tool, string error);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Tool bridge call of {Tool} failed: {Error}")]
    private static partial void LogCallFailed(ILogger logger, string tool, string error);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning,
        Message = "Tool bridge refused {Count} request(s) for a missing or wrong token, the last from {Remote}")]
    private static partial void LogTokenRefused(ILogger logger, string remote, long count);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Tool bridge closed a connection whose request body did not arrive within {Timeout} s")]
    private static partial void LogBodyTimedOut(ILogger logger, double timeout);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning,
        Message = "Tool bridge stopped with {Count} call(s) still running after {Timeout} s: they are abandoned")]
    private static partial void LogCallsAbandoned(ILogger logger, int count, double timeout);
}
