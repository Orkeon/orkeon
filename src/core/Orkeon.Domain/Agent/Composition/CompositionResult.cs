using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Domain.Agent.Composition;

/// <summary>Result of a crew composition operation.</summary>
public sealed record CompositionResult
{
    /// <summary>Gets a value indicating whether the composition succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the composed crew, or null if composition failed.</summary>
    public DomainCrew? Crew { get; init; }
    /// <summary>Gets the agents selected for this crew.</summary>
    public IReadOnlyList<DomainAgent> SelectedAgents { get; init; } = [];
    /// <summary>Gets the tasks planned for this crew.</summary>
    public IReadOnlyList<Task.CrewTask> PlannedTasks { get; init; } = [];
    /// <summary>Gets the assignment mapping from task to agent.</summary>
    public IReadOnlyDictionary<string, string> AgentAssignments { get; init; } = new Dictionary<string, string>();
    /// <summary>Gets the confidence score for this composition (0.0 to 1.0).</summary>
    public double ConfidenceScore { get; init; }
    /// <summary>Gets any warnings generated during composition.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>Gets the error message if composition failed, otherwise null.</summary>
    public string? Error { get; init; }
}
