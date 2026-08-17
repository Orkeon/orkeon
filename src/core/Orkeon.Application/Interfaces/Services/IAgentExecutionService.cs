using System.Collections.Immutable;
using Orkeon.Application.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using ToolUsage = Orkeon.Domain.Tools.ToolUsage;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Manages agent task execution.
/// </summary>
public interface IAgentExecutionService
{
    /// <summary>
    /// Executes a task with the specified agent.
    /// Handles tool usage, memory access, and delegation.
    /// </summary>
    System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent,
        ICrewTask task,
        Context.SimpleExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a task with type-safe output.
    /// </summary>
    /// <typeparam name="TOutput">The expected output type</typeparam>
    System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent,
        ICrewTask task,
        Context.SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
        where TOutput : class;

    /// <summary>
    /// Plans task execution without executing.
    /// Used by hierarchical process for delegation decisions.
    /// </summary>
    System.Threading.Tasks.Task<TaskExecutionPlan> PlanTaskExecutionAsync(
        DomainAgent agent,
        ICrewTask task,
        Context.SimpleExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if an agent can execute a specific task.
    /// Checks tools, capabilities, and context requirements.
    /// </summary>
    System.Threading.Tasks.Task<bool> CanExecuteTaskAsync(
        DomainAgent agent,
        ICrewTask task,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for streaming agent execution with real-time thought process.
/// </summary>
public interface IStreamingAgentExecutionService
{
    /// <summary>
    /// Streams the agent's execution process as it happens.
    /// </summary>
    IAsyncEnumerable<AgentThought> StreamExecutionAsync(
        DomainAgent agent,
        CrewTask task,
        Context.SimpleExecutionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a single thought or action during agent execution.
/// </summary>
public record AgentThought(
    string Content,
    AgentThought.ThoughtType Type,
    IReadOnlyList<ToolCall>? ToolCalls,
    DateTime Timestamp)
{
    /// <summary>
    /// ThoughtType type.
    /// </summary>
    public enum ThoughtType
    {
        /// <summary>Reasoning.</summary>
        Reasoning,
        /// <summary>Tool Selection.</summary>
        ToolSelection,
        /// <summary>Tool Execution.</summary>
        ToolExecution,
        /// <summary>Conclusion.</summary>
        Conclusion,
        /// <summary>Error.</summary>
        Error
    }
}

/// <summary>
/// Describes why the agent execution loop exited.
/// </summary>
public enum AgentExitReason
{
    /// <summary>Agent produced a final answer normally.</summary>
    Completed,

    /// <summary>The iteration loop exhausted <c>MaxIterations</c> without a final answer.</summary>
    MaxIterationsReached,

    /// <summary>Repeated identical errors tripped the circuit breaker (P1-BUG-02).</summary>
    CircuitBreakerTripped,

    /// <summary>The <see cref="CancellationToken"/> was signalled.</summary>
    Cancelled,

    /// <summary>The <c>AgentExecutionBudget</c> was exhausted (Autonomous mode).</summary>
    BudgetExhausted
}

/// <summary>
/// Result of task execution.
/// </summary>
public record TaskResult(
    bool Success,
    string Output,
    object? StructuredOutput,
    IReadOnlyList<ToolUsage> ToolsUsed,
    TimeSpan ExecutionTime,
    string? Error = null,
    int TokensUsed = 0)
{
    /// <summary>
    /// Structured reason for the agent execution loop exit.
    /// Defaults to <see cref="AgentExitReason.Completed"/>.
    /// </summary>
    public AgentExitReason ExitReason { get; init; } = AgentExitReason.Completed;

    /// <summary>
    /// Number of iterations the agent actually performed before exiting.
    /// </summary>
    public int IterationsUsed { get; init; }

    /// <summary>
    /// Last error message observed during execution (useful when <see cref="ExitReason"/>
    /// is <see cref="AgentExitReason.CircuitBreakerTripped"/> or
    /// <see cref="AgentExitReason.MaxIterationsReached"/>).
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// FQNs mentioned in prose inside the deliverable that could not be resolved against
    /// the RaggableTree store. Non-blocking — surfaced for AUTO_SUMMARY warnings.
    /// Empty when no inline FQN validation ran or when every cited FQN was known.
    /// </summary>
    public ImmutableArray<string> UnknownFqns { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Bare-form prose citations (e.g. <c>ts::Symbol</c>) that the validator resolved
    /// uniquely to a canonical long FQN via local-name lookup. Mapping is bare → canonical.
    /// </summary>
    public ImmutableDictionary<string, string> RewrittenFqns { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Bare-form citations that matched 2 or more canonical FQNs and require operator
    /// review. Each entry lists the candidates surfaced by the local-name lookup.
    /// </summary>
    public ImmutableArray<TaskAmbiguousFqn> AmbiguousFqns { get; init; } =
        ImmutableArray<TaskAmbiguousFqn>.Empty;

    /// <summary>
    /// Cumulative prompt-side tokens consumed during the task, when the provider
    /// reports the prompt/completion split. 0 when the split is unavailable —
    /// <see cref="TaskResult.TokensUsed"/> remains the authoritative total.
    /// </summary>
    public int PromptTokens { get; init; }

    /// <summary>
    /// Cumulative completion-side tokens consumed during the task, when the provider
    /// reports the prompt/completion split. 0 when the split is unavailable.
    /// </summary>
    public int CompletionTokens { get; init; }

    /// <summary>
    /// Cumulative prompt tokens that hit the provider's prompt cache during the task.
    /// Populated only for providers that report this metric (DeepSeek's <c>prompt_cache_hit_tokens</c>).
    /// 0 = either no cache hits, or the provider does not expose the field.
    /// </summary>
    public long CacheHitTokens { get; init; }

    /// <summary>
    /// Cumulative prompt tokens that missed the provider's prompt cache during the task.
    /// Populated only for providers that report this metric (DeepSeek's <c>prompt_cache_miss_tokens</c>).
    /// </summary>
    public long CacheMissTokens { get; init; }
}

/// <summary>
/// Type-safe result of task execution.
/// </summary>
/// <typeparam name="TOutput">The output type</typeparam>
public record TaskResult<TOutput>(
    bool Success,
    string RawOutput,
    TOutput? StructuredOutput,
    IReadOnlyList<ToolUsage> ToolsUsed,
    TimeSpan ExecutionTime,
    string? Error = null) where TOutput : class;

/// <summary>
/// Execution plan for a task.
/// </summary>
public record TaskExecutionPlan(
    Orkeon.Domain.Common.AgentId AssignedAgent,
    IReadOnlyList<PlannedStep> Steps,
    TimeSpan EstimatedDuration,
    double ConfidenceScore);

/// <summary>
/// A planned execution step.
/// Uses strongly-typed tool parameters instead of Dictionary with string keys and object values.
/// </summary>
public record PlannedStep(
    string Description,
    string? ToolName,
    ITypedToolParameters? ToolParameters);


