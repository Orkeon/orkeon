using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Infrastructure.Configuration;


/// <summary>
/// Loads crew definitions from YAML files and strings.
/// </summary>
public partial class YamlCrewDefinitionLoader : ICrewDefinitionLoader
{
    private readonly IYamlSerializer _yamlSerializer;
    private readonly IFileSystemService _fs;
    private readonly ILogger<YamlCrewDefinitionLoader> _logger;
    private readonly YamlCrewMapper _mapper;

    /// <summary>Initializes a new instance of <see cref="YamlCrewDefinitionLoader"/>.</summary>
    /// <param name="yamlSerializer">The YAML serializer.</param>
    /// <param name="fs">The virtual file system service.</param>
    /// <param name="logger">The logger.</param>
    public YamlCrewDefinitionLoader(
        IYamlSerializer yamlSerializer,
        IFileSystemService fs,
        ILogger<YamlCrewDefinitionLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(yamlSerializer);
        _yamlSerializer = yamlSerializer;
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _mapper = new YamlCrewMapper(logger);
    }

    /// <inheritdoc />
    public async Task<CrewConfiguration> LoadFromFileAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!await _fs.ExistsAsync(filePath, ct).ConfigureAwait(false))
            throw new FileNotFoundException($"Crew definition file not found: {filePath}", filePath);

        LogLoadingCrewDefinitionFromFile(filePath);

        var yamlContent = await _fs.TryReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        return await LoadFromStringAsync(yamlContent!, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CrewConfiguration> LoadFromDirectoryAsync(string directoryPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        if (!await _fs.ExistsAsync(directoryPath, ct).ConfigureAwait(false))
            throw new DirectoryNotFoundException($"Crew definition directory not found: {directoryPath}");

        return await LoadFromDirectoryCoreAsync(directoryPath, ct).ConfigureAwait(false);
    }

    private async Task<CrewConfiguration> LoadFromDirectoryCoreAsync(string directoryPath, CancellationToken ct)
    {
        LogLoadingCrewDefinitionFromDirectory(directoryPath);

        var crewFilePath = directoryPath.TrimEnd('/') + "/crew.yaml";
        var agentsFilePath = directoryPath.TrimEnd('/') + "/agents.yaml";
        var tasksFilePath = directoryPath.TrimEnd('/') + "/tasks.yaml";

        var crewYaml = await _fs.TryReadAllTextAsync(crewFilePath, ct).ConfigureAwait(false);
        if (crewYaml is null)
            throw new FileNotFoundException($"crew.yaml not found in directory: {directoryPath}", crewFilePath);

        var agentsYaml = await _fs.TryReadAllTextAsync(agentsFilePath, ct).ConfigureAwait(false);
        if (agentsYaml is null)
            throw new FileNotFoundException($"agents.yaml not found in directory: {directoryPath}", agentsFilePath);

        var tasksYaml = await _fs.TryReadAllTextAsync(tasksFilePath, ct).ConfigureAwait(false);
        if (tasksYaml is null)
            throw new FileNotFoundException($"tasks.yaml not found in directory: {directoryPath}", tasksFilePath);

        crewYaml = YamlAnchorPreprocessor.Preprocess(crewYaml);
        agentsYaml = YamlAnchorPreprocessor.Preprocess(agentsYaml);
        tasksYaml = YamlAnchorPreprocessor.Preprocess(tasksYaml);

        var crewSettings = _yamlSerializer.Deserialize<CrewSettingsYamlConfig>(crewYaml);
        var agents = _yamlSerializer.Deserialize<Dictionary<string, AgentYamlConfig>>(agentsYaml);
        var tasks = _yamlSerializer.Deserialize<Dictionary<string, TaskYamlConfig>>(tasksYaml);

        var config = _mapper.BuildConfiguration(
            new CrewMappingSettings
            {
                Name = crewSettings?.Name,
                Goal = crewSettings?.Goal,
                Process = crewSettings?.Process,
                Verbose = crewSettings?.Verbose,
                Memory = crewSettings?.Memory,
                MemoryProvider = crewSettings?.MemoryProvider,
                Planning = crewSettings?.Planning,
                ManagerAgent = crewSettings?.ManagerAgent,
                CircuitBreaker = crewSettings?.CircuitBreaker,
                GraphConfig = crewSettings?.GraphConfig,
                CrewDefaultLlm = crewSettings?.Llm,
            },
            agents,
            tasks);

        LogLoadedCrewDefinitionFromDirectory(config.Agents.Count, config.Tasks.Count);

        return config;
    }

    /// <inheritdoc />
    public Task<CrewConfiguration> LoadFromStringAsync(string yamlContent, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlContent);

        yamlContent = YamlAnchorPreprocessor.Preprocess(yamlContent);
        var crewYaml = _yamlSerializer.Deserialize<CrewYamlConfig>(yamlContent);

        var config = _mapper.BuildConfiguration(
            new CrewMappingSettings
            {
                Name = crewYaml?.Name,
                Goal = crewYaml?.Goal,
                Process = crewYaml?.Process,
                Verbose = crewYaml?.Verbose,
                Memory = crewYaml?.Memory,
                MemoryProvider = crewYaml?.MemoryProvider,
                Planning = crewYaml?.Planning,
                ManagerAgent = crewYaml?.ManagerAgent,
                CircuitBreaker = crewYaml?.CircuitBreaker,
                GraphConfig = crewYaml?.GraphConfig,
                CrewDefaultLlm = crewYaml?.Llm,
            },
            crewYaml?.Agents,
            crewYaml?.Tasks);

        LogLoadedCrewDefinitionFromString(config.Agents.Count, config.Tasks.Count);

        return System.Threading.Tasks.Task.FromResult(config);
    }

    /// <inheritdoc />
    public CrewDefinitionValidationResult Validate(CrewConfiguration config)
        => CrewDefinitionValidator.Validate(config);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Loading crew definition from file: {FilePath}")]
    private partial void LogLoadingCrewDefinitionFromFile(object filePath);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Loading crew definition from directory: {DirectoryPath}")]
    private partial void LogLoadingCrewDefinitionFromDirectory(object directoryPath);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Loaded crew definition from directory with {AgentCount} agents and {TaskCount} tasks")]
    private partial void LogLoadedCrewDefinitionFromDirectory(int agentCount, int taskCount);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Loaded crew definition from string with {AgentCount} agents and {TaskCount} tasks")]
    private partial void LogLoadedCrewDefinitionFromString(int agentCount, int taskCount);

}
