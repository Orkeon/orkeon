using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Agent;

namespace Orkeon.Interop.AgentFramework;

/// <summary>Fluent-builder entry points of the Agent Framework interop.</summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Makes <paramref name="agent"/> the model of the Orkeon agent being built: its own provider
    /// (<c>AgentBuilder.WithLlm(new AIAgentLlmProvider(agent, logger))</c>). The Orkeon agent keeps its
    /// role, goal and tasks; what answers is the MAF agent — the turns of its tasks and their correction
    /// round, its work as a hierarchical manager, its ballot in a consensual vote —, on a client the run
    /// builds over the provider and meters as the agent's work.
    /// <para>
    /// A task's <c>llm_override</c> profile still moves that task off it. The agent cannot run on a host
    /// profile as well (<c>WithLlmConfig(LlmConfig.OnProfile(name))</c>), nor carry Orkeon tools or
    /// delegation, which a MAF agent never calls: <c>Build()</c> refuses them, naming the remedies — the
    /// tool on the MAF agent itself, or the MAF agent as a tool of an Orkeon agent
    /// (<see cref="WithAgentFrameworkTool"/>).
    /// </para>
    /// </summary>
    /// <param name="builder">The Orkeon agent being built.</param>
    /// <param name="agent">The MAF agent that answers for it.</param>
    /// <param name="logger">
    /// Where the bridge says which options it did not send (<see cref="AIAgentLlmProvider"/>); null takes
    /// the logger factory the MAF agent exposes, else logs nothing.
    /// </param>
    public static AgentBuilder WithAgentFrameworkAgent(this AgentBuilder builder, AIAgent agent, ILogger<AIAgentLlmProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(agent);
        return builder.WithLlm(new AIAgentLlmProvider(agent, logger));
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
