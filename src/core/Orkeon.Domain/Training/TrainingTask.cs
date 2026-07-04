using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Domain.Training;

/// <summary>Represents a task used for training agents.</summary>
public record TrainingTask
{
    private const double DefaultDifficultyLevel = 0.5;
    private const double BasicDifficultyThreshold = 0.3;
    private const double IntermediateDifficultyMin = 0.3;
    private const double AdvancedDifficultyThreshold = SearchDefaults.DefaultSimilarityThreshold;

    /// <summary>Gets the unique identifier of this training task.</summary>
    public TrainingTaskId Id { get; }
    /// <summary>Gets the description of the task.</summary>
    public string Description { get; }
    /// <summary>Gets the expected output for this task.</summary>
    public string ExpectedOutput { get; }
    /// <summary>Gets the context data for this task.</summary>
    public Dictionary<string, object> Context { get; }
    /// <summary>Gets the skills required to complete this task.</summary>
    public IReadOnlyList<string> RequiredSkills { get; }
    /// <summary>Gets the correct approach description, or null if not specified.</summary>
    public string? CorrectApproach { get; }
    /// <summary>Gets the difficulty level (0.0 to 1.0).</summary>
    public double DifficultyLevel { get; }
    /// <summary>Gets the expected duration for this task, or null if not specified.</summary>
    public TimeSpan? ExpectedDuration { get; }

    /// <summary>Initializes a new instance of <see cref="TrainingTask"/>.</summary>
    /// <param name="description">The task description.</param>
    /// <param name="expectedOutput">The expected output.</param>
    /// <param name="context">Context data.</param>
    /// <param name="requiredSkills">Required skills.</param>
    /// <param name="correctApproach">The correct approach description.</param>
    /// <param name="difficultyLevel">Difficulty level (0.0 to 1.0).</param>
    /// <param name="expectedDuration">Expected duration.</param>
    public TrainingTask(
        string description,
        string expectedOutput,
        Dictionary<string, object>? context = null,
        IReadOnlyList<string>? requiredSkills = null,
        string? correctApproach = null,
        double difficultyLevel = DefaultDifficultyLevel,
        TimeSpan? expectedDuration = null)
    {
        Id = TrainingTaskId.Create();
        ArgumentNullException.ThrowIfNull(description);
        Description = description;
        ArgumentNullException.ThrowIfNull(expectedOutput);
        ExpectedOutput = expectedOutput;
        Context = context ?? [];
        RequiredSkills = requiredSkills ?? [];
        CorrectApproach = correctApproach;
        DifficultyLevel = double.IsNaN(difficultyLevel) ? 0.0 : Math.Max(0, Math.Min(1.0, difficultyLevel));
        ExpectedDuration = expectedDuration;
    }

    /// <summary>Gets a value indicating whether this task has basic difficulty (&lt; 0.3).</summary>
    public bool IsBasic => DifficultyLevel < BasicDifficultyThreshold;
    /// <summary>Gets a value indicating whether this task has intermediate difficulty (0.3 to 0.7).</summary>
    public bool IsIntermediate => DifficultyLevel >= IntermediateDifficultyMin && DifficultyLevel < AdvancedDifficultyThreshold;
    /// <summary>Gets a value indicating whether this task has advanced difficulty (>= 0.7).</summary>
    public bool IsAdvanced => DifficultyLevel >= AdvancedDifficultyThreshold;
}
