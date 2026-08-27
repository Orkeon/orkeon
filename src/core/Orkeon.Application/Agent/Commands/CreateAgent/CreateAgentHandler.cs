using System.Collections.Immutable;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Application.Constants.Execution;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Agent.Commands.CreateAgent;

/// <summary>
/// Handler for creating a new agent.
/// </summary>
public partial class CreateAgentHandler : ICommandHandler<CreateAgentCommand, AgentDto>
{
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<CreateAgentHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CreateAgentHandler"/>.
    /// </summary>
    public CreateAgentHandler(
        IAgentRepository agentRepository,
        ILogger<CreateAgentHandler> logger)
    {
        _agentRepository = agentRepository;
        _logger = logger;
    }

    /// <summary>
    /// Handle Async.
    /// </summary>
    public System.Threading.Tasks.Task<AgentDto> HandleAsync(
        CreateAgentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HandleCoreAsync();

        async System.Threading.Tasks.Task<AgentDto> HandleCoreAsync()
        {
            LogCreatingAgent(command.Role, command.Goal);

            // Create value objects
            var role = AgentRole.From(command.Role);
            var goal = AgentGoal.From(command.Goal);

            // Create the agent domain entity
            var backstory = !string.IsNullOrWhiteSpace(command.Backstory)
                ? AgentBackstory.From(command.Backstory)
                : null;
            var agent = DomainAgent.Create(
                role: role,
                goal: goal,
                backstory: backstory,
                verbose: true);

            // Add tools if provided
            if (command.Tools?.Count > 0)
            {
                foreach (var toolName in command.Tools)
                {
                    LogToolWouldBeAdded(toolName);
                }
            }

            // Save the agent
            await _agentRepository.AddAsync(agent, cancellationToken).ConfigureAwait(false);

            // The requested tool NAMES, not the agent's resolved tools: this handler logs
            // "tool would be added" rather than resolving them, so the DTO reports what the
            // caller asked for.
            return Common.Mapping.AgentMapper.ToDto(agent) with
            {
                Tools = (command.Tools ?? []).ToImmutableList(),
            };
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating agent with role {Role} and goal {Goal}")]
    private partial void LogCreatingAgent(string role, string goal);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tool {ToolName} would be added to agent")]
    private partial void LogToolWouldBeAdded(string toolName);
}
