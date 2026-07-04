using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Context;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Tools;

namespace Orkeon.Infrastructure.Agent;

/// <summary>
/// Provides delegation tools to agents that allow delegation.
/// </summary>
public partial class AgentDelegationToolsProvider
{
    private readonly IAgentCommunicationService _communicationService;
    private readonly IAgentExecutionService _agentExecutionService;
    private readonly ILogger<AgentDelegationToolsProvider> _logger;
    private readonly Dictionary<string, AgentId> _agentRoleMapping = [];
    private readonly Dictionary<AgentId, DomainAgent> _agentEntities = [];
    private SimpleExecutionContext? _currentContext;

    /// <summary>Initializes a new instance of <see cref="AgentDelegationToolsProvider"/>.</summary>
    /// <param name="communicationService">The agent communication service.</param>
    /// <param name="agentExecutionService">The agent execution service for synchronous delegation.</param>
    /// <param name="logger">The logger.</param>
    public AgentDelegationToolsProvider(
        IAgentCommunicationService communicationService,
        IAgentExecutionService agentExecutionService,
        ILogger<AgentDelegationToolsProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(communicationService);
        _communicationService = communicationService;
        ArgumentNullException.ThrowIfNull(agentExecutionService);
        _agentExecutionService = agentExecutionService;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Registers an agent with its role for delegation purposes.
    /// </summary>
    public void RegisterAgent(AgentId agentId, string role)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

#pragma warning disable CA1308 // lowercase is the required role dictionary-key storage form, not a comparison normalization
        _agentRoleMapping[role.ToLowerInvariant()] = agentId;
#pragma warning restore CA1308
        LogRegisteredAgentWithRole(agentId, role);
    }

    /// <summary>
    /// Adds delegation tools to an agent if it allows delegation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the disposable DelegateWorkTool/AskQuestionTool is transferred to the agent via AddTool; they live in the agent's tool list for the agent's lifetime and are not owned by this method.")]
    public void AddDelegationToolsToAgent(DomainAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (!agent.AllowDelegation)
        {
            LogAgentDoesNotAllowDelegation(agent.Id);
            return;
        }

        // Create delegation tools. Ownership of both disposable tools is transferred to the
        // agent via AddTool (they live in the agent's tool list for the agent's lifetime);
        // disposing them here would tear down tools the agent still holds.
        var delegateWorkTool = new DelegateWorkTool(
            _communicationService,
            agent.Id,
            FindAgentByRole,
            _agentExecutionService,
            FindAgentById,
            () => _currentContext,
            _logger as ILogger<DelegateWorkTool>
        );

        var askQuestionTool = new AskQuestionTool(
            _communicationService,
            agent.Id,
            FindAgentByRole,
            _logger as ILogger<AskQuestionTool>
        );

        // Add tools to agent (ownership transfer — see comment above)
        agent.AddTool(delegateWorkTool);
        agent.AddTool(askQuestionTool);

        LogAddedDelegationToolsToAgent(agent.Id, agent.Role);
    }

    /// <summary>
    /// Registers an agent entity for synchronous delegation lookup.
    /// </summary>
    public void RegisterAgentEntity(DomainAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agentEntities[agent.Id] = agent;
        RegisterAgent(agent.Id, agent.Role);
    }

    /// <summary>
    /// Updates the current execution context for synchronous delegation.
    /// Not thread-safe — designed for sequential process only.
    /// </summary>
    public void UpdateExecutionContext(SimpleExecutionContext context)
        => _currentContext = context;

    /// <summary>
    /// Finds an agent by role.
    /// </summary>
    private AgentId? FindAgentByRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

#pragma warning disable CA1308 // lowercase matches the stored role dictionary-key form, not a comparison normalization
        var normalizedRole = role.ToLowerInvariant();
#pragma warning restore CA1308
        return _agentRoleMapping.TryGetValue(normalizedRole, out var agentId) ? agentId : null;
    }

    private DomainAgent? FindAgentById(AgentId id)
        => _agentEntities.TryGetValue(id, out var agent) ? agent : null;

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Registered agent {AgentId} with role {Role}")]
    private partial void LogRegisteredAgentWithRole(object agentId, object role);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Agent {AgentId} does not allow delegation, skipping delegation tools")]
    private partial void LogAgentDoesNotAllowDelegation(object agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Added delegation tools to agent {AgentId} (role: {Role})")]
    private partial void LogAddedDelegationToolsToAgent(object agentId, object role);

}
