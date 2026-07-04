namespace Orkeon.Domain.Task.Contexts;

/// <summary>Context for analysis tasks.</summary>
public class AnalysisTaskContext
{
    /// <summary>Gets or sets the subject of analysis.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>Gets or sets the analysis metrics.</summary>
    public Dictionary<string, object> Metrics { get; init; } = [];

    private readonly List<string> _insights = [];
    private readonly List<AnalysisStage> _stages = [];
    private readonly List<DataSource> _dataSources = [];
    private readonly List<Recommendation> _recommendations = [];

    /// <summary>Gets or sets the insights discovered.</summary>
    public IReadOnlyList<string> Insights
    {
        get => _insights;
        init => _insights = AsBackingList(value);
    }

    /// <summary>Gets the confidence score.</summary>
    public float ConfidenceScore { get; internal set; }

    /// <summary>Gets the methodology used.</summary>
    public string Methodology { get; internal set; } = string.Empty;

    /// <summary>Gets or sets the data sources analyzed.</summary>
    public IReadOnlyList<DataSource> DataSources
    {
        get => _dataSources;
        init => _dataSources = AsBackingList(value);
    }

    /// <summary>Gets or sets the analysis stages.</summary>
    public IReadOnlyList<AnalysisStage> Stages
    {
        get => _stages;
        init => _stages = AsBackingList(value);
    }

    /// <summary>Gets or sets recommendations based on analysis.</summary>
    public IReadOnlyList<Recommendation> Recommendations
    {
        get => _recommendations;
        init => _recommendations = AsBackingList(value);
    }

    private static List<T> AsBackingList<T>(IReadOnlyList<T> value) =>
        value switch
        {
            null => null!,
            List<T> list => list,
            _ => [.. value],
        };

    /// <summary>Adds a data source to the analysis.</summary>
    /// <param name="dataSource">The data source to add.</param>
    public void AddDataSource(DataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSources.Add(dataSource);
    }

    /// <summary>Starts a new analysis stage.</summary>
    /// <param name="stage">The stage to add.</param>
    public void AddStage(AnalysisStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        _stages.Add(stage);
    }

    /// <summary>Adds a recommendation to the analysis.</summary>
    /// <param name="recommendation">The recommendation to add.</param>
    public void AddRecommendation(Recommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        _recommendations.Add(recommendation);
    }

    /// <summary>Sets the methodology used for analysis.</summary>
    /// <param name="methodology">The methodology name.</param>
    internal void SetMethodology(string methodology)
    {
        Methodology = methodology;
    }

    /// <summary>Adds a metric to the analysis.</summary>
    /// <param name="name">The metric name.</param>
    /// <param name="value">The metric value.</param>
    public void AddMetric(string name, object value)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Metrics[name] = value ?? "null";
        }
    }

    /// <summary>Adds an insight.</summary>
    /// <param name="insight">The insight text.</param>
    /// <param name="confidence">The confidence level for this insight.</param>
    public void AddInsight(string insight, float confidence = 1.0f)
    {
        if (!string.IsNullOrWhiteSpace(insight))
        {
            _insights.Add(insight);
            UpdateConfidence(confidence);
        }
    }

    /// <summary>Updates the overall confidence score.</summary>
    private void UpdateConfidence(float newConfidence)
    {
        if (Insights.Count == 1)
        {
            ConfidenceScore = newConfidence;
        }
        else
        {
            // Weighted average
            ConfidenceScore = ((ConfidenceScore * (Insights.Count - 1)) + newConfidence) / Insights.Count;
        }

        ConfidenceScore = Math.Clamp(ConfidenceScore, 0.0f, 1.0f);
    }

    /// <summary>Gets high-confidence insights.</summary>
    /// <param name="threshold">The confidence threshold.</param>
    /// <returns>Insights above the confidence threshold.</returns>
    public IEnumerable<string> GetHighConfidenceInsights(float threshold = 0.8f)
    {
        // In a more complex implementation, each insight would have its own confidence
        if (ConfidenceScore >= threshold)
            return Insights;

        return [];
    }

    /// <summary>Completes the current stage and starts a new one.</summary>
    /// <param name="stageName">The name of the new stage to start.</param>
    /// <param name="result">The result of the completed stage.</param>
    public void CompleteStage(string stageName, string result)
    {
        var index = _stages.FindIndex(s => s.Status == StageStatus.InProgress);
        if (index >= 0)
        {
            _stages[index] = _stages[index].WithCompletion(result);
        }

        if (!string.IsNullOrWhiteSpace(stageName))
        {
            _stages.Add(new AnalysisStage(stageName));
        }
    }
}

