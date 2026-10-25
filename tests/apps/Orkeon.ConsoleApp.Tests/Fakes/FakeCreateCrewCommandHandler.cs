using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Crew.Commands.CreateCrew;
using Orkeon.Application.Crew.DTOs;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>Answers a create-crew command with a DTO built from it and records each command.</summary>
internal sealed class FakeCreateCrewCommandHandler : ICommandHandler<CreateCrewCommand, CrewDto>
{
    public List<CreateCrewCommand> Received { get; } = [];

    public System.Threading.Tasks.Task<CrewDto> HandleAsync(CreateCrewCommand command, CancellationToken cancellationToken = default)
    {
        Received.Add(command);
        return System.Threading.Tasks.Task.FromResult(new CrewDto
        {
            Id = $"crew-{Received.Count}",
            Name = command.Name,
            Description = command.Goal,
            ProcessType = command.ProcessType.Value,
            Status = "Ready",
            Verbosity = "Normal",
        });
    }
}
