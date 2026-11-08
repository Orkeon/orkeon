using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Telemetry.HealthChecks;

namespace Orkeon.Host.Api;

/// <summary>
/// The run API and the health probes of <c>orkeon-host</c> (FWK-14), on their own <see cref="HttpListener"/>:
/// <c>GET /healthz</c>, <c>GET /readyz</c> (no authentication), <c>POST /runs</c>, <c>GET /runs</c>,
/// <c>GET /runs/{id}</c>, <c>GET /runs/{id}/events</c> and <c>POST /runs/{id}/cancel</c>. Registered after the A2A server
/// and before the chat channel and the crew host: it stops after the crew host's drain, so a client keeps reading its run
/// while it finishes.
/// </summary>
internal sealed partial class HostApiServer : IHostedService, IDisposable
{
    private const int MaxBodyBytes = 1 << 20;
    private const int MaxKeyLength = 255;
    private const int RetryAfterSeconds = 5;
    private static readonly TimeSpan s_eventPoll = TimeSpan.FromMilliseconds(200);

    private static readonly JsonSerializerOptions s_write = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions s_read = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    private readonly HostApiOptions _options;
    private readonly ICrewRunner _runner;
    private readonly CrewHostRegistry _registry;
    private readonly HealthCheckService _health;
    private readonly RunIdempotencyStore _idempotency;
    private readonly IRunHistoryReader? _history;
    private readonly IEnumerable<IAuthenticationProvider> _bearerProviders;
    private readonly ISecretProvider? _secrets;
    private readonly ILogger<HostApiServer> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<int, Task> _requests = new();
    private readonly ConcurrentDictionary<int, Task> _runs = new();
    private int _sequence;
    private int _streams;
    private (HealthReport Report, long At)? _readiness;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Released with Abort(), the disposal of a listener Stop() has stopped: Close() and Dispose() would bind the port again (GAP-41).")]
    private HttpListener? _listener;
    private HttpRequestAuthenticator? _authenticator;
    private CancellationTokenSource? _stopping;
    private Task? _loop;
    private readonly IPendingInputStore? _input;
    private readonly IPendingInputBroker? _broker;

    public HostApiServer(
        IOptions<OrkeonHostOptions> options,
        ICrewRunner runner,
        CrewHostRegistry registry,
        HealthCheckService health,
        RunIdempotencyStore idempotency,
        ILogger<HostApiServer> logger,
        IRunHistoryReader? history = null,
        IEnumerable<IAuthenticationProvider>? bearerProviders = null,
        ISecretProvider? secrets = null,
        TimeProvider? time = null,
        Orkeon.Application.Interfaces.Checkpointing.ICheckpointManager? checkpoints = null,
        IPendingInputBroker? broker = null,
        Microsoft.Extensions.Options.IOptions<Orkeon.Infrastructure.Checkpointing.CheckpointingOptions>? checkpointing = null)
    {
        // A run's questions are persisted only with Orkeon:Checkpointing:Enabled (the durable human input engages on it): the
        // checkpoint manager is registered either way, so its presence alone says nothing.
        _input = checkpointing?.Value.Enabled == true ? checkpoints as IPendingInputStore : null;
        _broker = broker;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.Api;
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _history = history;
        _bearerProviders = bearerProviders ?? [];
        _secrets = secrets;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Whether the listener is up.</summary>
    public bool IsListening => _listener?.IsListening == true;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        _options.Validate();
        if (!_options.ListensOnLoopbackOnly && _options.Security.AllowedAuthSchemes.Count == 0 && !_options.Security.RequireMutualTls)
        {
            throw new HostConfigurationException(
                $"{HostApiOptions.SectionName}:Prefix is '{_options.Prefix}', beyond the loopback interface, and the API section "
                + $"declares no authentication: set {HostApiOptions.SectionName}:Security:AllowedAuthSchemes (ApiKey, Bearer) "
                + $"or {HostApiOptions.SectionName}:Security:RequireMutualTls, or listen on http://localhost.");
        }

        _authenticator = new HttpRequestAuthenticator(
            _options.Security, _bearerProviders, _secrets, _logger, sectionName: HostApiOptions.SectionName + ":Security");
        try
        {
            await _authenticator.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new HostConfigurationException($"The host API refused its configuration: {ex.Message}", ex);
        }

        // The keys the history remembers, read once: memory is the authority afterwards.
        if (_history is not null)
        {
            try
            {
                var recovered = await _idempotency.HydrateAsync(_history, cancellationToken).ConfigureAwait(false);
                LogKeysRecovered(recovered);
            }
#pragma warning disable CA1031 // An unreadable history costs the keys of before the restart, not the start.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogKeysNotRecovered(ex);
            }
        }

