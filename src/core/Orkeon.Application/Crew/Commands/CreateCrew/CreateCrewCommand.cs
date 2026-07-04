using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Crew.DTOs;

namespace Orkeon.Application.Crew.Commands.CreateCrew;

/// <summary>
/// Command to create a new crew.
/// </summary>
public record CreateCrewCommand(
    string Name,
    string Goal,
    Domain.SharedKernel.ValueObjects.ProcessType ProcessType,
    IReadOnlyList<string>? AgentIds
) : ICommand<CrewDto>;
