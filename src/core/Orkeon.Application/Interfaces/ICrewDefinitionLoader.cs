using Orkeon.Domain.Configuration;

namespace Orkeon.Application.Interfaces;

/// <summary>
/// Validation result for crew definition loading.
/// </summary>
public record CrewDefinitionValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Interface for loading crew definitions from YAML files and strings.
/// </summary>
public interface ICrewDefinitionLoader
{
    /// <summary>
    /// Loads a crew configuration from a single YAML file.
    /// </summary>
    System.Threading.Tasks.Task<CrewConfiguration> LoadFromFileAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Loads a crew configuration from a directory containing crew.yaml, agents.yaml, and tasks.yaml.
    /// </summary>
    System.Threading.Tasks.Task<CrewConfiguration> LoadFromDirectoryAsync(string directoryPath, CancellationToken ct = default);

    /// <summary>
    /// Loads a crew configuration from a YAML string.
    /// </summary>
    System.Threading.Tasks.Task<CrewConfiguration> LoadFromStringAsync(string yamlContent, CancellationToken ct = default);

    /// <summary>
    /// Validates a crew configuration and returns errors and warnings.
    /// </summary>
    CrewDefinitionValidationResult Validate(CrewConfiguration config);
}
