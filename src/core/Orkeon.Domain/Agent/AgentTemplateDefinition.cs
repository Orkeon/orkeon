using Orkeon.Domain.Common;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Definition of an agent template.
/// </summary>
public class AgentTemplateDefinition
{
    /// <summary>Gets the template identifier.</summary>
    public AgentTemplateId Id { get; init; } = AgentTemplateId.Create();
    /// <summary>Gets the template name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the agent role.</summary>
    public string Role { get; init; } = string.Empty;
    /// <summary>Gets the agent goal.</summary>
    public string Goal { get; init; } = string.Empty;
    /// <summary>Gets the agent backstory.</summary>
    public string Backstory { get; init; } = string.Empty;
    /// <summary>Gets the required tool names.</summary>
    public IReadOnlyList<string> RequiredTools { get; init; } = [];
    /// <summary>Gets the agent skills.</summary>
    public IReadOnlyList<string> Skills { get; init; } = [];
    /// <summary>Gets the default agent parameters.</summary>
    public Dictionary<string, object> DefaultParameters { get; init; } = [];
    /// <summary>Gets whether this agent allows task delegation.</summary>
    public bool AllowDelegation { get; init; } = true;
    /// <summary>Gets the maximum number of iterations.</summary>
    public int MaxIterations { get; init; } = 25;
    /// <summary>Gets whether verbose output is enabled.</summary>
    public bool Verbose { get; init; } = true;
}
