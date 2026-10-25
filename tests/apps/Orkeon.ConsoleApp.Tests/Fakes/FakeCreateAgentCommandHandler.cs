using Orkeon.Application.Agent.Commands.CreateAgent;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Common.CQRS;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>Answers a create-agent command with a DTO built from it and records each command.</summary>
internal sealed class FakeCreateAgentCommandHandler : ICommandHandler<CreateAgentCommand, AgentDto>
{
    public List<CreateAgentCommand> Received { get; } = [];

    public System.Threading.Tasks.Task<AgentDto> HandleAsync(CreateAgentCommand command, CancellationToken cancellationToken = default)
    {
        Received.Add(command);
        return System.Threading.Tasks.Task.FromResult(new AgentDto
        {
            Id = $"agent-{Received.Count}-{command.Role}",
            Name = command.Role,
            Role = command.Role,
            Goal = command.Goal,
            Backstory = command.Backstory ?? string.Empty,
            Type = "Standard",
            Status = "Idle",
        });
    }
}
