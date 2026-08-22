using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Crew.DTOs;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Microsoft.Extensions.Logging;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainProcessType = Orkeon.Domain.SharedKernel.ValueObjects.ProcessType;

namespace Orkeon.Application.Crew.Commands.CreateCrew;

/// <summary>
/// Handler for creating a new crew.
/// </summary>
public partial class CreateCrewHandler : ICommandHandler<CreateCrewCommand, CrewDto>
{
    private readonly ICrewRepository _crewRepository;
    private readonly ILogger<CreateCrewHandler> _logger;
    private readonly EventHub.ICrewLinkRegistry? _linkRegistry;

    /// <summary>
    /// Initializes a new instance of <see cref="CreateCrewHandler"/>.
    /// </summary>
    public CreateCrewHandler(
        ICrewRepository crewRepository,
        ILogger<CreateCrewHandler> logger,
        EventHub.ICrewLinkRegistry? linkRegistry = null)
    {
        _crewRepository = crewRepository;
        _logger = logger;
        _linkRegistry = linkRegistry;
    }

    /// <summary>
    /// Handle Async.
    /// </summary>
    public System.Threading.Tasks.Task<CrewDto> HandleAsync(
        CreateCrewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HandleCoreAsync();

        async System.Threading.Tasks.Task<CrewDto> HandleCoreAsync()
        {
            LogCreatingCrew(command.Name, command.Goal);

            // Create the crew domain entity
            var crew = DomainCrew.Create(
                goal: command.Goal,
                processType: command.ProcessType,
                verbose: false);

            // Add agents if provided (validator guarantees all IDs are valid GUIDs)
            if (command.AgentIds?.Count > 0)
            {
                foreach (var agentIdStr in command.AgentIds)
                {
                    var agentId = AgentId.From(Guid.Parse(agentIdStr));
                    crew.AddAgent(agentId);
                }
            }

            // Save the crew
            await _crewRepository.AddAsync(crew, cancellationToken).ConfigureAwait(false);

            // Links name crews by name while messages carry ids, so even a crew created
            // through the API — which cannot declare links — must be resolvable as somebody
            // else's target. Without this, the first YAML crew to declare a `links:` block
            // silently made every API-created crew unreachable.
            if (!string.IsNullOrWhiteSpace(command.Name))
                _linkRegistry?.Register(crew.Id, command.Name, links: null);

            // Map to DTO and return
            return new CrewDto
            {
                Id = crew.Id.ToString(),
                Name = command.Name,
                Description = command.Goal,
                ProcessType = MapProcessType(command.ProcessType),
                Status = "Idle",
                Verbosity = crew.Verbose ? "verbose" : "normal",
                CreatedAt = crew.CreatedAt
            };
        }
    }

    private static string MapProcessType(DomainProcessType domainType) =>
        domainType.Value switch
        {
            "Sequential" => "Sequential",
            "Parallel" => "Parallel",
            "Hierarchical" => "Hierarchical",
            "Consensual" => "Consensual",
            _ => "Sequential"
        };

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating crew with name {Name} and goal {Goal}")]
    private partial void LogCreatingCrew(string name, string goal);
}
