using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Agent.Composition;

/// <summary>Template for creating crews.</summary>
public sealed record CrewTemplate
{
    /// <summary>Gets the unique identifier of this template.</summary>
    public CrewTemplateId Id { get; init; } = CrewTemplateId.Create();
    /// <summary>Gets the name of this template.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the description of this template.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>Gets the goal for crews created from this template.</summary>
    public string Goal { get; init; } = string.Empty;
    /// <summary>Gets the agent templates required by this crew template.</summary>
    public IReadOnlyList<AgentTemplate> RequiredAgents { get; init; } = [];
    /// <summary>Gets the task templates defined in this crew template.</summary>
    public IReadOnlyList<TaskTemplate> TaskTemplates { get; init; } = [];
    /// <summary>Gets the process type for this crew template.</summary>
    public ProcessType ProcessType { get; init; } = ProcessType.Sequential;
    /// <summary>Gets default settings for crews created from this template.</summary>
    public Dictionary<string, object> DefaultSettings { get; init; } = [];
    /// <summary>Gets tags associated with this template.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>Template for agent requirements.</summary>
public sealed record AgentTemplate
{
    /// <summary>Gets the role required for this agent slot.</summary>
    public string Role { get; init; } = string.Empty;
    /// <summary>Gets the goal for this agent.</summary>
    public string Goal { get; init; } = string.Empty;
    /// <summary>Gets the skills required for this agent slot.</summary>
    public IReadOnlyList<string> RequiredSkills { get; init; } = [];
    /// <summary>Gets the tools required for this agent slot.</summary>
    public IReadOnlyList<string> RequiredTools { get; init; } = [];
    /// <summary>Gets a value indicating whether this agent can delegate tasks.</summary>
    public bool AllowDelegation { get; init; } = true;
}

/// <summary>Template for task requirements.</summary>
public sealed record TaskTemplate
{
    /// <summary>Gets the description of this task template.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>Gets the expected output for tasks created from this template.</summary>
    public string ExpectedOutput { get; init; } = string.Empty;
    /// <summary>Gets the role required to execute this task, or null if any agent can execute it.</summary>
    public string? RequiredRole { get; init; }
    /// <summary>Gets the identifiers of tasks this task depends on.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    /// <summary>Gets additional parameters for this task template.</summary>
    public Dictionary<string, object> Parameters { get; init; } = [];
}
