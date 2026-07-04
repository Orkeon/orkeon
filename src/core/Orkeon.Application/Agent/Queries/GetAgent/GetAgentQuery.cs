using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Agent.DTOs;

namespace Orkeon.Application.Agent.Queries.GetAgent;

/// <summary>
/// Query to get an agent by ID.
/// </summary>
public record GetAgentQuery(Guid Id) : IQuery<AgentDto?>;
