using System.Globalization;
using CrewId = Orkeon.Domain.Common.CrewId;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Orchestrates crew execution (kickoff, streaming and async variants).
/// </summary>
public interface ICrewOrchestrationService
{
    /// <summary>
    /// Executes a crew with standard kickoff (asynchronous).
    /// </summary>
    /// <remarks>
    /// The synchronous Kickoff() overload was removed to eliminate deadlock risk
    /// from .GetAwaiter().GetResult(). All callers should use await KickoffAsync().
    /// See: AUDIT-P1-08.
    /// </remarks>
    System.Threading.Tasks.Task<CrewOutput> KickoffAsync(
        CrewId crewId,
        CrewInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the crew for each input, in sequence: each input is a full run — started, ended,
    /// its events dispatched — and the next one starts once the previous one has returned
    /// (CrewAI's <c>kickoff_for_each</c>). The results come back in the order of the inputs.
    /// </summary>
    System.Threading.Tasks.Task<BatchOutput> KickoffForEachAsync(
        CrewId crewId,
        IEnumerable<CrewInput> inputs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes crew asynchronously without waiting.
    /// </summary>
    System.Threading.Tasks.Task<CrewExecutionId> KickoffAsyncNoWait(
        CrewId crewId,
        CrewInput input);

    /// <summary>
    /// Gets the status of an async execution.
    /// </summary>
    System.Threading.Tasks.Task<CrewExecutionStatus> GetExecutionStatusAsync(
        CrewExecutionId executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the crew exactly as <see cref="KickoffAsync"/> does — the same tasks, agents, order,
    /// mode, memory, knowledge and plan — and yields what happens as the run goes (GAP-32): each
    /// task's start and end, each tool call, the model's text as it arrives (on a host with an
    /// <c>IChatClient</c>), then <c>run.finished</c> carrying the run's <see cref="CrewOutput"/>,
    /// preceded by an <c>error</c> when the run failed. Each event's <see cref="CrewExecutionEvent.Kind"/>
    /// is a <c>RunEventKinds</c> constant, the vocabulary <c>orkeon run --events</c> writes on the wire.
    /// </summary>
    /// <remarks>
    /// Leaving the stream — a <c>break</c>, the consumer's token — cancels the run; the iterator waits
    /// for it to end, so the crew ends failed by cancellation, its running tasks cancelled and its
    /// events dispatched.
    /// </remarks>
    IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(
        CrewId crewId,
        CrewInput input,
        CancellationToken cancellationToken = default);
}


/// <summary>
/// Execution identifier for async operations. ULID-backed for lexicographic sortability.
/// </summary>
public sealed class CrewExecutionId : Orkeon.Domain.Common.TypedId
{
    private CrewExecutionId(Ulid value) : base(value) { }

    /// <summary>Generates a new unique <see cref="CrewExecutionId"/>.</summary>
    public static CrewExecutionId New() => new(Ulid.NewUlid());

    /// <summary>Creates a <see cref="CrewExecutionId"/> from an existing ULID value.</summary>
    public static CrewExecutionId From(Ulid value) => new(value);

    /// <summary>Creates a <see cref="CrewExecutionId"/> from its canonical string representation (strict ULID parse).</summary>
    /// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is null/empty/whitespace, or not a valid ULID string.</exception>
    public static CrewExecutionId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("CrewExecutionId cannot be null, empty, or whitespace.", nameof(value));
        return new CrewExecutionId(Ulid.Parse(value, CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Input for crew execution.
/// Variables holds user-supplied key/value pairs used for template interpolation.
/// In snake_case crew YAML these are always strings; the <c>object</c> variant is kept for
/// backward compatibility but new code should prefer <see cref="WithStringVariables"/>.
/// </summary>
public record CrewInput(
    string? InitialContext,
    IReadOnlyDictionary<string, object> Variables)
{
    /// <summary>
    /// Creates a <see cref="CrewInput"/> with strongly-typed string variables.
    /// Preferred factory for new code since crew input variables are template strings.
    /// </summary>
    public static CrewInput WithStringVariables(
        string? initialContext,
        IReadOnlyDictionary<string, string> variables)
    {
        var objectVars = variables.ToDictionary(
            kvp => kvp.Key,
            kvp => (object)kvp.Value);
        return new CrewInput(initialContext, objectVars);
    }

    /// <summary>
    /// Creates an empty <see cref="CrewInput"/> with optional context and no variables.
    /// </summary>
    public static CrewInput Empty(string? initialContext = null)
        => new(initialContext, new Dictionary<string, object>());

    /// <summary>
    /// Returns variables as a string dictionary. Values that are not strings are
    /// converted via <see cref="object.ToString"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetStringVariables()
        => Variables.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.ToString() ?? string.Empty);
}

/// <summary>
/// Output from crew execution.
/// </summary>
/// <param name="FinalOutput">The final crew output text.</param>
/// <param name="TaskOutputs">The per-task outputs.</param>
/// <param name="Duration">The total execution duration.</param>
/// <param name="TokensUsed">
/// Real token telemetry measured during execution, or <see langword="null"/> when no
/// telemetry was collected (failed kickoff, strategy without token propagation).
/// <see langword="null"/> means "not measured" — it is never a fabricated zero, so cost
/// tracking can distinguish an unmetered run from a genuine zero-cost execution
/// (R10.8 / MAT-004).
/// </param>
public record CrewOutput(
    string FinalOutput,
    IReadOnlyList<Orkeon.Application.Execution.TaskOutput> TaskOutputs,
    TimeSpan Duration,
    TokenUsage? TokensUsed)
{
    /// <summary>
    /// Whether the crew actually ran to completion. KickoffAsync never throws — its fault
    /// barrier converts every failure into an output — so without this flag a caller could
    /// not tell "the crew answered" from "the crew died and here is the apology string",
    /// and a hosted run reported every timeout as Completed.
    /// </summary>
    public bool Succeeded { get; init; } = true;

    /// <summary>
    /// Why the crew failed, when <see cref="Succeeded"/> is false: the strategy's failure
    /// reason (a task that failed, a circuit breaker, a consensus that was not reached) or
    /// the exception message the fault barrier caught. Null on success. The runner prints
    /// it as its last stderr line and exits non-zero on it (STUDIO-12 C5a).
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Output from batch execution.
/// </summary>
public record BatchOutput(
    IReadOnlyList<CrewOutput> Results,
    int SuccessCount,
    int FailureCount,
    TimeSpan TotalDuration);

/// <summary>
/// Token usage metrics. The cache pair is a partition of <paramref name="PromptTokens"/>
/// — tokens served from the provider's prompt cache versus computed — never an addition
/// to the totals; <see langword="null"/> means "not measured" (a provider without cache
/// telemetry), never a fabricated zero (W-08).
/// </summary>
public record TokenUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens) : ITokenUsage
{
    /// <summary>Prompt tokens served from the provider's cache; null when unmeasured.</summary>
    public long? CacheHitTokens { get; init; }

    /// <summary>Prompt tokens the provider had to compute; null when unmeasured.</summary>
    public long? CacheMissTokens { get; init; }

    /// <summary>Cache hits over measured prompt tokens, in [0,1]; null when unmeasured.</summary>
    public double? CacheHitRatio =>
        CacheHitTokens is { } hit && CacheMissTokens is { } miss && hit + miss > 0
            ? (double)hit / (hit + miss)
            : null;
}



/// <summary>
/// One event of a streamed crew run (<see cref="ICrewOrchestrationService.KickoffStreamingAsync"/>,
/// GAP-32): what happened — <see cref="Kind"/>, a <c>RunEventKinds</c> constant, the vocabulary
/// <c>orkeon run --events</c> writes on the wire —, when, for which task and agent when the event has
/// one, then what its kind says:
/// <list type="table">
///   <listheader><term>Kind</term><description>Carries</description></listheader>
///   <item><term><c>task.started</c></term><description><see cref="TaskId"/>, <see cref="AgentRole"/>.</description></item>
///   <item><term><c>tool.called</c></term><description><see cref="ToolName"/> — never the arguments' values.</description></item>
///   <item><term><c>tool.returned</c></term><description><see cref="ToolName"/>, <see cref="Success"/>, <see cref="Duration"/> — not the result.</description></item>
///   <item><term><c>llm.delta</c></term><description><see cref="Text"/>: the model's text as it arrives.</description></item>
///   <item><term><c>task.completed</c></term><description><see cref="Success"/>, <see cref="Skipped"/> and <see cref="SkipReason"/>, <see cref="Duration"/>, <see cref="Tokens"/>, <see cref="ToolCalls"/>.</description></item>
///   <item><term><c>error</c></term><description><see cref="Code"/> (<c>crew_failed</c> or <c>crew_cancelled</c>) and <see cref="Message"/>, the run's reason.</description></item>
///   <item><term><c>run.finished</c></term><description><see cref="Output"/>: the run's <see cref="CrewOutput"/>. Always last.</description></item>
/// </list>
/// A task's events come in order — its start, its tool calls and deltas, its end —; the events of
/// tasks that run at once interleave. A delta or a tool call carries the task and the agent of the
/// call that produced it: a delegation or a ballot runs on a task of its own, and its events carry
/// that task's id.
/// </summary>
public sealed record CrewExecutionEvent
{
    /// <summary>What happened: a <c>RunEventKinds</c> constant (<c>task.started</c>, <c>llm.delta</c>, …).</summary>
    public required string Kind { get; init; }

    /// <summary>When it happened (UTC).</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The task the event belongs to; null for the run's own events (<c>error</c>, <c>run.finished</c>).</summary>
    public string? TaskId { get; init; }

    /// <summary>The role of the agent the event belongs to; null when no agent is involved.</summary>
    public string? AgentRole { get; init; }

    /// <summary><c>llm.delta</c>: a fragment of the model's text, as it arrived.</summary>
    public string? Text { get; init; }

    /// <summary><c>tool.called</c>, <c>tool.returned</c>: the tool's name.</summary>
    public string? ToolName { get; init; }

    /// <summary><c>tool.returned</c>, <c>task.completed</c>: whether the tool call or the task succeeded.</summary>
    public bool? Success { get; init; }

    /// <summary><c>tool.returned</c>, <c>task.completed</c>: how long the tool call or the task took.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary><c>task.completed</c>: the task never ran, because a task it depends on did not succeed.</summary>
    public bool Skipped { get; init; }

    /// <summary><c>task.completed</c>: why the task was skipped; null unless <see cref="Skipped"/>.</summary>
    public string? SkipReason { get; init; }

    /// <summary><c>task.completed</c>: the tokens the task used, 0 when its mode does not track them.</summary>
    public int Tokens { get; init; }

    /// <summary><c>task.completed</c>: the tool calls the task made, 0 when its mode does not track them.</summary>
    public int ToolCalls { get; init; }

    /// <summary><c>error</c>: <c>crew_failed</c> or <c>crew_cancelled</c> (<c>RunEventErrorCodes</c>).</summary>
    public string? Code { get; init; }

    /// <summary><c>error</c>: why the run failed — the crew's error, naming each task that did not succeed.</summary>
    public string? Message { get; init; }

    /// <summary><c>run.finished</c>: the run's output — success, error, final output, task outputs, tokens.</summary>
    public CrewOutput? Output { get; init; }
}

/// <summary>
/// Status of async crew execution.
/// </summary>
public record CrewExecutionStatus(
    CrewExecutionId Id,
    ExecutionState State,
    double Progress,
    string? CurrentTask,
    string? Error);

/// <summary>
/// Execution state enum.
/// </summary>
public enum ExecutionState
{
    /// <summary>Pending.</summary>
    Pending,
    /// <summary>Running.</summary>
    Running,
    /// <summary>Completed.</summary>
    Completed,
    /// <summary>Failed.</summary>
    Failed,
    /// <summary>Cancelled.</summary>
    Cancelled
}
