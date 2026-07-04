using Orkeon.Domain.Common;

namespace Orkeon.Domain.Task;

/// <summary>
/// Definition of a task template.
/// </summary>
public sealed record TaskTemplateDefinition
{
    /// <summary>Gets the template identifier.</summary>
    public TaskTemplateId Id { get; init; } = TaskTemplateId.Create();
    /// <summary>Gets the template name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the task description template.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>Gets the expected output description.</summary>
    public string ExpectedOutput { get; init; } = string.Empty;
    /// <summary>Gets the required agent role, or <see langword="null"/> if any agent can execute it.</summary>
    public string? AgentRole { get; init; }
    /// <summary>Gets the required tool names.</summary>
    public IReadOnlyList<string> RequiredTools { get; init; } = [];
    /// <summary>Gets the required agent skills.</summary>
    public IReadOnlyList<string> RequiredSkills { get; init; } = [];
    /// <summary>Gets the default task parameters.</summary>
    public Dictionary<string, object> DefaultParameters { get; init; } = [];
    /// <summary>Gets the template dependency identifiers.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    /// <summary>Gets the estimated duration for this task.</summary>
    public TimeSpan? EstimatedDuration { get; init; }
    /// <summary>Gets the output schema definition.</summary>
    public Dictionary<string, object> OutputSchema { get; init; } = [];
}
