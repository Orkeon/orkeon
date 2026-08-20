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

        var root = directoryPath.TrimEnd('/');

        // Per-entity layout: a crew is described by config.yaml/crew.yaml + agents/*.yaml + tasks/*.yaml.
        // It is selected as soon as an agents/ or tasks/ sub-directory exists; otherwise we fall back
        // to the flat legacy triplet (crew.yaml + agents.yaml + tasks.yaml), byte-for-byte unchanged.
        var agentsDirExists = await IsDirectoryAsync(root + "/agents", ct).ConfigureAwait(false);
        var tasksDirExists = await IsDirectoryAsync(root + "/tasks", ct).ConfigureAwait(false);

        if (agentsDirExists || tasksDirExists)
            return await LoadPerEntityDirectoryAsync(root, ct).ConfigureAwait(false);

        return await LoadFlatDirectoryAsync(root, directoryPath, ct).ConfigureAwait(false);
    }

    private async Task<CrewConfiguration> LoadFlatDirectoryAsync(string root, string directoryPath, CancellationToken ct)
    {
        var crewFilePath = root + "/crew.yaml";
        var agentsFilePath = root + "/agents.yaml";
        var tasksFilePath = root + "/tasks.yaml";

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

        return BuildFromSettings(crewSettings, agents, tasks);
    }

    private async Task<CrewConfiguration> LoadPerEntityDirectoryAsync(string root, CancellationToken ct)
    {
        // Guard against a mixed tree — a flat file and its per-entity directory side by side would be
        // ambiguous, so we refuse rather than pick a silent precedence.
        await ThrowIfMixedAsync(root, "agents", ct).ConfigureAwait(false);
        await ThrowIfMixedAsync(root, "tasks", ct).ConfigureAwait(false);

        // Crew settings: config.yaml is preferred, crew.yaml is accepted as a fallback name.
        var settingsPath = root + "/config.yaml";
        var settingsYaml = await _fs.TryReadAllTextAsync(settingsPath, ct).ConfigureAwait(false);
        if (settingsYaml is null)
        {
            settingsPath = root + "/crew.yaml";
            settingsYaml = await _fs.TryReadAllTextAsync(settingsPath, ct).ConfigureAwait(false);
        }
        if (settingsYaml is null)
            throw new FileNotFoundException(
                $"config.yaml (or crew.yaml) not found in directory: {root}", root + "/config.yaml");

        settingsYaml = YamlAnchorPreprocessor.Preprocess(settingsYaml);
        var crewSettings = _yamlSerializer.Deserialize<CrewSettingsYamlConfig>(settingsYaml);

        var agents = await LoadEntityFolderAsync<AgentYamlConfig>(root + "/agents", ct).ConfigureAwait(false);
        var tasks = await LoadEntityFolderAsync<TaskYamlConfig>(root + "/tasks", ct).ConfigureAwait(false);

        return BuildFromSettings(crewSettings, agents, tasks);
    }

    /// <summary>
    /// Enumerates <c>{folder}/*.yaml</c> (non-recursive, ordinally sorted), using each file-name stem as the
    /// entity key — the equivalent of the dictionary key in the flat single-file layout. Anchors are
    /// preprocessed per file (they cannot span files). An absent or empty folder yields an empty dictionary.
    /// </summary>
    private async Task<Dictionary<string, T>> LoadEntityFolderAsync<T>(string folder, CancellationToken ct)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);

        if (!await IsDirectoryAsync(folder, ct).ConfigureAwait(false))
            return result;

        var paths = new List<string>();
        await foreach (var entry in _fs.EnumerateFilesAsync(
                           folder, new VirtualEnumerationOptions(Recursive: false, SearchPattern: "*.yaml"), ct)
                           .ConfigureAwait(false))
        {
            if (entry.Kind == VirtualEntryKind.File)
                paths.Add(entry.VirtualPath);
        }
        paths.Sort(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var yaml = await _fs.TryReadAllTextAsync(path, ct).ConfigureAwait(false);
            if (yaml is null)
                continue;

            yaml = YamlAnchorPreprocessor.Preprocess(yaml);
            result[StemOf(path)] = _yamlSerializer.Deserialize<T>(yaml);
        }

        return result;
    }

    private CrewConfiguration BuildFromSettings(
        CrewSettingsYamlConfig? crewSettings,
        Dictionary<string, AgentYamlConfig>? agents,
        Dictionary<string, TaskYamlConfig>? tasks)
    {
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
                Rag = crewSettings?.Rag,
                Links = crewSettings?.Links,
            },
            agents,
            tasks);

        LogLoadedCrewDefinitionFromDirectory(config.Agents.Count, config.Tasks.Count);

        return config;
    }

    private async Task ThrowIfMixedAsync(string root, string entity, CancellationToken ct)
    {
        var flatFile = $"{root}/{entity}.yaml";
        var dir = $"{root}/{entity}";
        if (await IsDirectoryAsync(dir, ct).ConfigureAwait(false) &&
            await _fs.ExistsAsync(flatFile, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Mixed crew layout: both '{flatFile}' and '{dir}/' exist. " +
                "Use either the flat single-file layout or the per-entity directory layout, not both.");
        }
    }

    private async Task<bool> IsDirectoryAsync(string path, CancellationToken ct)
    {
        if (!await _fs.ExistsAsync(path, ct).ConfigureAwait(false))
            return false;
        return await _fs.GetEntryKindAsync(path, ct).ConfigureAwait(false) == VirtualEntryKind.Directory;
    }

    private static string StemOf(string virtualPath)
    {
        var name = virtualPath[(virtualPath.LastIndexOf('/') + 1)..];
        return name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ? name[..^".yaml".Length] : name;
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
                Rag = crewYaml?.Rag,
                Links = crewYaml?.Links,
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
