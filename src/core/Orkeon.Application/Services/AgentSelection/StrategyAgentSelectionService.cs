using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Services.AgentSelection;

/// <summary>
/// Real <see cref="IAgentSelectionService"/> that composes a configured
/// <see cref="IAgentSelectionStrategy"/> (embedding-based or skill-matching) to rank
/// agents, instead of returning the first available one.
/// </summary>
/// <remarks>
/// Semantic (embedding) selection only carries real meaning when the injected
/// embedding provider is a real one (OpenAI / Ollama / Local). With the default
/// hash-based <c>SimpleEmbeddingService</c> the embeddings have no semantic signal.
/// </remarks>
public sealed class StrategyAgentSelectionService : IAgentSelectionService
{
    private readonly IAgentSelectionStrategy _strategy;

    /// <summary>
    /// Initializes a new instance of <see cref="StrategyAgentSelectionService"/>.
    /// </summary>
    /// <param name="strategy">The selection strategy to delegate ranking to.</param>
    public StrategyAgentSelectionService(IAgentSelectionStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _strategy = strategy;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<AgentSelectionResult> SelectBestAgentAsync(
        IReadOnlyList<DomainAgent> agents,
        CrewTask task,
        IEmbeddingService embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(task);

        return SelectBestAgentAsyncCore(agents, task, cancellationToken);
    }

    private async System.Threading.Tasks.Task<AgentSelectionResult> SelectBestAgentAsyncCore(
        IReadOnlyList<DomainAgent> agents,
        CrewTask task,
        CancellationToken cancellationToken)
    {
        if (agents.Count == 0)
            return AgentSelectionResult.Failure("No agents available");

        var best = await _strategy
            .SelectBestAgentAsync(task, agents, currentAgent: null, cancellationToken)
            .ConfigureAwait(false);

        if (best is null)
            return AgentSelectionResult.Failure(
                "No agent matched the task above the selection threshold");

        return AgentSelectionResult.Success(best.Id);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<AgentCapability> GetAgentCapabilityAsync(
        DomainAgent agent,
        IEmbeddingService embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return System.Threading.Tasks.Task.FromResult(AgentCapability.Create(
            name: agent.Role,
            description: agent.Goal,
            confidenceLevel: 1.0));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskRequirement> GetTaskRequirementAsync(
        CrewTask task,
        IEmbeddingService embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        return System.Threading.Tasks.Task.FromResult(TaskRequirement.Create(
            name: task.Description.Value,
            type: RequirementType.Capability,
            description: task.ExpectedOutput.Value));
    }
}
