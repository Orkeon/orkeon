using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Constants.Llm;

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
            ManagerAgent = config.ManagerAgentId?.ToString(),
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
            ManagerAgent = config.ManagerAgentId?.ToString(),
            Agents = MapToAgentsDictionary(config.Agents),
            Tasks = MapToTasksDictionary(config.Tasks),
        };
    }

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
            MaxIter = agent.MaxIterations != 20 ? agent.MaxIterations : null,
            MaxRpm = agent.MaxRPM != 10 ? agent.MaxRPM : null,
            Verbose = agent.Verbose ? true : null,
            Llm = MapLlmConfig(agent.LlmConfig),
        };
    }

    private static LlmYamlConfig? MapLlmConfig(LlmConfig? llmConfig)
    {
        if (llmConfig == null)
            return null;

        return new LlmYamlConfig
        {
            Model = llmConfig.Model,
            Temperature = llmConfig.Temperature != LlmDefaults.DefaultTemperature ? llmConfig.Temperature : null,
            MaxTokens = llmConfig.MaxTokens != 4096 ? llmConfig.MaxTokens : null,
        };
    }

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
                Tools = task.RequiredTools.Count > 0 ? new Collection<string>(task.RequiredTools.ToList()) : null,
                Dependencies = task.Dependencies.Count > 0
                    ? new Collection<string>(task.Dependencies.Select(d => d.ToString()).ToList())
                    : null,
                AsyncExecution = task.AsyncExecution ? true : null,
                HumanInput = task.HumanInput ? true : null,
                Context = task.Context.Count > 0 ? task.Context : null,
            };
        }
        return dict;
    }

[LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Exported crew configuration to file: {FilePath}")]
    private partial void LogExportedCrewConfigurationToFile(object filePath);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Exported crew configuration to directory: {DirectoryPath}")]
    private partial void LogExportedCrewConfigurationToDirectory(object directoryPath);

}
