using System.Globalization;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Flows;
using Orkeon.Domain.Flows.ValueObjects;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Infrastructure.Flows;

/// <summary>
/// Loads flow definitions from YAML format.
/// </summary>
public class YamlFlowDefinitionLoader
{
    private readonly IYamlSerializer _yaml;
    private readonly IFileSystemService _fs;

    /// <summary>Initializes a new instance of <see cref="YamlFlowDefinitionLoader"/>.</summary>
    /// <param name="yaml">The YAML serializer.</param>
    /// <param name="fs">The virtual file system service.</param>
    public YamlFlowDefinitionLoader(IYamlSerializer yaml, IFileSystemService fs)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        _yaml = yaml;
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
    }

    /// <summary>
    /// Loads a flow definition from a YAML file asynchronously.
    /// </summary>
    public async Task<IFlowDefinition> LoadFromFileAsync(string virtualPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        if (!await _fs.ExistsAsync(virtualPath, ct).ConfigureAwait(false))
            throw new FileNotFoundException($"Flow definition file not found: {virtualPath}", virtualPath);

        var yaml = await _fs.TryReadAllTextAsync(virtualPath, ct).ConfigureAwait(false);
        return LoadFromString(yaml!);
    }

    /// <summary>
    /// Loads a flow definition from a YAML string.
    /// </summary>
    public IFlowDefinition LoadFromString(string yamlContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlContent);

        var yamlData = _yaml.Deserialize<YamlFlowModel>(yamlContent);

        var definition = new InMemoryFlowDefinition
        {
            Id = FlowId.Create(),
            Name = yamlData.Name ?? string.Empty,
            Description = yamlData.Description ?? string.Empty,
            Type = ParseFlowType(yamlData.Type),
            Configuration = BuildConfiguration(yamlData),
            Steps = BuildSteps(yamlData.Steps ?? [])
        };

        return definition;
    }

    private static FlowType ParseFlowType(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return FlowType.Sequential;

#pragma warning disable CA1308 // lowercase is the normalized switch subject the YAML keys are matched against
        return type.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "sequential" => FlowType.Sequential,
            "parallel" => FlowType.Parallel,
            "conditional" => FlowType.Conditional,
            "loop" => FlowType.Loop,
            "crew" => FlowType.Crew,
            "custom" => FlowType.Custom,
            _ => FlowType.Sequential
        };
    }

    private static FlowConfiguration BuildConfiguration(YamlFlowModel model)
    {
        TimeSpan? timeout = null;
        var maxRetries = AgentDefaults.MaxRetryLimit;
        var settings = FlowConfigurationSettings.Empty;

        if (model.Settings != null)
        {
            if (model.Settings.TryGetValue("timeout_seconds", out var timeoutVal) && timeoutVal != null)
            {
                timeout = TimeSpan.FromSeconds(Inv.ToDouble(timeoutVal));
            }

            if (model.Settings.TryGetValue("max_retries", out var retries) && retries != null)
            {
                maxRetries = Convert.ToInt32(retries, CultureInfo.InvariantCulture);
            }

            var settingsBuilder = FlowConfigurationSettings.CreateBuilder();
            foreach (var kvp in model.Settings)
            {
                if (kvp.Value != null)
                    settingsBuilder.Add(kvp.Key, kvp.Value);
            }
            settings = settingsBuilder.Build();
        }

        return new FlowConfiguration
        {
            Name = model.Name ?? string.Empty,
            Type = ParseFlowType(model.Type),
            Timeout = timeout,
            MaxRetries = maxRetries,
            Settings = settings
        };
    }

    private static List<FlowStep> BuildSteps(List<YamlStepModel> yamlSteps)
    {
        var steps = new List<FlowStep>();

        // Build a name-to-id mapping so we can resolve dependency references
        var nameToId = new Dictionary<string, FlowStepId>();
        var stepModels = new List<(YamlStepModel Yaml, FlowStepId StepId)>();

        foreach (var yamlStep in yamlSteps)
        {
            var stepId = FlowStepId.Create();
            var name = yamlStep.Name ?? string.Empty;
            if (!string.IsNullOrEmpty(name))
                nameToId[name] = stepId;
            stepModels.Add((yamlStep, stepId));
        }

        foreach (var (yamlStep, stepId) in stepModels)
        {
            var dependencies = new List<FlowStepId>();
            if (yamlStep.Dependencies is { Count: > 0 })
            {
                foreach (var dep in yamlStep.Dependencies)
                {
                    if (nameToId.TryGetValue(dep, out var depId))
                        dependencies.Add(depId);
                }
            }

            var step = new FlowStep
            {
                Id = stepId,
                Name = yamlStep.Name ?? string.Empty,
                Type = yamlStep.Type ?? string.Empty,
                Dependencies = dependencies.AsReadOnly(),
                Timeout = yamlStep.Timeout_Seconds.HasValue
                    ? TimeSpan.FromSeconds(yamlStep.Timeout_Seconds.Value)
                    : null,
                MaxRetries = yamlStep.Max_Retries ?? 3,
                Parameters = yamlStep.Parameters is { Count: > 0 }
                    ? FlowStepParameters.FromDictionary(yamlStep.Parameters)
                    : FlowStepParameters.Empty
            };

            steps.Add(step);
        }

        return steps;
    }

    // YAML model classes for deserialization
    /// <summary>YAML deserialization model for a flow definition.</summary>
    internal class YamlFlowModel
    {
        /// <summary>Gets or sets the flow name.</summary>
        public string? Name { get; set; }
        /// <summary>Gets or sets the flow description.</summary>
        public string? Description { get; set; }
        /// <summary>Gets or sets the flow type (sequential, parallel, conditional, loop).</summary>
        public string? Type { get; set; }
        /// <summary>Gets or sets additional flow configuration settings.</summary>
        public Dictionary<string, object?>? Settings { get; set; }
        /// <summary>Gets or sets the list of step definitions.</summary>
        public List<YamlStepModel>? Steps { get; set; }
    }

    /// <summary>YAML deserialization model for a single flow step.</summary>
    internal class YamlStepModel
    {
        /// <summary>Gets or sets the step name.</summary>
        public string? Name { get; set; }
        /// <summary>Gets or sets the step type (crew, llm, tool, conditional, human_input, delay).</summary>
        public string? Type { get; set; }
        /// <summary>Gets or sets the step parameters.</summary>
        public Dictionary<string, object>? Parameters { get; set; }
        /// <summary>Gets or sets the step dependency IDs.</summary>
        public List<string>? Dependencies { get; set; }
        /// <summary>Gets or sets the step timeout in seconds.</summary>
        public int? Timeout_Seconds { get; set; }
        /// <summary>Gets or sets the maximum retry count for this step.</summary>
        public int? Max_Retries { get; set; }
    }
}
