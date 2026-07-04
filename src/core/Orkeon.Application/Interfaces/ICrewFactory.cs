using Orkeon.Domain.Configuration;

namespace Orkeon.Application.Interfaces;

/// <summary>
/// Factory for creating domain Crew objects from configuration.
/// </summary>
public interface ICrewFactory
{
    /// <summary>
    /// Creates a domain Crew from a CrewConfiguration.
    /// </summary>
    System.Threading.Tasks.Task<Domain.Crew.Crew> CreateFromConfigAsync(CrewConfiguration config, CancellationToken ct = default);

    /// <summary>
    /// Creates a domain Crew from a YAML file.
    /// </summary>
    System.Threading.Tasks.Task<Domain.Crew.Crew> CreateFromFileAsync(string yamlFilePath, CancellationToken ct = default);

    /// <summary>
    /// Creates a domain Crew from a directory containing crew.yaml, agents.yaml, and tasks.yaml.
    /// </summary>
    System.Threading.Tasks.Task<Domain.Crew.Crew> CreateFromDirectoryAsync(string directoryPath, CancellationToken ct = default);
}
