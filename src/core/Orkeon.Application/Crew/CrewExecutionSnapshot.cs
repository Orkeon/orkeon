using System.Collections.Immutable;

namespace Orkeon.Application.Crew;

/// <summary>
/// Immutable snapshot of a single completed task captured during crew execution.
/// </summary>
public sealed record TaskExecutionSnapshot
{
    /// <summary>Task identifier string.</summary>
    public required string TaskId { get; init; }

    /// <summary>Role of the agent that executed the task.</summary>
    public required string AgentRole { get; init; }

    /// <summary>Whether the task completed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>Wall-clock duration of the task.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>UTC timestamp when the task finished.</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Number of tool calls made during the task, or zero when not tracked.</summary>
    public int ToolCallCount { get; init; }

    /// <summary>Number of LLM exchanges (prompt → completion pairs) during the task.</summary>
    public int LlmExchangeCount { get; init; }

    /// <summary>Tokens consumed during the task, or zero when not tracked.</summary>
    public int TokensUsed { get; init; }

    /// <summary>
    /// Prompt tokens that hit the provider's prompt cache during the task (DeepSeek's
    /// <c>prompt_cache_hit_tokens</c>). 0 when the provider does not expose the metric.
    /// Surfaced for Experiment 07 friction #5 so operators can pilot the cache.
    /// </summary>
    public long CacheHitTokens { get; init; }

    /// <summary>Prompt tokens that missed the provider's prompt cache during the task.</summary>
    public long CacheMissTokens { get; init; }

    /// <summary>
    /// FQNs mentioned in the task's deliverable that could not be resolved in the
    /// RaggableTree store. Populated by <see cref="DeliverableResolvers.FinalMessageResolver"/>
    /// when an <c>IInlineFqnValidator</c> is registered. Empty otherwise.
    /// </summary>
    public ImmutableArray<string> UnknownFqns { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Bare-form prose citations (e.g. <c>ts::Symbol</c>) that resolved uniquely to a
    /// canonical long FQN via local-name lookup. Mapping is bare → canonical. Empty
    /// when no rewrites were performed.
    /// </summary>
    public ImmutableDictionary<string, string> RewrittenFqns { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Bare-form prose citations that matched 2 or more canonical FQNs and require operator
    /// review. Each entry lists the candidates. Empty when no ambiguity was detected.
    /// </summary>
    public ImmutableArray<TaskAmbiguousFqn> AmbiguousFqns { get; init; } =
        ImmutableArray<TaskAmbiguousFqn>.Empty;
}

/// <summary>
/// AUTO_SUMMARY-friendly view of an ambiguous bare FQN — flattened to avoid leaking
/// <c>Orkeon.Analysis.Abstractions</c> types into <c>Orkeon.Application</c>.
/// </summary>
/// <param name="BareFqn">The original bare-form citation.</param>
/// <param name="Candidates">Canonical FQNs sharing the trailing symbol name.</param>
public sealed record TaskAmbiguousFqn(
    string BareFqn,
    ImmutableArray<string> Candidates);

/// <summary>
/// Accumulated snapshot of an entire crew execution, built task-by-task.
/// Passed to <see cref="ICrewExecutionHook.OnCrewCompletedAsync"/> and
/// <see cref="ICrewExecutionHook.OnCrewFailedAsync"/>.
/// </summary>
public sealed record CrewExecutionSnapshot
{
    /// <summary>Crew identifier string.</summary>
    public required string CrewId { get; init; }

    /// <summary>UTC timestamp when the crew execution started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>UTC timestamp when the crew execution ended (canceled, failed, or completed).</summary>
    public required DateTimeOffset EndedAt { get; init; }

    /// <summary>Total wall-clock duration of the crew run.</summary>
    public TimeSpan TotalDuration => EndedAt - StartedAt;

    /// <summary>All task snapshots recorded before the run ended.</summary>
    public required ImmutableList<TaskExecutionSnapshot> Tasks { get; init; }

    /// <summary>Number of tasks that completed successfully.</summary>
    public int CompletedTaskCount => Tasks.Count(t => t.Success);

    /// <summary>Number of tasks that did not complete successfully.</summary>
    public int FailedOrInterruptedTaskCount => Tasks.Count(t => !t.Success);

    /// <summary>Total tokens used across all tasks.</summary>
    public int TotalTokensUsed => Tasks.Sum(t => t.TokensUsed);

    /// <summary>Total prompt cache hit tokens across all tasks.</summary>
    public long TotalCacheHitTokens => Tasks.Sum(t => t.CacheHitTokens);

    /// <summary>Total prompt cache miss tokens across all tasks.</summary>
    public long TotalCacheMissTokens => Tasks.Sum(t => t.CacheMissTokens);

    /// <summary>
    /// Aggregate prompt-cache hit ratio across all tasks — <c>hit / (hit + miss)</c> per DeepSeek
    /// guideline §6.5 — or <see langword="null"/> when no cache tokens were reported. Below ~0.5
    /// indicates an unstable prompt prefix worth auditing for cache friendliness.
    /// </summary>
    public double? TotalCacheHitRatio =>
        (TotalCacheHitTokens + TotalCacheMissTokens) > 0
            ? (double)TotalCacheHitTokens / (TotalCacheHitTokens + TotalCacheMissTokens)
            : null;

    /// <summary>Total tool calls across all tasks.</summary>
    public int TotalToolCallCount => Tasks.Sum(t => t.ToolCallCount);

    /// <summary>
    /// Virtual paths of files written to output mounts during the run.
    /// Populated by the hook implementation if it has access to the file system service.
    /// </summary>
    public ImmutableList<OutputFileInfo> OutputFiles { get; init; } = [];

    /// <summary>
    /// Status of the crew execution from the hook's perspective.
    /// </summary>
    public required CrewHookStatus Status { get; init; }

    /// <summary>Human-readable failure message when <see cref="Status"/> is not <see cref="CrewHookStatus.Completed"/>.</summary>
    public string? FailureReason { get; init; }
}

/// <summary>
/// Information about a file written under an output mount.
/// </summary>
public sealed record OutputFileInfo
{
    /// <summary>Virtual path of the file (e.g. <c>/output/report.md</c>).</summary>
    public required string VirtualPath { get; init; }

    /// <summary>File size in bytes at the time of snapshot.</summary>
    public required long SizeBytes { get; init; }
}

/// <summary>
/// Status of a crew execution from a lifecycle hook perspective.
/// </summary>
public enum CrewHookStatus
{
    /// <summary>All tasks completed within time/budget limits.</summary>
    Completed,

    /// <summary>Execution was canceled (e.g. timeout via <see cref="OperationCanceledException"/>).</summary>
    Canceled,

    /// <summary>Execution failed with an unexpected exception.</summary>
    Failed,
}
