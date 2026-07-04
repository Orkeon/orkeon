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

            // Map to DTO and return
            return new AgentDto
            {
                Id = agent.Id.ToString(),
                Name = agent.Role.Value,
                Role = agent.Role.Value,
                Goal = agent.Goal.Value,
                Backstory = agent.Backstory?.Value ?? string.Empty,
                Type = "standard",
                Status = agent.Status.ToString(),
                Verbose = agent.Verbose,
                AllowDelegation = agent.AllowDelegation,
                MaxExecutionTime = ExecutionDefaults.DefaultMaxExecutionSeconds,
                Tools = (command.Tools ?? []).ToImmutableList(),
                CreatedAt = agent.CreatedAt,
                UpdatedAt = agent.UpdatedAt
            };
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating agent with role {Role} and goal {Goal}")]
    private partial void LogCreatingAgent(string role, string goal);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tool {ToolName} would be added to agent")]
    private partial void LogToolWouldBeAdded(string toolName);
}
