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
    private readonly Orkeon.Application.Interfaces.Ports.IToolDecorator? _toolDecorator;
    private readonly Dictionary<string, AgentId> _agentRoleMapping = [];
    private readonly Dictionary<AgentId, DomainAgent> _agentEntities = [];
    private SimpleExecutionContext? _currentContext;

    /// <summary>
    /// The context of the task a flow running alongside the run serves — a task launched with
    /// <c>asyncExecution</c> (GAP-22) —, which wins over <see cref="_currentContext"/> on that flow.
    /// </summary>
    private readonly AsyncLocal<SimpleExecutionContext?> _flowContext = new();

    /// <summary>Initializes a new instance of <see cref="AgentDelegationToolsProvider"/>.</summary>
    /// <param name="communicationService">The agent communication service.</param>
    /// <param name="agentExecutionService">The agent execution service for synchronous delegation.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="toolDecorator">Optional observer seam wrapping the per-agent tools (BUS-03).</param>
    public AgentDelegationToolsProvider(
        IAgentCommunicationService communicationService,
        IAgentExecutionService agentExecutionService,
        ILogger<AgentDelegationToolsProvider> logger,
        Orkeon.Application.Interfaces.Ports.IToolDecorator? toolDecorator = null)
    {
        ArgumentNullException.ThrowIfNull(communicationService);
        _communicationService = communicationService;
        ArgumentNullException.ThrowIfNull(agentExecutionService);
        _agentExecutionService = agentExecutionService;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _toolDecorator = toolDecorator;
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
    /// Adds delegation tools to an agent if it allows delegation. The tools an earlier run gave the
    /// same agent — a crew kicked off again without being reloaded keeps its agents — are replaced:
    /// theirs read that run's context, these read this one's (GAP-21).
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
        // agent via AddTool (they live in the agent's tool list for the agent's lifetime)  —
        // disposing them here would tear down tools the agent still holds.
        var delegateWorkTool = new DelegateWorkTool(
            _communicationService,
            agent.Id,
            FindAgentByRole,
            _agentExecutionService,
            FindAgentById,
            () => _flowContext.Value ?? _currentContext,
            _logger as ILogger<DelegateWorkTool>
        );

        var askQuestionTool = new AskQuestionTool(
            _communicationService,
            agent.Id,
            FindAgentByRole,
            _logger as ILogger<AskQuestionTool>
        );

        ReplaceEarlierRunsTool(agent, delegateWorkTool.Name);
        ReplaceEarlierRunsTool(agent, askQuestionTool.Name);

        // Add tools to agent (ownership transfer — see comment above). Decorated when an
        // observer is registered: these two are built per agent with `new`, so the DI-level
        // decoration that covers every registered tool never sees them — and a delegation is
        // exactly the call BUS-03's watcher cannot afford to miss.
        agent.AddTool(Decorate(delegateWorkTool));
        agent.AddTool(Decorate(askQuestionTool));

        LogAddedDelegationToolsToAgent(agent.Id, agent.Role);
    }

    private Orkeon.Domain.Tools.IBaseTool Decorate(Orkeon.Domain.Tools.IBaseTool tool) =>
        _toolDecorator?.Decorate(tool) ?? tool;

    /// <summary>
    /// Removes the tool named <paramref name="toolName"/> an earlier run gave <paramref name="agent"/>,
    /// and disposes it — the agent owned it.
    /// </summary>
    private static void ReplaceEarlierRunsTool(DomainAgent agent, string toolName)
    {
        var earlier = agent.Tools.FirstOrDefault(t => t.Name == toolName);
        if (earlier is null)
            return;

        agent.RemoveTool(toolName);
        (earlier as IDisposable)?.Dispose();
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
    /// Updates the current execution context for synchronous delegation: the context of the task the
    /// run's own flow is running. Not thread-safe — set from that flow only; a task running alongside
    /// it carries its own (<see cref="UseExecutionContextOnThisFlow"/>).
    /// </summary>
    public void UpdateExecutionContext(SimpleExecutionContext context)
        => _currentContext = context;

    /// <summary>
    /// Gives the delegations made on the calling async flow the context of the task that flow runs.
    /// A task launched with <c>asyncExecution</c> runs on its own flow while the run moves on to the
    /// next tasks and their contexts; a coworker it delegates to works in the context of the task it
    /// serves, never in the one the run has reached (GAP-21, GAP-22). Called from that task's own
    /// flow: the run's flow never sees it.
    /// </summary>
    internal void UseExecutionContextOnThisFlow(SimpleExecutionContext context)
        => _flowContext.Value = context;

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
