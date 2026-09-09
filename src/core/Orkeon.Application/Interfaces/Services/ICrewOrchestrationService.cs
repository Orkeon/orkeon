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
    /// Executes crew for each input in sequence.
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
    /// Executes a crew with streaming output.
    /// Emits events as agents think, use tools, and produce answers.
    /// </summary>
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
/// Event emitted during streaming crew execution.
/// </summary>
public record CrewExecutionEvent(
    string AgentRole,
    string TaskDescription,
    AgentThought Thought,
    DateTime Timestamp);

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
