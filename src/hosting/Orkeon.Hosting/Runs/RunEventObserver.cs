using System.Diagnostics.CodeAnalysis;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;

namespace Orkeon.Hosting.Runs;

/// <summary>
/// Projects a crew run onto the shared event stream: task completions from
/// <see cref="ICrewExecutionHook"/>, the token meter from <see cref="ILlmUsageSink"/>, and —
/// only when <c>--stream</c> asked for it — generation deltas from <see cref="ILlmDeltaSink"/>:
/// the crew's agent turns (GAP-32) and the <c>ctx.llm.*</c> calls of a script.
/// <para>
/// <b>It composes, it does not replace.</b> <c>ICrewExecutionHook</c> is a single service and
/// <c>RunnerExecution</c> already registers <c>AutoSummaryWriter</c> on it whenever an
/// <c>/output:rw</c> mount exists. Registering another one would silently drop AUTO_SUMMARY.md
/// on every observed run, so this observer takes the previously registered hook and forwards
/// every callback to it. An inner hook that throws never breaks the stream, and vice versa:
/// observing a run must not change its outcome.
/// </para>
/// </summary>
internal sealed class RunEventObserver : ICrewExecutionHook, ILlmUsageSink, ILlmDeltaSink, Orkeon.Domain.Graph.IGraphNodeObserver
{
    private readonly IRunEventSink _events;
    private readonly ICrewExecutionHook? _inner;
    private readonly bool _stream;
    private readonly Lock _gate = new();
    private long _tokens;
    private long _promptTokens;
    private long _completionTokens;
    private long _cacheHitTokens;
    private long _cacheMissTokens;
    private bool _cacheMeasured;
    private long _estimatedTokens;
    private decimal? _vendorCost;
    private string? _vendorCostCurrency;
    private string? _pendingRequestId;

