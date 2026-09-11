using Microsoft.Agents.AI;
using Orkeon.Domain.Agent;

namespace Orkeon.Interop.AgentFramework;

/// <summary>Fluent-builder entry points of the Agent Framework interop.</summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Makes <paramref name="agent"/> the language model of the Orkeon agent being built:
    /// every prompt the Orkeon agent sends is a run of the MAF agent. Its role, goal and
    /// tasks stay Orkeon's; what answers is MAF's.
    /// </summary>
    public static AgentBuilder WithAgentFrameworkAgent(this AgentBuilder builder, AIAgent agent)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(agent);
        return builder.WithLlm(new AIAgentLlmProvider(agent));
    }

    /// <summary>
    /// Gives the Orkeon agent being built a tool that delegates to <paramref name="agent"/>
    /// (<see cref="AIAgentTool"/>), next to whatever other tools it carries.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The tool is owned by the agent being built; ToolBase disposal follows the agent's lifetime, like every other WithTool(...) call.")]
    public static AgentBuilder WithAgentFrameworkTool(this AgentBuilder builder, AIAgent agent, string? toolName = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(agent);
        return builder.WithTool(new AIAgentTool(agent, toolName, description));
    }
}