        var listener = new HttpListener();
        listener.Prefixes.Add(_options.Prefix);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            listener.Abort();
            throw new HostConfigurationException(
                $"The host API could not listen on '{_options.Prefix}': {ex.Message}. Another process holds the port, or, under Windows, "
                + "the URL is not reserved for the service's account (netsh http add urlacl).", ex);
        }

        _listener = listener;
        _stopping = new CancellationTokenSource();
        _loop = Task.Run(() => ListenAsync(listener, _stopping.Token), CancellationToken.None);
        LogApiStarted(_options.Prefix);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
            return;

        if (_stopping is not null)
            await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException)
            {
                // The listener closing: expected.
            }
        }

        await Task.WhenAll(_requests.Values).WaitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(_runs.Values).WaitAsync(cancellationToken).ConfigureAwait(false);
        _listener.Abort();
        _listener = null;
        _stopping?.Dispose();
        _stopping = null;
        _authenticator?.Dispose();
        LogApiStopped();
    }

    private async Task ListenAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            var id = Interlocked.Increment(ref _sequence);
            _requests[id] = Task.Run(async () =>
            {
                try
                {
                    await HandleAsync(context, ct).ConfigureAwait(false);
                }
                finally
                {
                    _requests.TryRemove(id, out _);
                }
            }, CancellationToken.None);
        }
    }

#pragma warning disable CA1031 // Each request is its own fault barrier: a handler's failure answers 500, never takes the listener down.
    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            await RouteAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host is stopping, or the client left.
            LogClientGone(context.Request.HttpMethod);
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException)
        {
            // A client that disconnected while it was being written to: nothing to answer, nothing to alarm.
            LogClientGone(context.Request.HttpMethod);
        }
        catch (Exception ex)
        {
            LogRequestFailed(ex, context.Request.HttpMethod);
            await TryWriteAsync(context, 500, new ApiErrorDto { Error = "internal_error", Message = "The request failed. Details are in the host log." }).ConfigureAwait(false);
        }
        finally
        {
            try { context.Response.Close(); }
            catch (Exception) { /* the client is gone */ }
        }
    }