    /// <summary>Builds the observer over the stream, the hook it must not displace, and the streaming flag.</summary>
    public RunEventObserver(IRunEventSink events, ICrewExecutionHook? inner, bool stream)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _inner = inner;
        _stream = stream;
    }

    /// <summary>The question the run is waiting on, for the closing event (FWK-23); null once it is answered.</summary>
    public string? PendingRequestId
    {
        get { lock (_gate) { return _pendingRequestId; } }
    }

    /// <inheritdoc />
    public Task OnHumanInputAsync(HumanInputNotice notice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var request = notice.Request;
        var scope = new RunEventScope { CorrelationId = request.RequestId };
        if (notice.Kind == HumanInputNoticeKind.Needed)
        {
            lock (_gate)
                _pendingRequestId = request.RequestId;
            _events.Emit(
                RunEventKinds.InputNeeded,
                scope,
                new
                {
                    requestId = request.RequestId,
                    inputKind = request.Kind switch
                    {
                        PendingInputKind.Confirm => "confirm",
                        PendingInputKind.Choice => "choice",
                        PendingInputKind.MultiChoice => "multi",
                        PendingInputKind.ToolApproval => "tool_approval",
                        _ => "text",
                    },
                    prompt = request.Prompt,
                    choices = request.Choices.Count > 0 ? request.Choices : null,
                    taskKey = request.TaskKey,
                    toolName = request.ToolName,
                    suspending = notice.Suspending ? true : (bool?)null,
                });
        }
        else
        {
            lock (_gate)
                _pendingRequestId = null;
            _events.Emit(RunEventKinds.InputGiven, scope, new { requestId = request.RequestId });
        }

        return _inner?.OnHumanInputAsync(notice, ct) ?? Task.CompletedTask;
    }

    /// <summary>Cumulative tokens seen on the meter, for the closing event.</summary>
    public long TokensUsed
    {
        get { lock (_gate) { return _tokens; } }
    }

    /// <summary>Cumulative prompt-side tokens, for the closing event (W-08).</summary>
    public long PromptTokens
    {
        get { lock (_gate) { return _promptTokens; } }
    }

    /// <summary>Cumulative completion-side tokens, for the closing event (W-08).</summary>
    public long CompletionTokens
    {
        get { lock (_gate) { return _completionTokens; } }
    }

    /// <summary>
    /// Cumulative cache-served prompt tokens, or null when no provider reported cache
    /// telemetry — "not measured" is never presented as a zero (W-08).
    /// </summary>
    public long? CacheHitTokens
    {
        get { lock (_gate) { return _cacheMeasured ? _cacheHitTokens : null; } }
    }

    /// <summary>Cumulative cache-missed prompt tokens, null when unmeasured (W-08).</summary>
    public long? CacheMissTokens
    {
        get { lock (_gate) { return _cacheMeasured ? _cacheMissTokens : null; } }
    }

    /// <summary>
    /// How much of <see cref="TokensUsed"/> the runtime estimated because a provider counted
    /// nothing, or null when every call was counted by its provider — a
    /// figure a screen must mark as approximate rather than pass off as a count.
    /// </summary>
    public long? EstimatedTokens
    {
        get { lock (_gate) { return _estimatedTokens > 0 ? _estimatedTokens : null; } }
    }

    /// <summary>
    /// A graph engine, a crew's or a script's, is about to run a node: <c>node</c> and <c>visit</c>, and
    /// <c>task</c> or <c>graph</c> when the engine has one to name.
    /// </summary>
    public void NodeEntered(Orkeon.Domain.Graph.GraphNodeEntered entered)
    {
        ArgumentNullException.ThrowIfNull(entered);
        _events.Emit(
            RunEventKinds.GraphNodeEntered,
            new
            {
                node = entered.Node,
                visit = entered.Visit,
                task = entered.Task,
                graph = entered.Graph,
            });
    }

    /// <inheritdoc />
    public async Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Which task and whose turn — nothing else is known yet, and the stream says only
        // what it knows. Between this line and the matching task.completed a watcher shows
        // the task as in progress.
        _events.Emit(
            RunEventKinds.TaskStarted,
            new RunEventScope { AgentId = snapshot.AgentRole },
            new
            {
                taskId = snapshot.TaskId,
                agentRole = snapshot.AgentRole,
                attempt = snapshot.Attempt,
            });

        if (_inner is not null)
            await _inner.OnTaskStartedAsync(snapshot, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _events.Emit(
            RunEventKinds.TaskCompleted,
            new RunEventScope { AgentId = snapshot.AgentRole },
            new
            {
                taskId = snapshot.TaskId,
                agentRole = snapshot.AgentRole,
                success = snapshot.Success,
                skipped = snapshot.Skipped,
                resumed = snapshot.Resumed,
                attempt = snapshot.Attempt,
                durationMs = (long)snapshot.Duration.TotalMilliseconds,
                tokens = snapshot.TokensUsed,
                toolCalls = snapshot.ToolCallCount,
                skipReason = snapshot.SkipReason,
                defaulted = snapshot.Defaulted,
                isDecision = snapshot.IsDecision,
                decision = snapshot.DecisionAnswers,
            });

        if (_inner is not null)
            await _inner.OnTaskCompletedAsync(snapshot, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct) =>
        _inner?.OnCrewCompletedAsync(snapshot, ct) ?? Task.CompletedTask;

    /// <inheritdoc />
    public Task OnCrewFailedAsync(CrewExecutionSnapshot isPartial, Exception? ex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(isPartial);

        // The strategies' terminal dispatch has to reach the WIRE, not just AUTO_SUMMARY.md:
        // without this line the `error` kind had no producer on the run path at all, and
        // a client's error surface was unreachable for the exact events built to feed it.
        _events.Emit(
            RunEventKinds.Error,
            new RunEventScope { CrewId = Blank(isPartial.CrewId) },
            new
            {
                code = isPartial.Status == CrewHookStatus.Canceled
                    ? Orkeon.Constants.Protocol.RunEventErrorCodes.CrewCancelled
                    : Orkeon.Constants.Protocol.RunEventErrorCodes.CrewFailed,
                message = isPartial.FailureReason ?? ex?.Message ?? string.Empty,
                recoverable = false,
            });

        return _inner?.OnCrewFailedAsync(isPartial, ex, ct) ?? Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every reading carries the run's standing meter, cumulative like <c>run.finished</c>: both
    /// directions at every call,
    /// the cache pair once a provider measured it, the part the runtime had to estimate once a
    /// provider counted nothing, and the vendor's own charge once a vendor billed one. Every
    /// generation call of the run reaches here — the agents', the manager's, the planner's, the
    /// RAG pipelines', the memory services' — through the one metered provider, and
    /// each reading says which kind of work its own call was.
    /// </remarks>
    public void Record(CostUsageEvent usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        long total, prompt, completion;
        long? cacheHit, cacheMiss, estimated;
        decimal? cost;
        string? currency;
        lock (_gate)
        {
            _tokens += usage.PromptTokens + usage.CompletionTokens;
            _promptTokens += usage.PromptTokens;
            _completionTokens += usage.CompletionTokens;

            // The provider counted nothing for this one: the figure is the runtime's own.
            if (usage.Estimated)
                _estimatedTokens += usage.PromptTokens + usage.CompletionTokens;

            // The cache pair partitions the prompt tokens (never additive); one measured
            // call is enough to call the run "measured".
            if (usage.CacheHitTokens is { } hit)
            {
                _cacheHitTokens += hit;
                _cacheMeasured = true;
            }
            if (usage.CacheMissTokens is { } miss)
            {
                _cacheMissTokens += miss;
                _cacheMeasured = true;
            }

            // The vendor's charge, summed as billed — a 0 is a free call, not a missing one.
            if (usage.Cost is { } charged)
            {
                _vendorCost = (_vendorCost ?? 0m) + charged;
                _vendorCostCurrency = usage.CostCurrency ?? _vendorCostCurrency;
            }

            total = _tokens;
            prompt = _promptTokens;
            completion = _completionTokens;
            cacheHit = _cacheMeasured ? _cacheHitTokens : null;
            cacheMiss = _cacheMeasured ? _cacheMissTokens : null;
            estimated = _estimatedTokens > 0 ? _estimatedTokens : null;
            cost = _vendorCost;
            currency = _vendorCostCurrency;
        }

        // The price is the vendor's own figure or nothing (DD-1): the framework's price
        // registry estimates for budgets, and an estimate on the wire would read as a bill.
        // A null field is omitted by the writer, so a run no vendor billed shows no price.
        _events.Emit(
            RunEventKinds.CostUpdated,
            new RunEventScope { CrewId = Blank(usage.CrewId), AgentId = Blank(usage.AgentId) },
            new
            {
                tokens = total,
                promptTokens = prompt,
                completionTokens = completion,
                cacheHitTokens = cacheHit,
                cacheMissTokens = cacheMiss,
                estimatedTokens = estimated,
                model = Blank(usage.Model),
                provider = Blank(usage.Provider),
                operation = Operation(usage.OperationType),
                cost,
                currency,
                costSource = cost is null ? null : VendorCostSource,
            });
    }

    /// <summary>
    /// The one value of <c>costSource</c>: the charge is what the vendor billed. The field
    /// exists so a price never has to be taken on faith — and DD-1 keeps it the only source.
    /// </summary>
    private const string VendorCostSource = "vendor";

    /// <summary>
    /// What an event built without a kind of work carries — <see cref="CostUsageEvent"/>'s own
    /// default, read off a fresh instance so the two cannot drift apart.
    /// </summary>
    private static readonly string UnnamedOperation = new CostUsageEvent().OperationType;

    /// <summary>
    /// The kind of work a reading measured, as the engine attributed its call: what
    /// lets a watcher name the model the agents work on rather than a judge's or a RAG
    /// pipeline's. A call nothing claimed names none — an unknown kind is left out, not guessed.
    /// </summary>
    private static string? Operation(string operation) =>
        string.IsNullOrEmpty(operation)
        || string.Equals(operation, LlmUsageOperations.Unattributed, StringComparison.Ordinal)
        || string.Equals(operation, UnnamedOperation, StringComparison.Ordinal)
            ? null
            : operation;

    /// <inheritdoc />
    public void OnDelta(string delta)
    {
        if (_stream && !string.IsNullOrEmpty(delta))
            _events.Emit(RunEventKinds.LlmDelta, new { text = delta, deltaKind = "content" });
    }

    /// <inheritdoc />
    public void OnToolCallDelta(Orkeon.Domain.SharedKernel.ValueObjects.ToolCallDelta toolCall)
    {
        if (!_stream)
            return;

        // The id and the name are on the fragments that carry them: a fragment without one leaves it out.
        var call = new System.Text.Json.Nodes.JsonObject { ["index"] = toolCall.Index };
        if (!string.IsNullOrEmpty(toolCall.Id))
            call["id"] = toolCall.Id;
        if (!string.IsNullOrEmpty(toolCall.Name))
            call["name"] = toolCall.Name;
        call["argumentsDelta"] = toolCall.ArgumentsDelta;
        _events.Emit(RunEventKinds.LlmDelta, new { deltaKind = "tool_call", toolCall = call });
    }

    /// <inheritdoc />
    public void OnTurnCompleted()
    {
        // Turn boundaries are already visible through task.completed; emitting a second
        // marker would only add noise to a stream that is verbose by nature.
    }

    private static string? Blank(string value) => string.IsNullOrEmpty(value) ? null : value;
}
