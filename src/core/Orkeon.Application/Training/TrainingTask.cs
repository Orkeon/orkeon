namespace Orkeon.Application.Training;

/// <summary>
/// Represents a task used for training agents.
/// </summary>
public record TrainingTask(
    string Id,
    string Description,
    string ExpectedOutput,
    IReadOnlyList<string> RequiredSkills,
    Dictionary<string, object>? Context = null,
    TimeSpan? TimeLimit = null)
{
    /// <summary>
    /// Creates a new training task with auto-generated ID.
    /// </summary>
    public static TrainingTask Create(
        string description,
        string expectedOutput,
        IReadOnlyList<string> requiredSkills)
    {
        return new TrainingTask(
            Id: Guid.NewGuid().ToString(),
            Description: description,
            ExpectedOutput: expectedOutput,
            RequiredSkills: requiredSkills);
    }
}
