using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Crew.DTOs;

namespace Orkeon.Application.Crew.Queries.GetCrew;

/// <summary>
/// Query to get a crew by ID.
/// </summary>
public record GetCrewQuery(Guid Id) : IQuery<CrewDto?>;
