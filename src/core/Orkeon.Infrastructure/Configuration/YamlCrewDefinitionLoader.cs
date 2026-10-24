using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Constants.Llm;
using YamlDotNet.Core;

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
        var config = LoadFromYaml(yamlContent!, filePath);
        return WithCrewDirectory(config, DirectoryOf(filePath));
    }

    /// <inheritdoc />
    public async Task<CrewConfiguration> LoadFromDirectoryAsync(string directoryPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        if (!await _fs.ExistsAsync(directoryPath, ct).ConfigureAwait(false))
            throw new DirectoryNotFoundException($"Crew definition directory not found: {directoryPath}");

        var config = await LoadFromDirectoryCoreAsync(directoryPath, ct).ConfigureAwait(false);
        return WithCrewDirectory(config, directoryPath.Length > 1 ? directoryPath.TrimEnd('/') : directoryPath);
    }

    /// <summary>
    /// Records where the crew was read from on its <c>rag:</c> block: a relative source
    /// (<c>./data/faq.txt</c>) resolves against the crew's folder — <c>/crew</c> under
    /// <c>orkeon run crew.yaml</c>, the directory itself for a crew directory (GAP-27). The
    /// sources stay as written; the bootstrapper resolves them.
    /// </summary>
    private static CrewConfiguration WithCrewDirectory(CrewConfiguration config, string? directory)
        => config.Rag is { } rag && directory is not null
            ? config with { Rag = rag with { CrewDirectory = directory } }
            : config;

    /// <summary>The virtual folder of <paramref name="filePath"/>, or null for a bare file name.</summary>
    private static string? DirectoryOf(string filePath)
    {
        var path = filePath.Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        return slash switch
        {
            < 0 => null,
            0 => "/",
            _ => path[..slash],
        };
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

        var crew = new CrewDocument(YamlAnchorPreprocessor.Expand(crewYaml), "", crewFilePath, typeof(CrewSettingsYamlConfig));
        var agentsDocument = new CrewDocument(YamlAnchorPreprocessor.Expand(agentsYaml), "agents", agentsFilePath, typeof(Dictionary<string, AgentYamlConfig>));
        var tasksDocument = new CrewDocument(YamlAnchorPreprocessor.Expand(tasksYaml), "tasks", tasksFilePath, typeof(Dictionary<string, TaskYamlConfig>));
        CheckDocuments([crew, agentsDocument, tasksDocument]);

        var crewSettings = Deserialize<CrewSettingsYamlConfig>(crew);
        var agents = Deserialize<Dictionary<string, AgentYamlConfig>>(agentsDocument);
        var tasks = Deserialize<Dictionary<string, TaskYamlConfig>>(tasksDocument);

        return BuildFromSettings(crewSettings, agents, tasks);
    }

    private async Task<CrewConfiguration> LoadPerEntityDirectoryAsync(string root, CancellationToken ct)
    {
        // Guard against a mixed tree — a flat file and its per-entity directory side by side would be
        // ambiguous, so we refuse rather than pick a silent precedence.
        await ThrowIfMixedAsync(root, "agents", ct).ConfigureAwait(false);
        await ThrowIfMixedAsync(root, "tasks", ct).ConfigureAwait(false);

        // Crew settings: config.yaml is preferred, crew.yaml is accepted as a fallback name.
        var settingsPath = $"{root}/{ConventionalNames.CrewSettingsFile}";
        var settingsYaml = await _fs.TryReadAllTextAsync(settingsPath, ct).ConfigureAwait(false);
        if (settingsYaml is null)
        {
            settingsPath = $"{root}/{ConventionalNames.CrewSettingsFallbackFile}";
            settingsYaml = await _fs.TryReadAllTextAsync(settingsPath, ct).ConfigureAwait(false);
        }
        if (settingsYaml is null)
            throw new FileNotFoundException(
                $"config.yaml (or crew.yaml) not found in directory: {root}", root + "/config.yaml");

        var settings = new CrewDocument(YamlAnchorPreprocessor.Expand(settingsYaml), "", settingsPath, typeof(CrewSettingsYamlConfig));
        var documents = new List<CrewDocument> { settings };

        var agentFiles = await ReadEntityFolderAsync(root + "/agents", typeof(AgentYamlConfig), documents, ct).ConfigureAwait(false);
        var taskFiles = await ReadEntityFolderAsync(root + "/tasks", typeof(TaskYamlConfig), documents, ct).ConfigureAwait(false);
        CheckDocuments(documents);

        var crewSettings = Deserialize<CrewSettingsYamlConfig>(settings);
        var agents = DeserializeEntities<AgentYamlConfig>(agentFiles);
        var tasks = DeserializeEntities<TaskYamlConfig>(taskFiles);

        return BuildFromSettings(crewSettings, agents, tasks);
    }

    /// <summary>
    /// Enumerates <c>{folder}/*.yaml</c> and <c>*.yml</c> (non-recursive, ordinally sorted), using each file-name stem as the
    /// entity key — the equivalent of the dictionary key in the flat single-file layout. Anchors are
    /// preprocessed per file (they cannot span files). An absent or empty folder yields an empty dictionary.
    /// Each file is added to <paramref name="documents"/> with its path in the crew document
    /// (<c>{folder name}.{stem}</c>) for the key checks; the entity key and the text come back for the
    /// deserialization, once every document has been checked.
    /// </summary>
    private async Task<List<(string Stem, CrewDocument Document)>> ReadEntityFolderAsync(
        string folder, Type model, List<CrewDocument> documents, CancellationToken ct)
    {
        var result = new List<(string Stem, CrewDocument Document)>();

        if (!await IsDirectoryAsync(folder, ct).ConfigureAwait(false))
            return result;

        var paths = new List<string>();
        foreach (var pattern in new[] { "*.yaml", "*.yml" })
        {
            await foreach (var entry in _fs.EnumerateFilesAsync(
                               folder, new VirtualEnumerationOptions(Recursive: false, SearchPattern: pattern), ct)
                               .ConfigureAwait(false))
            {
                if (entry.Kind == VirtualEntryKind.File)
                    paths.Add(entry.VirtualPath);
            }
        }
        paths.Sort(StringComparer.Ordinal);

        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!seen.TryAdd(StemOf(path), path))
                throw new InvalidOperationException(
                    $"Entity '{StemOf(path)}' is written under both extensions: {seen[StemOf(path)]} and {path}. Keep one.");
        }

        foreach (var path in paths)
        {
            var yaml = await _fs.TryReadAllTextAsync(path, ct).ConfigureAwait(false);
            if (yaml is null)
                continue;

            var stem = StemOf(path);
            var document = new CrewDocument(YamlAnchorPreprocessor.Expand(yaml), $"{StemOf(folder)}.{stem}", path, model);
            documents.Add(document);
            result.Add((stem, document));
        }

        return result;
    }

    private Dictionary<string, T> DeserializeEntities<T>(List<(string Stem, CrewDocument Document)> files)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (stem, document) in files)
            result[stem] = Deserialize<T>(document);
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
                MaxRpm = crewSettings?.MaxRpm,
                ManagerAgent = crewSettings?.ManagerAgent,
                GraphConfig = crewSettings?.GraphConfig,
                CrewDefaultLlm = crewSettings?.Llm,
                Rag = crewSettings?.Rag,
                Links = crewSettings?.Links,
                Mounts = crewSettings?.Mounts,
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

    private static readonly string[] s_yamlExtensions = [".yaml", ".yml"];

    private static string StemOf(string virtualPath)
    {
        var name = virtualPath[(virtualPath.LastIndexOf('/') + 1)..];
        var extension = Array.Find(s_yamlExtensions, e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        return extension is null ? name : name[..^extension.Length];
    }

    /// <inheritdoc />
    public Task<CrewConfiguration> LoadFromStringAsync(string yamlContent, CancellationToken ct = default)
        => System.Threading.Tasks.Task.FromResult(LoadFromYaml(yamlContent, null));

    /// <summary>Loads one crew document; <paramref name="source"/> is where it was read from, null for a string.</summary>
    private CrewConfiguration LoadFromYaml(string yamlContent, string? source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlContent);

        var document = new CrewDocument(YamlAnchorPreprocessor.Expand(yamlContent), "", source, typeof(CrewYamlConfig));
        CheckDocuments([document]);
        var crewYaml = Deserialize<CrewYamlConfig>(document);

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
                MaxRpm = crewYaml?.MaxRpm,
                ManagerAgent = crewYaml?.ManagerAgent,
                GraphConfig = crewYaml?.GraphConfig,
                CrewDefaultLlm = crewYaml?.Llm,
                Rag = crewYaml?.Rag,
                Links = crewYaml?.Links,
                Mounts = crewYaml?.Mounts,
            },
            crewYaml?.Agents,
            crewYaml?.Tasks);

        LogLoadedCrewDefinitionFromString(config.Agents.Count, config.Tasks.Count);

        return config;
    }

    /// <summary>A raw crew document: its expanded text, its path in the crew document, where it was read, what it deserializes to.</summary>
    private readonly record struct CrewDocument(ExpandedYaml Yaml, string Path, string? File, Type Model);

    /// <summary>
    /// Judges the raw documents of a crew before any is deserialized: every fault of the crew —
    /// retired, unknown, duplicated or misshapen key — gathered into one refusal.
    /// </summary>
    private static void CheckDocuments(IReadOnlyList<CrewDocument> documents)
    {
        var faults = documents.SelectMany(d => CrewYamlKeyCheck.Find(d.Yaml, d.File, d.Model, d.Path)).ToList();
        if (faults.Count > 0)
            throw new InvalidOperationException(
                "Crew YAML cannot be loaded as written:\n  " + string.Join("\n  ", faults));
    }

    /// <summary>
    /// Deserializes a checked document; a value that does not convert is refused with the file, the
    /// line of the file and the innermost reason, never the bare reader error.
    /// </summary>
    private T Deserialize<T>(CrewDocument document)
    {
        try
        {
            return _yamlSerializer.Deserialize<T>(document.Yaml.Text);
        }
        catch (YamlException ex)
        {
            throw new InvalidOperationException(
                $"{CrewYamlKeyCheck.Situate(document.File, document.Yaml.FileLine(ex.Start.Line))}: {InnermostMessage(ex)}", ex);
        }
    }

    private static string InnermostMessage(Exception ex)
    {
        while (ex.InnerException is { } inner)
            ex = inner;
        return ex.Message;
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
