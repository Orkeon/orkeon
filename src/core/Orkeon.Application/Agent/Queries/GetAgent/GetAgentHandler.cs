using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Domain.Common;
using Orkeon.Domain.Agent;
using Orkeon.Application.Constants.Execution;

namespace Orkeon.Application.Agent.Queries.GetAgent;

/// <summary>
/// Handler for getting an agent by ID.
/// </summary>
public partial class GetAgentHandler : IQueryHandler<GetAgentQuery, AgentDto?>
{
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<GetAgentHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GetAgentHandler"/>.
    /// </summary>
    public GetAgentHandler(
        IAgentRepository agentRepository,
        ILogger<GetAgentHandler> logger)
    {
        _agentRepository = agentRepository;
        _logger = logger;
    }

    /// <summary>
    /// Handle Async.
    /// </summary>
    public System.Threading.Tasks.Task<AgentDto?> HandleAsync(
        GetAgentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return HandleCoreAsync();

        async System.Threading.Tasks.Task<AgentDto?> HandleCoreAsync()
        {
            LogGettingAgent(query.Id);

            var agentId = AgentId.From(query.Id);
            var agent = await _agentRepository.GetByIdAsync(agentId, cancellationToken).ConfigureAwait(false);

            if (agent == null)
            {
                LogAgentNotFound(query.Id);
                return null;
            }

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
                Tools = agent.Tools?.Select(t => t.Name).ToImmutableList() ?? [],
                CreatedAt = agent.CreatedAt,
                UpdatedAt = agent.UpdatedAt
            };
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Getting agent with ID {Id}")]
    private partial void LogGettingAgent(Guid id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent with ID {Id} not found")]
    private partial void LogAgentNotFound(Guid id);
}
