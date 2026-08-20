using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Commands.Run;

/// <summary>
/// Wire spellings of the run event stream (BUS-02). The Atelier's own kinds live in
/// <c>ForgeEventKinds</c>; both verbs share the envelope, not the vocabulary.
/// </summary>
internal static class RunEventKinds
{
    /// <summary>Opening event: what is about to run.</summary>
    public const string RunStarted = "run.started";

    /// <summary>One task of the crew completed — the granularity <c>ICrewExecutionHook</c> gives.</summary>
    public const string TaskCompleted = "task.completed";

    /// <summary>The token meter moved.</summary>
    public const string CostUpdated = "cost.updated";

    /// <summary>Token-by-token generation; only under <c>--stream</c>.</summary>
    public const string LlmDelta = "llm.delta";

    /// <summary>Closing event; mirrors the process exit code.</summary>
    public const string RunFinished = "run.finished";

    /// <summary>An anomaly, recoverable or not.</summary>
    public const string Error = "error";
}

/// <summary>
/// Projects a crew run onto the shared event stream: task completions from
/// <see cref="ICrewExecutionHook"/>, the token meter from <see cref="ILlmUsageSink"/>, and —
/// only when <c>--stream</c> asked for it — generation deltas from <see cref="ILlmDeltaSink"/>.
/// <para>
/// <b>It composes, it does not replace.</b> <c>ICrewExecutionHook</c> is a single service and
/// <c>RunnerExecution</c> already registers <c>AutoSummaryWriter</c> on it whenever an
/// <c>/output:rw</c> mount exists. Registering another one would silently drop AUTO_SUMMARY.md
/// on every observed run, so this observer takes the previously registered hook and forwards
/// every callback to it. An inner hook that throws never breaks the stream, and vice versa:
/// observing a run must not change its outcome.
/// </para>
/// </summary>
internal sealed class RunEventObserver : ICrewExecutionHook, ILlmUsageSink, ILlmDeltaSink
{
    private readonly OrkeonEventWriter _events;
    private readonly ICrewExecutionHook? _inner;
    private readonly bool _stream;
    private readonly Lock _gate = new();
    private long _tokens;

    /// <summary>Builds the observer over the stream, the hook it must not displace, and the streaming flag.</summary>
    public RunEventObserver(OrkeonEventWriter events, ICrewExecutionHook? inner, bool stream)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _inner = inner;
        _stream = stream;
    }

    /// <summary>Cumulative tokens seen on the meter, for the closing event.</summary>
    public long TokensUsed
    {
        get { lock (_gate) { return _tokens; } }
    }

    /// <inheritdoc />
    public async Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _events.Emit(
            RunEventKinds.TaskCompleted,
            new OrkeonEventScope { AgentId = snapshot.AgentRole },
            new
            {
                taskId = snapshot.TaskId,
                agentRole = snapshot.AgentRole,
                success = snapshot.Success,
                durationMs = (long)snapshot.Duration.TotalMilliseconds,
                tokens = snapshot.TokensUsed,
                toolCalls = snapshot.ToolCallCount,
            });

        if (_inner is not null)
            await _inner.OnTaskCompletedAsync(snapshot, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct) =>
        _inner?.OnCrewCompletedAsync(snapshot, ct) ?? Task.CompletedTask;

    /// <inheritdoc />
    public Task OnCrewFailedAsync(CrewExecutionSnapshot snapshot, Exception? ex, CancellationToken ct) =>
        _inner?.OnCrewFailedAsync(snapshot, ex, ct) ?? Task.CompletedTask;

    /// <inheritdoc />
    public void Record(CostUsageEvent usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        long total;
        lock (_gate)
        {
            _tokens += usage.PromptTokens + usage.CompletionTokens;
            total = _tokens;
        }

        // The cost in currency is deliberately absent: no price table exists in the
        // framework, and inventing one would be worse than omitting it.
        _events.Emit(
            RunEventKinds.CostUpdated,
            new OrkeonEventScope { CrewId = Blank(usage.CrewId), AgentId = Blank(usage.AgentId) },
            new { tokens = total, model = Blank(usage.Model), provider = Blank(usage.Provider) });
    }

    /// <inheritdoc />
    public void OnDelta(string delta)
    {
        if (_stream && !string.IsNullOrEmpty(delta))
            _events.Emit(RunEventKinds.LlmDelta, new { text = delta });
    }

    /// <inheritdoc />
    public void OnTurnCompleted()
    {
        // Turn boundaries are already visible through task.completed; emitting a second
        // marker would only add noise to a stream that is verbose by nature.
    }

    private static string? Blank(string value) => string.IsNullOrEmpty(value) ? null : value;
}
