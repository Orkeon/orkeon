using Orkeon.Application.Interfaces;
using Orkeon.Domain.Configuration;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for ICrewDefinitionLoader with call tracking and configurable results.
/// </summary>
public class MockCrewDefinitionLoader : ICrewDefinitionLoader
{
    private CrewConfiguration _loadResult = new();
    private CrewDefinitionValidationResult _validateResult = new(true, Array.Empty<string>(), Array.Empty<string>());

    // --- Tracking ---
    public int LoadFromFileCallCount { get; private set; }
    public string? LastLoadedFilePath { get; private set; }

    public int LoadFromDirectoryCallCount { get; private set; }
    public string? LastLoadedDirectoryPath { get; private set; }

    public int LoadFromStringCallCount { get; private set; }
    public string? LastLoadedYamlContent { get; private set; }

    public int ValidateCallCount { get; private set; }
    public CrewConfiguration? LastValidatedConfig { get; private set; }

    // --- Configuration ---
    public void SetLoadResult(CrewConfiguration result) => _loadResult = result;

    public void SetValidateResult(CrewDefinitionValidationResult result) => _validateResult = result;

    public void SetValidateErrors(params string[] errors) =>
        _validateResult = new CrewDefinitionValidationResult(false, errors, Array.Empty<string>());

    // --- ICrewDefinitionLoader ---
    public Task<CrewConfiguration> LoadFromFileAsync(string filePath, CancellationToken ct = default)
    {
        LoadFromFileCallCount++;
        LastLoadedFilePath = filePath;
        return Task.FromResult(_loadResult);
    }

    public Task<CrewConfiguration> LoadFromDirectoryAsync(string directoryPath, CancellationToken ct = default)
    {
        LoadFromDirectoryCallCount++;
        LastLoadedDirectoryPath = directoryPath;
        return Task.FromResult(_loadResult);
    }

    public Task<CrewConfiguration> LoadFromStringAsync(string yamlContent, CancellationToken ct = default)
    {
        LoadFromStringCallCount++;
        LastLoadedYamlContent = yamlContent;
        return Task.FromResult(_loadResult);
    }

    public CrewDefinitionValidationResult Validate(CrewConfiguration config)
    {
        ValidateCallCount++;
        LastValidatedConfig = config;
        return _validateResult;
    }
}
