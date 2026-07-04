using Orkeon.Domain.Common;

namespace Orkeon.Domain.Training;

/// <summary>Represents a training scenario for crews.</summary>
public sealed record TrainingScenario
{
    /// <summary>Gets the unique identifier of this scenario.</summary>
    public TrainingScenarioId Id { get; init; } = TrainingScenarioId.Create();
    /// <summary>Gets the name of this scenario.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the description of this scenario.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>Gets the goal for this training scenario.</summary>
    public string Goal { get; init; } = string.Empty;
    /// <summary>Gets the training objectives for this scenario.</summary>
    public IReadOnlyList<TrainingObjective> Objectives { get; init; } = [];
    /// <summary>Gets the initial context data for this scenario.</summary>
    public Dictionary<string, object> InitialContext { get; init; } = [];
    /// <summary>Gets the steps in this training scenario.</summary>
    public IReadOnlyList<TrainingStep> Steps { get; init; } = [];
    /// <summary>Gets the difficulty level of this scenario.</summary>
    public TrainingDifficulty Difficulty { get; init; } = TrainingDifficulty.Medium;
    /// <summary>Gets the estimated duration for completing this scenario.</summary>
    public TimeSpan EstimatedDuration { get; init; }
}

/// <summary>Training objective.</summary>
public sealed record TrainingObjective
{
    /// <summary>Gets the unique identifier of this objective.</summary>
    public TrainingObjectiveId Id { get; init; } = TrainingObjectiveId.Create();
    /// <summary>Gets the description of this objective.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>Gets the success criteria for this objective.</summary>
    public Dictionary<string, object> SuccessCriteria { get; init; } = [];
    /// <summary>Gets the weight of this objective in the overall score.</summary>
    public double Weight { get; init; } = 1.0;
}

/// <summary>Training step.</summary>
public sealed record TrainingStep
{
    /// <summary>Gets the unique identifier of this step.</summary>
    public TrainingStepId Id { get; init; } = TrainingStepId.Create();
    /// <summary>Gets the action to perform in this step.</summary>
    public string Action { get; init; } = string.Empty;
    /// <summary>Gets the expected result of this step.</summary>
    public string ExpectedResult { get; init; } = string.Empty;
    /// <summary>Gets the context data for this step.</summary>
    public Dictionary<string, object> Context { get; init; } = [];
    /// <summary>Gets hints available for this step.</summary>
    public IReadOnlyList<string> Hints { get; init; } = [];
}

/// <summary>Training difficulty levels.</summary>
public enum TrainingDifficulty
{
    /// <summary>Beginner difficulty level.</summary>
    Beginner,
    /// <summary>Easy difficulty level.</summary>
    Easy,
    /// <summary>Medium difficulty level.</summary>
    Medium,
    /// <summary>Hard difficulty level.</summary>
    Hard,
    /// <summary>Expert difficulty level.</summary>
    Expert
}

/// <summary>Result of a training session.</summary>
public sealed record TrainingResult
{
    /// <summary>Gets the identifier of the training scenario.</summary>
    public TrainingScenarioId ScenarioId { get; init; } = TrainingScenarioId.Create();
    /// <summary>Gets the identifier of the crew that completed the training.</summary>
    public CrewId CrewId { get; init; } = CrewId.Create();
    /// <summary>Gets a value indicating whether the training succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the overall training score.</summary>
    public double Score { get; init; }
    /// <summary>Gets the total duration of the training session.</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>Gets scores for individual objectives.</summary>
    public IReadOnlyDictionary<string, double> ObjectiveScores { get; init; } = new Dictionary<string, double>();
    /// <summary>Gets feedback from the training session.</summary>
    public IReadOnlyList<string> Feedback { get; init; } = [];
    /// <summary>Gets the timestamp when the training was completed.</summary>
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
}