/// <summary>Represents a data source for analysis.</summary>
public sealed record DataSource
{
    /// <summary>Gets the name of this data source.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the type of this data source.</summary>
    public string Type { get; init; } = string.Empty;
    /// <summary>Gets the location or URL of this data source.</summary>
    public string Location { get; init; } = string.Empty;
    /// <summary>Gets the timestamp when this data source was accessed.</summary>
    public DateTime AccessedAt { get; init; } = DateTime.UtcNow;
    /// <summary>Gets the number of records in this data source.</summary>
    public long RecordCount { get; init; }
    /// <summary>Gets additional metadata for this data source.</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>Represents an immutable stage in the analysis process.</summary>
public class AnalysisStage
{
    /// <summary>Gets the name of this stage.</summary>
    public string Name { get; }
    /// <summary>Gets the timestamp when this stage started.</summary>
    public DateTime StartedAt { get; }
    /// <summary>Gets the timestamp when this stage completed, or null if not completed.</summary>
    public DateTime? CompletedAt { get; }
    /// <summary>Gets the status of this stage.</summary>
    public StageStatus Status { get; }
    /// <summary>Gets the result of this stage.</summary>
    public string Result { get; }

    /// <summary>Initializes a new instance of <see cref="AnalysisStage"/> with the given name.</summary>
    /// <param name="name">The stage name.</param>
    public AnalysisStage(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        StartedAt = DateTime.UtcNow;
        Status = StageStatus.InProgress;
        CompletedAt = null;
        Result = string.Empty;
    }

    private AnalysisStage(string name, DateTime startedAt, DateTime? completedAt, StageStatus status, string result)
    {
        Name = name;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Status = status;
        Result = result;
    }

    /// <summary>Returns a new <see cref="AnalysisStage"/> marked as completed with the given result.</summary>
    /// <param name="result">The result of this stage.</param>
    /// <returns>A new completed <see cref="AnalysisStage"/> instance.</returns>
    public AnalysisStage WithCompletion(string result)
    {
        return new AnalysisStage(Name, StartedAt, DateTime.UtcNow, StageStatus.Completed, result ?? string.Empty);
    }
}

/// <summary>Stage status enumeration.</summary>
public enum StageStatus
{
    /// <summary>The stage has not started.</summary>
    NotStarted,
    /// <summary>The stage is currently in progress.</summary>
    InProgress,
    /// <summary>The stage has completed successfully.</summary>
    Completed,
    /// <summary>The stage has failed.</summary>
    Failed
}

/// <summary>Represents a recommendation from the analysis.</summary>
public sealed record Recommendation
{
    /// <summary>Gets the title of this recommendation.</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>Gets the description of this recommendation.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>Gets the priority of this recommendation.</summary>
    public RecommendationPriority Priority { get; init; }
    /// <summary>Gets the impact score (0.0 to 1.0).</summary>
    public float ImpactScore { get; init; }
    /// <summary>Gets the effort score (0.0 to 1.0).</summary>
    public float EffortScore { get; init; }
    /// <summary>Gets the recommended actions.</summary>
    public IReadOnlyList<string> Actions { get; init; } = [];
}

/// <summary>Recommendation priority levels.</summary>
public enum RecommendationPriority
{
    /// <summary>Low priority recommendation.</summary>
    Low,
    /// <summary>Medium priority recommendation.</summary>
    Medium,
    /// <summary>High priority recommendation.</summary>
    High,
    /// <summary>Critical priority recommendation.</summary>
    Critical
}
