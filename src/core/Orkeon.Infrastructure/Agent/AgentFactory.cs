using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.Agent;

/// <summary>
/// Default implementation of IAgentFactory for creating agents at runtime.
/// Provides both synchronous and asynchronous agent creation with optional validation.
/// </summary>
public sealed partial class AgentFactory : IAgentFactory
{
    private readonly ILogger<AgentFactory>? _logger;

    /// <summary>
    /// Creates a new instance of AgentFactory.
    /// </summary>
    public AgentFactory(ILogger<AgentFactory>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a new agent based on the provided spawn request.
    /// </summary>
    public Domain.Agent.Agent CreateAgent(AgentSpawnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var options = new AgentCreateOptions
            {
                Role = request.Role,
                Goal = request.Goal,
                Backstory = request.Backstory,
                AllowDelegation = request.AllowDelegation,
                MaxIterations = request.MaxIterations,
                MaxRpm = request.MaxRpm,
                Verbose = request.Verbose,
                MaxExecutionTime = request.MaxExecutionTime,
                CacheEnabled = request.CacheEnabled,
                Tools = request.Tools.Count > 0 ? request.Tools : null
            };

            var agent = Domain.Agent.Agent.Create(options);

            if (_logger is not null)
                LogAgentCreated(_logger, agent.Id, request.Role, request.ParentCrewId);

            return agent;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
                LogAgentCreateFailed(_logger, ex, request.Role, request.ParentCrewId);

            throw;
        }
    }

    /// <summary>
    /// Asynchronously creates a new agent based on the provided spawn request.
    /// </summary>
    public Task<Domain.Agent.Agent> CreateAgentAsync(
        AgentSpawnRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateAgentCoreAsync(request, cancellationToken);
    }

    private async Task<Domain.Agent.Agent> CreateAgentCoreAsync(
        AgentSpawnRequest request,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() => CreateAgent(request), cancellationToken).ConfigureAwait(false);
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Dynamic agent created successfully. Agent ID: {AgentId}, Role: {Role}, Parent Crew: {ParentCrew}")]
    static partial void LogAgentCreated(ILogger logger, AgentId agentId, AgentRole role, CrewId parentCrew);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "Failed to create dynamic agent. Role: {Role}, Parent Crew: {ParentCrew}")]
    static partial void LogAgentCreateFailed(ILogger logger, Exception ex, AgentRole role, CrewId parentCrew);
}
