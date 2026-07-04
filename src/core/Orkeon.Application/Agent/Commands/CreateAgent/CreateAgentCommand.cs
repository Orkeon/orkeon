using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Agent.DTOs;

namespace Orkeon.Application.Agent.Commands.CreateAgent;

/// <summary>
/// Command to create a new agent.
/// </summary>
public record CreateAgentCommand(
    string Role,
    string Goal,
    string? Backstory,
    IReadOnlyList<string>? Tools
) : ICommand<AgentDto>;
