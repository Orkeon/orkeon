using Microsoft.Extensions.Logging;
using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Crew.DTOs;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;

namespace Orkeon.Application.Crew.Queries.GetCrew;

/// <summary>
/// Handler for getting a crew by ID.
/// </summary>
public partial class GetCrewHandler : IQueryHandler<GetCrewQuery, CrewDto?>
{
    private readonly ICrewRepository _crewRepository;
    private readonly ILogger<GetCrewHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GetCrewHandler"/>.
    /// </summary>
    public GetCrewHandler(
        ICrewRepository crewRepository,
        ILogger<GetCrewHandler> logger)
    {
        _crewRepository = crewRepository;
        _logger = logger;
    }

    /// <summary>
    /// Handle Async.
    /// </summary>
    public System.Threading.Tasks.Task<CrewDto?> HandleAsync(
        GetCrewQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return HandleCoreAsync();

        async System.Threading.Tasks.Task<CrewDto?> HandleCoreAsync()
        {
            LogGettingCrew(query.Id);

            var crewId = CrewId.From(query.Id);
            var crew = await _crewRepository.GetByIdAsync(crewId, cancellationToken).ConfigureAwait(false);

            if (crew == null)
            {
                LogCrewNotFound(query.Id);
                return null;
            }

            // Map to DTO and return
            return new CrewDto
            {
                Id = crew.Id.ToString(),
                Name = string.Empty,
                Description = crew.Goal.Value,
                ProcessType = crew.ProcessType.Value,
                Status = MapCrewStatus(crew.Status),
                Verbosity = crew.Verbose ? "verbose" : "normal",
                CreatedAt = crew.CreatedAt
            };
        }
    }

    private static string MapCrewStatus(Domain.Crew.ValueObjects.CrewStatus domainStatus) =>
        domainStatus.Value switch
        {
            "Idle" => "Idle",
            "Executing" => "Executing",
            "Completed" => "Completed",
            "Failed" => "Failed",
            "Paused" => "Paused",
            "Cancelled" => "Cancelled",
            _ => "Idle"
        };

    [LoggerMessage(Level = LogLevel.Information, Message = "Getting crew with ID {Id}")]
    private partial void LogGettingCrew(Guid id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew with ID {Id} not found")]
    private partial void LogCrewNotFound(Guid id);
}
