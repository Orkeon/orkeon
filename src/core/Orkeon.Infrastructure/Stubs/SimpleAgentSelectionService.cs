using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Stubs;

/// <summary>
/// Simple implementation of <see cref="IAgentSelectionService"/> that selects
/// the first available agent. Logs a warning on first use to indicate semantic
/// selection is not configured.
/// </summary>
public sealed partial class SimpleAgentSelectionService : IAgentSelectionService
{
    private readonly ILogger<SimpleAgentSelectionService> _logger;
    private int _warnedOnce;

    /// <summary>Initializes a new instance of <see cref="SimpleAgentSelectionService"/>.</summary>
    public SimpleAgentSelectionService(ILogger<SimpleAgentSelectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    private void WarnOnce()
    {
        if (Interlocked.Exchange(ref _warnedOnce, 1) == 0)
            LogFirstFitFallback();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Using SimpleAgentSelectionService — agent selection uses first-fit, not semantic similarity. " +
            "Register a real IAgentSelectionService with embedding support for production.")]
    private partial void LogFirstFitFallback();

    /// <inheritdoc />
    public Task<AgentSelectionResult> SelectBestAgentAsync(
        IReadOnlyList<DomainAgent> agents,
        CrewTask task,
        IEmbeddingService embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agents);
        WarnOnce();
        if (agents.Count == 0)
            return Task.FromResult(AgentSelectionResult.Failure("No agents available"));

        var selected = agents[0];
        return Task.FromResult(AgentSelectionResult.Success(selected.Id, confidence: 1.0));
    }

    /// <inheritdoc />
    public Task<AgentCapability> GetAgentCapabilityAsync(
        DomainAgent agent,
        IEmbeddingService embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        WarnOnce();
        return Task.FromResult(AgentCapability.Create(
            name: agent.Role,
            description: agent.Goal,
            confidenceLevel: 1.0));
    }

    /// <inheritdoc />
    public Task<TaskRequirement> GetTaskRequirementAsync(
        CrewTask task,
        IEmbeddingService embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        WarnOnce();
        return Task.FromResult(TaskRequirement.Create(
            name: task.Description.Value,
            type: RequirementType.Capability,
            description: task.ExpectedOutput.Value));
    }
}
