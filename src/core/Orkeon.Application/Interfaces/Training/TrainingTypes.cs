using Orkeon.Application.Evaluation;
using Orkeon.Domain.Constants.Memory;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Application.Interfaces.Training;

/// <summary>
/// Defines the training plan for an agent, including the tasks to execute and iteration count.
/// </summary>
public record TrainingPlan(
    string Name,
    IReadOnlyList<TrainingTask> Tasks,
    int Iterations = 1);

/// <summary>
/// A single task within a training plan.
/// </summary>
public record TrainingTask(
    string TaskId,
    string Description,
    string? ExpectedOutput = null);

/// <summary>
/// Options controlling training behavior.
/// </summary>
public record TrainingOptions
{
    /// <summary>Minimum average score required to pass.</summary>
    public double PassingThreshold { get; init; } = SearchDefaults.DefaultSimilarityThreshold;

    /// <summary>Whether to collect feedback after evaluation.</summary>
    public bool CollectFeedback { get; init; } = true;

    /// <summary>Maximum retry attempts for failed tasks.</summary>
    public int MaxRetries { get; init; } = AgentDefaults.MaxRetryLimit;
}

/// <summary>
/// Result of a single training iteration for an agent.
/// </summary>
public record TrainingResult
{
    /// <summary>The agent that was trained.</summary>
    public required string AgentId { get; init; }

    /// <summary>The training plan name.</summary>
    public required string PlanName { get; init; }

    /// <summary>The iteration number (1-based).</summary>
    public required int Iteration { get; init; }

    /// <summary>Average score across all evaluations.</summary>
    public required double AverageScore { get; init; }

    /// <summary>Whether the agent passed the training threshold.</summary>
    public required bool Passed { get; init; }

    /// <summary>Individual evaluation scores.</summary>
    public required IReadOnlyList<EvaluationScore> Scores { get; init; }

    /// <summary>Optional feedback generated from evaluation.</summary>
    public TrainingFeedback? Feedback { get; init; }

    /// <summary>When this training iteration completed.</summary>
    public required DateTime CompletedAt { get; init; }
}

/// <summary>
/// Feedback generated from training evaluation results.
/// </summary>
public record TrainingFeedback(
    string AgentId,
    string Summary,
    IReadOnlyList<string> Suggestions,
    DateTime GeneratedAt);

/// <summary>
/// Performance trend data for an agent over time.
/// </summary>
public record PerformanceTrend(
    string AgentId,
    IReadOnlyList<PerformanceDataPoint> DataPoints,
    double AverageScore,
    double Improvement);

/// <summary>
/// A single performance data point.
/// </summary>
public record PerformanceDataPoint(
    string TaskId,
    double Score,
    DateTime RecordedAt,
    IReadOnlyDictionary<string, object>? Metadata = null);
