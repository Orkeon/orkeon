using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Exports crew configuration to YAML format.
/// </summary>
public partial class YamlCrewExporter
{
    private readonly IYamlSerializer _yamlSerializer;
    private readonly IFileSystemService _fs;
    private readonly ILogger<YamlCrewExporter> _logger;

    /// <summary>Initializes a new instance of <see cref="YamlCrewExporter"/>.</summary>
    /// <param name="yamlSerializer">The YAML serializer.</param>
    /// <param name="fs">The virtual file system service.</param>
    /// <param name="logger">The logger.</param>
    public YamlCrewExporter(
        IYamlSerializer yamlSerializer,
        IFileSystemService fs,
        ILogger<YamlCrewExporter> logger)
    {
        ArgumentNullException.ThrowIfNull(yamlSerializer);
        _yamlSerializer = yamlSerializer;
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Exports a crew configuration to a YAML string (single-file format).
    /// </summary>
    public string ExportToString(CrewConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var crewYaml = MapToCrewYamlConfig(config);
        return _yamlSerializer.Serialize(crewYaml);
    }

    /// <summary>
    /// Exports a crew configuration to a single YAML file.
    /// </summary>
    public Task ExportToFileAsync(CrewConfiguration config, string virtualPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        return ExportToFileCoreAsync(config, virtualPath, ct);
    }

    private async Task ExportToFileCoreAsync(CrewConfiguration config, string virtualPath, CancellationToken ct)
    {
        var yaml = ExportToString(config);
        await _fs.WriteAllTextAsync(virtualPath, yaml, ct).ConfigureAwait(false);
        LogExportedCrewConfigurationToFile(virtualPath);
    }

    /// <summary>
    /// Exports a crew configuration to a directory with three files: crew.yaml, agents.yaml, tasks.yaml.
    /// VFS creates parent directories automatically.
    /// </summary>
    public Task ExportToDirectoryAsync(CrewConfiguration config, string directoryPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        return ExportToDirectoryCoreAsync(config, directoryPath, ct);
    }

    private async Task ExportToDirectoryCoreAsync(CrewConfiguration config, string directoryPath, CancellationToken ct)
    {
        var baseDir = directoryPath.TrimEnd('/');

        // Export crew.yaml (crew-level settings only)
        var crewSettings = new CrewSettingsYamlConfig
        {
            Name = config.Name,
            Goal = config.Goal,
#pragma warning disable CA1308 // lowercase is the required YAML wire/storage form, not a comparison normalization
            Process = config.Process.ToString().ToLowerInvariant(),
#pragma warning restore CA1308
            Verbose = config.Verbose ? true : null,
            Memory = config.Memory ? true : null,
            Planning = config.Planning ? true : null,
            MaxRpm = config.MaxRpm,
            ManagerAgent = config.ManagerAgentId?.ToString(),
            Mounts = MapMounts(config),
        };

        var crewYaml = _yamlSerializer.Serialize(crewSettings);
        await _fs.WriteAllTextAsync(baseDir + "/crew.yaml", crewYaml, ct).ConfigureAwait(false);

        // Export agents.yaml
        var agents = MapToAgentsDictionary(config.Agents);
        var agentsYaml = _yamlSerializer.Serialize(agents);
        await _fs.WriteAllTextAsync(baseDir + "/agents.yaml", agentsYaml, ct).ConfigureAwait(false);

        // Export tasks.yaml
        var tasks = MapToTasksDictionary(config.Tasks);
        var tasksYaml = _yamlSerializer.Serialize(tasks);
        await _fs.WriteAllTextAsync(baseDir + "/tasks.yaml", tasksYaml, ct).ConfigureAwait(false);

        LogExportedCrewConfigurationToDirectory(directoryPath);
    }

    private static CrewYamlConfig MapToCrewYamlConfig(CrewConfiguration config)
    {
        return new CrewYamlConfig
        {
            Name = config.Name,
            Goal = config.Goal,
#pragma warning disable CA1308 // lowercase is the required YAML wire/storage form, not a comparison normalization
            Process = config.Process.ToString().ToLowerInvariant(),
#pragma warning restore CA1308
            Verbose = config.Verbose ? true : null,
            Memory = config.Memory ? true : null,
            Planning = config.Planning ? true : null,
            MaxRpm = config.MaxRpm,
            ManagerAgent = config.ManagerAgentId?.ToString(),
            Mounts = MapMounts(config),
            Agents = MapToAgentsDictionary(config.Agents),
            Tasks = MapToTasksDictionary(config.Tasks),
        };
    }

    /// <summary>The <c>mounts:</c> block as the loader reads it back (VFS-90); null when the crew has none.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null",
        Justification = "null means the crew never declared a mounts: block and the exporter writes none; an empty " +
                        "collection means it declared an empty one and `mounts: []` is written. The exact mirror of " +
                        "YamlCrewMapper.MapMounts, which reads the two states apart.")]
    private static System.Collections.ObjectModel.Collection<string>? MapMounts(CrewConfiguration config) =>
        config.Mounts is { } mounts
            ? new System.Collections.ObjectModel.Collection<string>([.. mounts.Select(m => m.ToString())])
            : null;

    private static Dictionary<string, AgentYamlConfig> MapToAgentsDictionary(
        IReadOnlyList<AgentConfiguration> agents)
    {
        var dict = new Dictionary<string, AgentYamlConfig>();
        foreach (var agent in agents)
        {
            dict[agent.Id.ToString()] = MapSingleAgent(agent);
        }
        return dict;
    }

    private static AgentYamlConfig MapSingleAgent(AgentConfiguration agent)
    {
        return new AgentYamlConfig
        {
            Role = agent.Role,
            Goal = agent.Goal,
            Backstory = string.IsNullOrWhiteSpace(agent.Backstory) ? null : agent.Backstory,
            Tools = agent.Tools.Count > 0 ? new Collection<string>(agent.Tools.ToList()) : null,
            AllowDelegation = agent.AllowDelegation ? null : false,
            // Written when it differs from the one default, as the loader reads it back (GAP-38).
            MaxIter = agent.MaxIterations != Orkeon.Domain.Constants.Agent.AgentDefaults.MaxIterations ? agent.MaxIterations : null,
            MaxRpm = agent.MaxRpm,
            Verbose = agent.Verbose ? true : null,
            Llm = MapLlmConfig(agent.LlmConfig),
        };
    }

    /// <summary>
    /// An agent's <c>llm:</c> block: everything its configuration sets, and nothing else — what the
    /// loader reads back (GAP-36). A temperature of 0.7 is written like any other: it was left out
    /// as "the default", and the round trip held only because the loader filled it back in;
    /// <c>topP</c>, <c>thinking</c>, the response format and its schema and <c>cache</c> were not
    /// written at all.
    /// </summary>
    private static LlmYamlConfig? MapLlmConfig(LlmConfig? llmConfig)
    {
        if (llmConfig == null)
            return null;

        var (responseFormat, responseSchema) = MapResponseFormat(llmConfig.ResponseFormat);
        return new LlmYamlConfig
        {
            Profile = llmConfig.Profile,
            // An empty model is the profile's own (GAP-17): left out, like an unpinned cap.
            Model = string.IsNullOrWhiteSpace(llmConfig.Model) ? null : llmConfig.Model,
            Temperature = llmConfig.Temperature,
            MaxTokens = llmConfig.MaxTokens,   // only a pinned cap is written; null was never one
            TopP = llmConfig.TopP,
            Thinking = MapThinking(llmConfig.Thinking),
            ResponseFormat = responseFormat,
            ResponseSchema = responseSchema,
            Cache = MapCache(llmConfig.Cache),
        };
    }

    /// <summary>
    /// A task's <c>llmOverride:</c> block, field by field (GAP-36); null when the task has none.
    /// The block has no <c>cache</c> key: a cache request set on the override in code has no YAML
    /// form.
    /// </summary>
    private static LlmOverrideYamlConfig? MapLlmOverride(LlmConfigOverride? llmOverride)
    {
        if (llmOverride is null)
            return null;

        var (responseFormat, responseSchema) = MapResponseFormat(llmOverride.ResponseFormat);
        return new LlmOverrideYamlConfig
        {
            Profile = llmOverride.Profile,
            ResponseFormat = responseFormat,
            ResponseSchema = responseSchema,
            Temperature = llmOverride.Temperature,
            MaxTokens = llmOverride.MaxTokens,
            TopP = llmOverride.TopP,
            Thinking = MapThinking(llmOverride.Thinking),
        };
    }

    /// <summary>
    /// The <c>responseFormat:</c> value and its <c>responseSchema:</c> companion, as the loader
    /// reads them back. <c>text</c> — the vendor default, which the loader reads as no format — is
    /// written as nothing.
    /// </summary>
    private static (string? Format, ResponseSchemaYamlConfig? Schema) MapResponseFormat(LlmResponseFormat? format)
    {
        if (format is null
            || string.IsNullOrWhiteSpace(format.Type)
            || string.Equals(format.Type, "text", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        return format.Schema is { } schema
            ? (format.Type, new ResponseSchemaYamlConfig { Name = schema.Name, Schema = schema.Schema, Strict = schema.Strict })
            : (format.Type, null);
    }

    /// <summary>The <c>thinking:</c> block; null when it sets nothing, as the loader reads an empty one.</summary>
    private static ThinkingYamlConfig? MapThinking(LlmThinkingConfig? thinking) =>
        thinking is null || (thinking.Enabled is null && string.IsNullOrWhiteSpace(thinking.Effort) && thinking.BudgetTokens is null)
            ? null
            : new ThinkingYamlConfig { Enabled = thinking.Enabled, Effort = thinking.Effort, BudgetTokens = thinking.BudgetTokens };

    /// <summary>The <c>cache:</c> block; null when it marks no breakpoint, as the loader reads one.</summary>
    private static CacheYamlConfig? MapCache(LlmCacheConfig? cache) =>
        cache is { RequestsAnyBreakpoint: true }
            ? new CacheYamlConfig
            {
                System = cache.CacheSystemPrompt ? true : null,
                Tools = cache.CacheTools ? true : null,
                Ttl = cache.Ttl,
            }
            : null;

    private static Dictionary<string, TaskYamlConfig> MapToTasksDictionary(
        IReadOnlyList<TaskConfiguration> tasks)
    {
        var dict = new Dictionary<string, TaskYamlConfig>();
        foreach (var task in tasks)
        {
            dict[task.Id.ToString()] = new TaskYamlConfig
            {
                Description = task.Description,
                ExpectedOutput = task.ExpectedOutput,
                Agent = task.AssignedAgentId?.ToString(),
                Tools = task.Tools.Count > 0 ? new Collection<string>(task.Tools.ToList()) : null,
                Dependencies = task.Dependencies.Count > 0
                    ? new Collection<string>(task.Dependencies.Select(d => d.ToString()).ToList())
                    : null,
                AsyncExecution = task.AsyncExecution ? true : null,
                HumanInput = task.HumanInput ? true : null,
                Context = task.Context.Count > 0 ? task.Context : null,
                LlmOverride = MapLlmOverride(task.LlmOverride),
            };
        }
        return dict;
    }

[LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Exported crew configuration to file: {FilePath}")]
    private partial void LogExportedCrewConfigurationToFile(object filePath);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Exported crew configuration to directory: {DirectoryPath}")]
    private partial void LogExportedCrewConfigurationToDirectory(object directoryPath);

}