#pragma warning restore CA1031

    private async Task RouteAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var segments = (request.Url?.AbsolutePath ?? "/").Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var method = request.HttpMethod;

        // Without any authentication the loopback is the only protection (FWK final review H8): a web page the operator
        // opens must not drive the daemon, neither by a cross-site form (no JSON content type) nor through a DNS name
        // rebound to 127.0.0.1 (a Host the listener does not name).
        if (_options.IsUnauthenticated)
        {
            if (!_options.AcceptsHost(request.Headers["Host"]))
            {
                await WriteAsync(context, 421, new ApiErrorDto { Error = "misdirected_request", Message = "The Host header does not name this listener." }).ConfigureAwait(false);
                return;
            }

            if (method == "POST" && !IsJson(request.ContentType))
            {
                await WriteAsync(context, 415, new ApiErrorDto { Error = "unsupported_media_type", Message = "A POST must declare Content-Type: application/json." }).ConfigureAwait(false);
                return;
            }
        }

        // Probes first, and without authentication: an orchestrator holds no credential.
        if (segments is ["healthz"] && method == "GET")
        {
            await ProbeAsync(context, HealthCheckExtensions.LiveTag, includeReadinessTags: false, ct).ConfigureAwait(false);
            return;
        }

        if (segments is ["readyz"] && method == "GET")
        {
            await ProbeAsync(context, HealthCheckExtensions.ReadyTag, includeReadinessTags: true, ct).ConfigureAwait(false);
            return;
        }

        if (segments is not ["runs", ..])
        {
            await WriteAsync(context, 404, new ApiErrorDto { Error = "not_found", Message = "No such route." }).ConfigureAwait(false);
            return;
        }

        var verdict = await _authenticator!.AuthorizeAsync(context, ct).ConfigureAwait(false);
        if (!verdict.Allowed)
        {
            LogUnauthorized(verdict.Reason ?? "rejected");
            await WriteAsync(context, verdict.StatusCode, new ApiErrorDto { Error = verdict.StatusCode == 401 ? "unauthorized" : "forbidden", Message = verdict.Error ?? "Refused." }).ConfigureAwait(false);
            return;
        }

        switch (segments, method)
        {
            case (["runs"], "POST"):
                await StartRunAsync(context, ct).ConfigureAwait(false);
                break;
            case (["runs"], "GET"):
                await ListRunsAsync(context, ct).ConfigureAwait(false);
                break;
            case (["runs", var id], "GET"):
                await GetRunAsync(context, id, ct).ConfigureAwait(false);
                break;
            case (["runs", var id, "events"], "GET"):
                await StreamEventsAsync(context, id, ct).ConfigureAwait(false);
                break;
            case (["runs", var id, "input"], "POST"):
                await ProvideInputAsync(context, id, ct).ConfigureAwait(false);
                break;
            case (["runs", var id, "cancel"], "POST"):
                await CancelRunAsync(context, id, ct).ConfigureAwait(false);
                break;
            default:
                await WriteAsync(context, 405, new ApiErrorDto { Error = "method_not_allowed", Message = "No such route for this method." }).ConfigureAwait(false);
                break;
        }
    }

    private static bool IsJson(string? contentType)
    {
        var type = contentType?.Trim();
        return type is not null
            && type.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            && (type.Length == 16 || type[16] is ';' or ' ');
    }

    private async Task ProbeAsync(HttpListenerContext context, string tag, bool includeReadinessTags, CancellationToken ct)
    {
        var extra = includeReadinessTags ? _options.ReadinessTags : [];
        var cached = includeReadinessTags && _options.ReadinessCacheTtl > TimeSpan.Zero;
        HealthReport report;
        if (cached && _readiness is { } last && _time.GetElapsedTime(last.At) < _options.ReadinessCacheTtl)
        {
            report = last.Report;
        }
        else
        {
            report = await _health.CheckHealthAsync(
                registration => registration.Tags.Contains(tag) || registration.Tags.Intersect(extra, StringComparer.OrdinalIgnoreCase).Any(), ct)
                .ConfigureAwait(false);
            if (cached)
                _readiness = (report, _time.GetTimestamp());
        }

        var stopping = _registry.AdmissionClosed;
        var status = stopping ? "stopping" : StatusName(report.Status);
        var code = stopping || report.Status == HealthStatus.Unhealthy ? 503 : 200;
        var checks = report.Entries
            .Select(e => new ProbeCheckDto { Name = e.Key, Status = StatusName(e.Value.Status) })
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
        await WriteAsync(context, code, new ProbeDto { Status = status, Checks = checks }).ConfigureAwait(false);
    }

    private async Task StartRunAsync(HttpListenerContext context, CancellationToken ct)
    {
        var body = await ReadBodyAsync(context.Request, ct).ConfigureAwait(false);
        if (body is null)
        {
            await WriteAsync(context, 413, new ApiErrorDto { Error = "payload_too_large", Message = $"The body is larger than {MaxBodyBytes} bytes." }).ConfigureAwait(false);
            return;
        }

        StartRunRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<StartRunRequest>(body, s_read);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Crew) || string.IsNullOrEmpty(request.Prompt))
        {
            await WriteAsync(context, 400, new ApiErrorDto
            {
                Error = "invalid_request",
                Message = "The body must be a JSON object with a crew, a prompt, optional string variables and an optional idempotency_key.",
            }).ConfigureAwait(false);
            return;
        }

        if (request.IdempotencyKey is { } key && (key.Length == 0 || key.Length > MaxKeyLength))
        {
            await WriteAsync(context, 400, new ApiErrorDto { Error = "invalid_request", Message = $"idempotency_key must hold 1 to {MaxKeyLength} characters." }).ConfigureAwait(false);
            return;
        }

        var hosted = _registry.Find(request.Crew);
        if (hosted is null)
        {
            await WriteAsync(context, 404, new ApiErrorDto { Error = "unknown_crew", Message = $"No hosted crew named '{request.Crew}'." }).ConfigureAwait(false);
            return;
        }

        var variables = request.Variables is { Count: > 0 } ? request.Variables : null;
        var hash = HashRequest(hosted.Name, request.Prompt, variables);

        IdempotentStart result;
        if (request.IdempotencyKey is null)
        {
            result = new IdempotentStart(false, false, await StartAsync(hosted.Name, request.Prompt, variables, null).ConfigureAwait(false));
        }
        else
        {
            var idempotency = new RunIdempotency(request.IdempotencyKey, hash);
            result = await _idempotency.StartOnceAsync(
                request.IdempotencyKey, hash, () => StartAsync(hosted.Name, request.Prompt, variables, idempotency)).ConfigureAwait(false);
        }

        if (result.Conflict)
        {
            await WriteAsync(context, 409, new ApiErrorDto { Error = "idempotency_conflict", Message = "This idempotency_key was used with a different request." }).ConfigureAwait(false);
            return;
        }

        var outcome = result.Outcome!;
        if (outcome.Kind == StartKind.Started)
        {
            await WriteAsync(
                context,
                result.Replay ? 200 : 202,
                new RunAcceptedDto { RunId = outcome.RunId!, IdempotentReplay = result.Replay }).ConfigureAwait(false);
            return;
        }

        await WriteRefusalAsync(context, outcome.Refusal!).ConfigureAwait(false);
    }

    private static async Task WriteRefusalAsync(HttpListenerContext context, HostedRunResult refusal)
    {
        switch (refusal.Outcome)
        {
            case HostedRunOutcome.UnknownCrew:
                await WriteAsync(context, 404, new ApiErrorDto { Error = "unknown_crew", Message = refusal.Message }).ConfigureAwait(false);
                break;
            case HostedRunOutcome.Busy:
                context.Response.Headers["Retry-After"] = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                await WriteAsync(context, 429, new ApiErrorDto { Error = "at_limit", Message = refusal.Message }).ConfigureAwait(false);
                break;
            case HostedRunOutcome.HostStopping:
                await WriteAsync(context, 503, new ApiErrorDto { Error = "host_stopping", Message = refusal.Message }).ConfigureAwait(false);
                break;
            default:
                await WriteAsync(context, 500, new ApiErrorDto { Error = "internal_error", Message = refusal.Message }).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Launches the run and returns as soon as its slot is reserved (<c>onStarted</c>), or as soon as the runner refused it.
    /// The run itself goes on in the background; the registry's drain, not this task, bounds it at shutdown.
    /// </summary>
    private async Task<StartOutcome> StartAsync(string crew, string prompt, IReadOnlyDictionary<string, string>? variables, RunIdempotency? idempotency)
    {
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = _runner.RunAsync(
            crew,
            prompt,
            "api",
            onStarted: id =>
            {
                started.TrySetResult(id);
                return Task.CompletedTask;
            },
            variables: variables,
            idempotency: idempotency);

        await Task.WhenAny(started.Task, run).ConfigureAwait(false);
        if (started.Task.IsCompletedSuccessfully)
        {
            var key = Interlocked.Increment(ref _sequence);
            _runs[key] = ObserveAsync(run, key);
            return StartOutcome.Started(await started.Task.ConfigureAwait(false));
        }

        return StartOutcome.Refused(await run.ConfigureAwait(false));
    }

#pragma warning disable CA1031 // The run's own fault barrier lives in CrewRunner; this only keeps the task observed.
    private async Task ObserveAsync(Task<HostedRunResult> run, int key)
    {
        try
        {
            await run.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogRunFaulted(ex);
        }
        finally
        {
            _runs.TryRemove(key, out _);
        }
    }
#pragma warning restore CA1031

    private async Task ListRunsAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (_history is null)
        {
            await WriteHistoryOffAsync(context).ConfigureAwait(false);
            return;
        }

        var query = context.Request.QueryString;
        var limit = int.TryParse(query["limit"], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 1, 500) : 50;
        var runs = await _history.ListAsync(new RunQuery { CrewKey = query["crew"], Limit = limit }, ct).ConfigureAwait(false);
        await WriteAsync(context, 200, new RunListDto { Runs = [.. runs.Select(RunDto.From)] }).ConfigureAwait(false);
    }

    private async Task GetRunAsync(HttpListenerContext context, string id, CancellationToken ct)
    {
        var run = await FindRunAsync(id, ct).ConfigureAwait(false);
        if (run is null)
        {
            await (_history is null && !IsRunning(id) ? WriteHistoryOffAsync(context) : WriteNoSuchRunAsync(context)).ConfigureAwait(false);
            return;
        }

        await WriteAsync(context, 200, run with { PendingInput = await PendingOfAsync(id, ct).ConfigureAwait(false) }).ConfigureAwait(false);
    }

    // The question the run holds on or stopped on (FWK-23), read where the run persisted it.
    private async Task<PendingInputDto?> PendingOfAsync(string id, CancellationToken ct)
    {
        if (_input is null || await _input.GetPendingAsync(id, ct).ConfigureAwait(false) is not { } pending)
            return null;

        return new PendingInputDto
        {
            RequestId = pending.RequestId,
            Kind = pending.Kind switch
            {
                PendingInputKind.Confirm => "confirm",
                PendingInputKind.Choice => "choice",
                PendingInputKind.MultiChoice => "multi_choice",
                PendingInputKind.ToolApproval => "tool_approval",
                _ => "text",
            },
            // The stored prompt stays as it was asked, the resume needs it; what is read back is masked as the event sink masks.
            Prompt = Orkeon.Infrastructure.Runs.RedactingRunEventSink.RedactText(pending.Prompt),
            Choices = pending.Choices.Count > 0 ? [.. pending.Choices.Select(Orkeon.Infrastructure.Runs.RedactingRunEventSink.RedactText)] : null,
        };
    }

    /// <summary>
    /// <c>POST /runs/{id}/input</c> (FWK-23): hands the answer to the run that holds on the question in this process, which
    /// goes on (200); a run that was suspended has the answer recorded and is resumed with <c>orkeon run --resume</c> (202).
    /// The first answer wins: the next one is a 409.
    /// </summary>
    private async Task ProvideInputAsync(HttpListenerContext context, string id, CancellationToken ct)
    {
        if (_input is null || _broker is null)
        {
            await WriteAsync(context, 409, new ApiErrorDto { Error = "input_unavailable", Message = "This host keeps no checkpoints (Orkeon:Checkpointing:Enabled), so no run can wait for an answer." }).ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context.Request, ct).ConfigureAwait(false);
        if (body is null)
        {
            await WriteAsync(context, 413, new ApiErrorDto { Error = "payload_too_large", Message = $"The body is larger than {MaxBodyBytes} bytes." }).ConfigureAwait(false);
            return;
        }

        ProvideInputRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ProvideInputRequest>(body, s_read);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request?.Answer is null)
        {
            await WriteAsync(context, 400, new ApiErrorDto { Error = "invalid_request", Message = "The body must be a JSON object with an answer and an optional request_id." }).ConfigureAwait(false);
            return;
        }

        var heldIds = _broker.HeldRequestIds(id);
        if (request.RequestId is null && heldIds.Count > 1)
        {
            // Several questions are held (parallel agents): an answer without a request_id would go to an arbitrary one.
            await WriteAsync(context, 409, new ApiErrorDto
            {
                Error = "request_id_required",
                Message = $"The run holds {heldIds.Count} questions: name the one you answer with request_id ({string.Join(", ", heldIds)}).",
            }).ConfigureAwait(false);
            return;
        }

        var held = request.RequestId is null
            ? heldIds.Count == 1 ? heldIds[0] : null
            : heldIds.FirstOrDefault(h => string.Equals(h, request.RequestId, StringComparison.Ordinal));
        if (heldIds.Count > 0 && held is null)
        {
            await WriteAsync(context, 409, new ApiErrorDto { Error = "request_mismatch", Message = "The run waits on another question." }).ConfigureAwait(false);
            return;
        }

        if (held is not null)
        {
            if (_broker.TryComplete(id, held, request.Answer))
            {
                await WriteAsync(context, 200, new InputAcceptedDto { RunId = id, RequestId = held, Status = "delivered" }).ConfigureAwait(false);
                return;
            }
        }

        var answered = await _input.AnswerAsync(id, request.RequestId, request.Answer, ct).ConfigureAwait(false);
        switch (answered.Outcome)
        {
            case PendingInputAnswerOutcome.Accepted:
                await WriteAsync(context, 202, new InputAcceptedDto { RunId = id, RequestId = answered.RequestId!, Status = "recorded" }).ConfigureAwait(false);
                break;
            case PendingInputAnswerOutcome.AlreadyAnswered:
                // The same answer given again before the resume (a retried POST): the same fact, so the same success.
                await WriteAsync(context, 200, new InputAcceptedDto { RunId = id, RequestId = answered.RequestId!, Status = "already_answered" }).ConfigureAwait(false);
                break;
            case PendingInputAnswerOutcome.UnknownSession:
                await WriteNoSuchRunAsync(context).ConfigureAwait(false);
                break;
            case PendingInputAnswerOutcome.Mismatch:
                await WriteAsync(context, 409, new ApiErrorDto { Error = "request_mismatch", Message = "The run waits on another question." }).ConfigureAwait(false);
                break;
            case PendingInputAnswerOutcome.RequestIdRequired:
                await WriteAsync(context, 409, new ApiErrorDto { Error = "request_id_required", Message = "The run waits on several questions: name the one you answer with request_id." }).ConfigureAwait(false);
                break;
            default:
                await WriteAsync(context, 409, new ApiErrorDto { Error = "no_pending_input", Message = "The run waits for no answer: it asked nothing, or the question was already answered." }).ConfigureAwait(false);
                break;
        }
    }

    // The history first; a run admitted a moment ago has no run.started yet, and the registry knows it.
    private async Task<RunDto?> FindRunAsync(string id, CancellationToken ct)
    {
        var summary = _history is null ? null : await _history.GetAsync(id, ct).ConfigureAwait(false);
        if (summary is not null)
            return RunDto.From(summary);

        var live = _registry.Running.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        return live is null
            ? null
            : new RunDto { RunId = live.Id, Crew = live.CrewName, Status = "running", StartedAt = live.StartedAt };
    }

    private bool IsRunning(string id) => _registry.Running.Any(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    private async Task CancelRunAsync(HttpListenerContext context, string id, CancellationToken ct)
    {
        var live = _registry.Running.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        if (live is not null && _registry.RequestStop(live.Id))
        {
            await WriteAsync(context, 202, new RunAcceptedDto { RunId = live.Id }).ConfigureAwait(false);
            return;
        }

        var known = await FindRunAsync(id, ct).ConfigureAwait(false);
        await (known is null
            ? WriteNoSuchRunAsync(context)
            : WriteAsync(context, 409, new ApiErrorDto { Error = "run_not_running", Message = "The run is not running: there is nothing to cancel." })).ConfigureAwait(false);
    }

    private async Task StreamEventsAsync(HttpListenerContext context, string id, CancellationToken ct)
    {
        if (_history is null)
        {
            await WriteHistoryOffAsync(context).ConfigureAwait(false);
            return;
        }

        if (await FindRunAsync(id, ct).ConfigureAwait(false) is null)
        {
            await WriteNoSuchRunAsync(context).ConfigureAwait(false);
            return;
        }

        // A stream holds a connection and polls the history: beyond the cap the answer is a structured 503 (FWK final review H18).
        if (Interlocked.Increment(ref _streams) > _options.MaxEventStreams)
        {
            Interlocked.Decrement(ref _streams);
            context.Response.Headers["Retry-After"] = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            await WriteAsync(context, 503, new ApiErrorDto { Error = "too_many_streams", Message = $"The host serves {_options.MaxEventStreams} event streams already; try again shortly." }).ConfigureAwait(false);
            return;
        }

        try
        {
            await ServeStreamAsync(context, id, ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _streams);
        }
    }

    private async Task ServeStreamAsync(HttpListenerContext context, string id, CancellationToken ct)
    {
        var history = _history!;
        // Last-Event-ID is the seq the client last received: it resumes after it. ?from= is inclusive, as the history reads.
        var next = 1;
        var lastEventId = context.Request.Headers["Last-Event-ID"];
        if (int.TryParse(lastEventId, NumberStyles.None, CultureInfo.InvariantCulture, out var last))
            next = last + 1;
        else if (int.TryParse(context.Request.QueryString["from"], NumberStyles.None, CultureInfo.InvariantCulture, out var from) && from >= 1)
            next = from;

        var sse = (context.Request.Headers["Accept"] ?? string.Empty).Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = sse ? "text/event-stream; charset=utf-8" : "application/x-ndjson; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.SendChunked = true;

        var lastWrite = _time.GetTimestamp();
        while (!ct.IsCancellationRequested)
        {
            var events = await history.ReadEventsAsync(id, next, ct).ConfigureAwait(false);
            var finished = false;
            foreach (var runEvent in events)
            {
                var wire = RunEventWire.Serialize(runEvent);
                var frame = sse
                    ? $"id: {runEvent.Seq.ToString(CultureInfo.InvariantCulture)}\nevent: {runEvent.Kind}\ndata: {wire}\n\n"
                    : wire + "\n";
                var bytes = Encoding.UTF8.GetBytes(frame);
                await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
                next = runEvent.Seq + 1;
                finished = runEvent.Kind == RunEventKinds.RunFinished;
                lastWrite = _time.GetTimestamp();
            }

            // Quiet for a keep-alive interval: write a frame no client reads as an event. A client that left makes this
            // write throw, which ends the stream (HandleAsync) instead of leaving it to live as long as the run.
            if (!finished && _time.GetElapsedTime(lastWrite) >= _options.SseKeepAliveInterval)
            {
                await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(sse ? ": keep-alive\n\n" : "\n"), ct).ConfigureAwait(false);
                lastWrite = _time.GetTimestamp();
            }

            await response.OutputStream.FlushAsync(ct).ConfigureAwait(false);
            if (finished)
                return;

            // Nothing new: stop when the run is over without a run.finished (a killed process), else wait.
            if (events.Count == 0 && !IsRunning(id) && await history.GetAsync(id, ct).ConfigureAwait(false) is { Status: not RunStatus.Running })
                return;

            await Task.Delay(s_eventPoll, _time, ct).ConfigureAwait(false);
        }
    }

    private static string StatusName(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "healthy",
        HealthStatus.Degraded => "degraded",
        _ => "unhealthy",
    };

    /// <summary>Releases what a stop that never came would leave: the listener's resources.</summary>
    public void Dispose()
    {
        _listener?.Abort();
        _stopping?.Dispose();
        _authenticator?.Dispose();
    }

    private static string HashRequest(string crew, string prompt, IReadOnlyDictionary<string, string>? variables)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            crew,
            prompt,
            variables = variables?.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => new[] { v.Key, v.Value }).ToArray(),
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpListenerRequest request, CancellationToken ct)
    {
        if (request.ContentLength64 > MaxBodyBytes)
            return null;

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.InputStream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private static Task WriteHistoryOffAsync(HttpListenerContext context) =>
        WriteAsync(context, 501, new ApiErrorDto { Error = "run_history_disabled", Message = "The run history is off: reading runs needs Orkeon:RunHistory:Enabled." });

    private static Task WriteNoSuchRunAsync(HttpListenerContext context) =>
        WriteAsync(context, 404, new ApiErrorDto { Error = "unknown_run", Message = "No such run." });

    private static async Task WriteAsync(HttpListenerContext context, int status, object body)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), s_write);
        var response = context.Response;
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }

