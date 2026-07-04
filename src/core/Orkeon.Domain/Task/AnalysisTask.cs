using Orkeon.Domain.Common;
using Orkeon.Domain.Task.Contexts;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Task;

/// <summary>
/// Task specialized for analysis activities.
/// </summary>
public class AnalysisTask : CrewTaskBase<AnalysisTaskContext>
{
    /// <summary>
    /// Private constructor for analysis task.
    /// </summary>
    private AnalysisTask(
        TaskId taskId,
        TaskDescription description,
        ExpectedOutput expectedOutput,
        string subject,
        string methodology,
        TaskPriority priority,
        TaskOutputOptions? outputOptions)
        : base(
            taskId,
            description,
            expectedOutput,
            priority,
            outputOptions,
            new AnalysisTaskContext
            {
                Subject = subject,
                Methodology = methodology
            })
    {
    }

    /// <summary>
    /// Creates a new analysis task with the specified parameters.
    /// </summary>
    /// <param name="taskId">The unique task identifier.</param>
    /// <param name="description">The task description.</param>
    /// <param name="expectedOutput">The expected output description.</param>
    /// <param name="subject">The subject of analysis.</param>
    /// <param name="methodology">The methodology to use.</param>
    /// <param name="priority">The task priority.</param>
    /// <param name="outputOptions">Optional output configuration.</param>
    /// <returns>A new <see cref="AnalysisTask"/> instance.</returns>
    public static AnalysisTask Create(
        TaskId taskId,
        TaskDescription description,
        ExpectedOutput expectedOutput,
        string subject,
        string methodology = "",
        TaskPriority? priority = null,
        TaskOutputOptions? outputOptions = null)
    {
        return new AnalysisTask(taskId, description, expectedOutput, subject, methodology, priority ?? TaskPriority.Normal, outputOptions);
    }

    /// <summary>
    /// Adds a metric to the analysis.
    /// </summary>
    public void AddMetric(string name, object value)
    {
        UpdateContext(ctx => ctx.AddMetric(name, value));
    }

    /// <summary>
    /// Adds multiple metrics.
    /// </summary>
    public void AddMetrics(Dictionary<string, object> metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        UpdateContext(ctx =>
        {
            foreach (var kvp in metrics)
            {
                ctx.AddMetric(kvp.Key, kvp.Value);
            }
        });
    }

    /// <summary>
    /// Adds an insight with confidence score.
    /// </summary>
    public void AddInsight(string insight, float confidence = 1.0f)
    {
        UpdateContext(ctx => ctx.AddInsight(insight, confidence));
    }

    /// <summary>
    /// Adds a data source.
    /// </summary>
    public void AddDataSource(DataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        UpdateContext(ctx => ctx.AddDataSource(dataSource));
    }

    /// <summary>
    /// Starts a new analysis stage.
    /// </summary>
    public void StartStage(string stageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);

        UpdateContext(ctx => ctx.AddStage(new AnalysisStage(stageName)));
    }

    /// <summary>
    /// Completes the current stage and optionally starts a new one.
    /// </summary>
    public void CompleteStage(string result, string? nextStageName = null)
    {
        UpdateContext(ctx => ctx.CompleteStage(nextStageName ?? string.Empty, result));
    }

    /// <summary>
    /// Adds a recommendation.
    /// </summary>
    public void AddRecommendation(Recommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);

        UpdateContext(ctx => ctx.AddRecommendation(recommendation));
    }

    /// <summary>
    /// Sets the methodology.
    /// </summary>
    public void SetMethodology(string methodology)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodology);

        UpdateContext(ctx => ctx.SetMethodology(methodology));
    }

    /// <summary>
    /// Gets high-confidence insights.
    /// </summary>
    public IEnumerable<string> GetHighConfidenceInsights(float threshold = 0.8f)
    {
        return TypedContext.GetHighConfidenceInsights(threshold);
    }

    /// <summary>
    /// Gets critical recommendations.
    /// </summary>
    public IEnumerable<Recommendation> GetCriticalRecommendations()
    {
        return TypedContext.Recommendations
            .Where(r => r.Priority == RecommendationPriority.Critical);
    }

    /// <summary>
    /// Gets the current confidence score.
    /// </summary>
    public float ConfidenceScore => TypedContext.ConfidenceScore;

    /// <summary>
    /// Gets completed stages.
    /// </summary>
    public IEnumerable<AnalysisStage> GetCompletedStages()
    {
        return TypedContext.Stages
            .Where(s => s.Status == StageStatus.Completed);
    }

    /// <summary>
    /// Gets a summary of the analysis context.
    /// </summary>
    public override string GetContextSummary()
    {
        var context = TypedContext;
        var completedStages = context.Stages.Count(s => s.Status == StageStatus.Completed);
        return Inv.Format($"Analysis of '{context.Subject}' with {context.Insights.Count} insights, {completedStages}/{context.Stages.Count} stages completed (confidence: {context.ConfidenceScore:P0})");
    }
}
