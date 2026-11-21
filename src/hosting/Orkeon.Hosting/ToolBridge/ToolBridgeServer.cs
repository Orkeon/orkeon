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
/// A request carrying an <c>Origin</c> header (a browser page) is refused, as is one without the token, one whose
/// body exceeds <see cref="ToolBridgeServerOptions.MaxRequestBytes"/>, and a <c>POST</c> that is not JSON. The token
/// is read once, at start, from the environment variable the options name, and compared in fixed time.
/// </para>
/// <para>
/// A call of a caller's run runs in the bridge's scope of that run (<see cref="ToolBridgeRuns"/>), ended by the
/// caller's <c>POST end-run</c> or by a missed heartbeat window. A call that carries the caller's mounts runs over a
/// scratch folder presented at those virtual paths (<see cref="ToolBridgeScratch"/>), and its answer carries what the
/// tool wrote there.
/// </para>
/// <para>
/// <c>orkeon bridge serve</c> builds one over the host's registry, calls <see cref="StartAsync"/>, reports
/// <see cref="Prefix"/>, <see cref="ServedTools"/> and <see cref="Warnings"/>, and waits; <see cref="StopAsync"/>
/// cancels the calls in flight and waits for them.
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
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _calls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<System.Threading.Tasks.Task, byte> _inFlight = new();
    private ImmutableDictionary<string, (IBaseTool Tool, string Hash)> _served =
        ImmutableDictionary<string, (IBaseTool Tool, string Hash)>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private byte[]? _tokenHash;
    private HttpListener? _listener;
    private System.Threading.Tasks.Task? _loop;
    private ToolBridgeRuns? _runs;

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

    /// <summary>The number of runs the bridge holds for the caller <paramref name="callerId"/>.</summary>
    /// <param name="callerId">The id the caller's link sends.</param>
    /// <returns>Zero for a caller without runs, or before <see cref="StartAsync"/>.</returns>
    public int RunsOf(string callerId) => _runs?.RunsOf(callerId) ?? 0;

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
        var token = _readEnvironment(_options.TokenEnvVar);
        if (string.IsNullOrEmpty(token))
            throw new RunnerSettingsException(
                $"the tool bridge reads its token from the environment variable {_options.TokenEnvVar}, which is not set.");
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
        listener.Start();
        _listener = listener;
        _runs = new ToolBridgeRuns(_options.HeartbeatWindow, _time, _logger);
        _loop = ListenAsync(listener);
        LogServing(_logger, _options.Prefix, ServedTools.Count == 0 ? "(none)" : string.Join(", ", ServedTools));
        foreach (var warning in Warnings)
            LogWarning(_logger, warning);
    }

    /// <summary>Stops listening, cancels the calls in flight and waits for them.</summary>
    /// <returns>A task that completes once every request has ended.</returns>
    public async System.Threading.Tasks.Task StopAsync()
    {
        if (_listener is not { } listener)
            return;
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (listener.IsListening)
            listener.Stop();
        if (_loop is { } loop)
            await loop.ConfigureAwait(false);
        await System.Threading.Tasks.Task.WhenAll(_inFlight.Keys).ConfigureAwait(false);
        if (_runs is { } runs)
            await runs.DisposeAsync().ConfigureAwait(false);
        _runs = null;
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
            var (status, body) = await AnswerAsync(context.Request).ConfigureAwait(false);
            await WriteAsync(context.Response, status, body).ConfigureAwait(false);
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

    private async System.Threading.Tasks.Task<(HttpStatusCode Status, object Body)> AnswerAsync(HttpListenerRequest request)
    {
        if (!string.IsNullOrEmpty(request.Headers["Origin"]))
            return Refuse(HttpStatusCode.Forbidden, "a request carrying an Origin header is refused: the tool bridge serves no browser page.");
        if (!HoldsToken(request.Headers["Authorization"]))
            return Refuse(HttpStatusCode.Unauthorized, "the token is missing or refused.");
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
        var body = await ReadBoundedAsync(request.InputStream, _options.MaxRequestBytes, _stopping.Token).ConfigureAwait(false);
        if (body is null)
            return TooLarge();

        return endpoint switch
        {
            ToolBridgeProtocol.CallEndpoint => await CallAsync(body).ConfigureAwait(false),
            ToolBridgeProtocol.CancelEndpoint => Cancel(body),
            ToolBridgeProtocol.EndRunEndpoint => await EndRunAsync(body).ConfigureAwait(false),
            _ => Heartbeat(body),
        };
    }

    private static readonly HashSet<string> s_postEndpoints = new(StringComparer.Ordinal)
    {
        ToolBridgeProtocol.CallEndpoint, ToolBridgeProtocol.CancelEndpoint, ToolBridgeProtocol.EndRunEndpoint, ToolBridgeProtocol.HeartbeatEndpoint,
    };

    private BridgeToolListResponse ListTools() => new()
    {
        Tools = [.. ServedTools.Select(name => new BridgeServedTool { Name = name, ContractHash = _served[name].Hash })],
        HeartbeatWindowMs = (long)_options.HeartbeatWindow.TotalMilliseconds,
    };

    private async System.Threading.Tasks.Task<(HttpStatusCode Status, object Body)> EndRunAsync(byte[] body)
    {
        if (Parse<BridgeRunRequest>(body) is not { } run || string.IsNullOrWhiteSpace(run.CallerId) || string.IsNullOrWhiteSpace(run.RunId))
            return Refuse(HttpStatusCode.BadRequest, "the body of the end of a run names a caller_id and a run_id.");
        if (_runs is { } runs)
            await runs.EndAsync(run.CallerId, run.RunId).ConfigureAwait(false);
        return (HttpStatusCode.OK, new Dictionary<string, object?>());
    }

    private (HttpStatusCode Status, object Body) Heartbeat(byte[] body)
    {
        if (Parse<BridgeHeartbeatRequest>(body) is not { } beat || string.IsNullOrWhiteSpace(beat.CallerId))
            return Refuse(HttpStatusCode.BadRequest, "the body of a heartbeat names a caller_id.");
        _runs?.Touch(beat.CallerId);
        return (HttpStatusCode.OK, new Dictionary<string, object?>());
    }

    private async System.Threading.Tasks.Task<(HttpStatusCode Status, object Body)> CallAsync(byte[] body)
    {
        if (Parse<BridgeCallRequest>(body) is not { } call || string.IsNullOrWhiteSpace(call.CallId) || call.Arguments.ValueKind != JsonValueKind.Object)
            return Refuse(HttpStatusCode.BadRequest, "the body of a call names a call_id, a tool and an object of arguments.");
        if (!_served.TryGetValue(call.Tool, out var served))
            return Refuse(HttpStatusCode.NotFound, $"the tool bridge serves no tool '{call.Tool}'.");

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        if (!_calls.TryAdd(call.CallId, cancellation))
            return Refuse(HttpStatusCode.Conflict, $"a call '{call.CallId}' is already in flight.");
        var watch = Stopwatch.StartNew();
        BridgeCallResponse answer;
        try
        {
            answer = await RunAsync(served.Tool, call, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            _calls.TryRemove(call.CallId, out _);
        }

        await AuditAsync(call.CallId, served.Tool.Name, answer.Success ? AuditOutcome.Success : AuditOutcome.Failure, watch.ElapsedMilliseconds)
            .ConfigureAwait(false);
        return (HttpStatusCode.OK, answer);
    }

    /// <summary>
    /// Runs the call in the bridge's scope of the caller's run, over the scratch of the caller's mounts when it carries
    /// them, and answers with the tool's raw response and the files it wrote.
    /// </summary>
    private async System.Threading.Tasks.Task<BridgeCallResponse> RunAsync(IBaseTool tool, BridgeCallRequest call, CancellationToken cancellationToken)
    {
        if (call.CallerId is { Length: > 0 } callerId)
            _runs?.Touch(callerId);
        var run = call is { CallerId: { Length: > 0 } caller, RunId: { Length: > 0 } runId } && _runs is { } runs
            ? runs.ScopeOf(caller, runId)
            : null;
        if (call.Mounts is not null && (_fileScope is null || _bootMounts is null))
            return Failure("the tool bridge has no file system to present the caller's mounts on.");
        if (call.Files is { } sent && sent.Sum(f => (long)f.Content.Length) > _options.MaxFileBytes)
            return Failure($"the files the call sends exceed {ToolBridgeServerOptions.SectionName}:MaxFileBytes ({ToolBridgeProtocol.Describe(_options.MaxFileBytes)}).");

        using var entered = run?.Enter();
        ToolBridgeScratch? scratch = null;
        try
        {
            if (call.Mounts is not null)
                scratch = ToolBridgeScratch.Enter(call, _fileScope!, _bootMounts!);
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

            var answer = ToWire(response);
            if (scratch is null)
                return answer;
            var written = await scratch.WrittenAsync(_options.MaxFileBytes, CancellationToken.None).ConfigureAwait(false);
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

    private (HttpStatusCode Status, object Body) Cancel(byte[] body)
    {
        if (Parse<BridgeCancelRequest>(body) is not { } cancel || string.IsNullOrWhiteSpace(cancel.CallId))
            return Refuse(HttpStatusCode.BadRequest, "the body of a cancellation names a call_id.");
        if (_calls.TryGetValue(cancel.CallId, out var call))
        {
            try
            {
                call.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The call ended meanwhile: nothing is left to cancel.
            }
        }

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

    private static async System.Threading.Tasks.Task<byte[]?> ReadBoundedAsync(Stream input, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
                return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
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
}