#pragma warning disable CA1031 // Best effort: the client may be gone.
    private static async Task TryWriteAsync(HttpListenerContext context, int status, object body)
    {
        try
        {
            await WriteAsync(context, status, body).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Nothing more to do.
        }
    }
#pragma warning restore CA1031

    [LoggerMessage(Level = LogLevel.Information, Message = "Host API listening on {Prefix}")]
    private partial void LogApiStarted(string prefix);

    [LoggerMessage(Level = LogLevel.Information, Message = "Host API stopped")]
    private partial void LogApiStopped();

    [LoggerMessage(Level = LogLevel.Information, Message = "Host API: {Count} idempotency key(s) recovered from the run history")]
    private partial void LogKeysRecovered(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Host API: the run history could not be read; the idempotency keys of before the restart are forgotten")]
    private partial void LogKeysNotRecovered(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Host API: request refused: {Reason}")]
    private partial void LogUnauthorized(string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Host API: a {Method} request failed")]
    private partial void LogRequestFailed(Exception ex, string method);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Host API: a {Method} request ended: the client went away or the host is stopping")]
    private partial void LogClientGone(string method);

    [LoggerMessage(Level = LogLevel.Error, Message = "Host API: a run task faulted")]
    private partial void LogRunFaulted(Exception ex);
}
