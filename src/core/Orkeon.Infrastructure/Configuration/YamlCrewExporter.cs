using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.EventHub;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Exports a crew configuration to YAML, in the camelCase form the loader reads (GAP-39): loaded
/// again, the file gives the same configuration — every key the loader reads —, and the export of
/// that configuration is the same text, byte for byte. A key nothing sets is not written.
/// <para>
/// Each agent and each task is written under its key (<see cref="AgentConfiguration.Key"/>,
/// <see cref="TaskConfiguration.Key"/>), the name its author gave it, and so is every reference to it
/// (<c>agent:</c>, <c>dependencies:</c>, <c>managerAgent:</c>). A configuration built in code or by a
/// <c>.ork.ts</c> script has no keys: its entries are written under their identifiers. Two entries
/// under one name, or a reference that names no entry, are refused — the loader would refuse the file.
/// </para>
/// <para>
/// What the configuration carries is written, not the text its author typed: a guardrails
/// <c>preset:</c> comes back as the <c>header</c>, <c>rules</c> and <c>toolRules</c> it gave the agent
/// or the task, the crew's <c>llm:</c> as the block each agent got from it, under the agent, and
/// <c>anchors:</c> as the text they stood for. A <c>rag:</c> source is written as it was: a relative
/// one follows the folder the file is read from next (GAP-27). The folder the crew was read from is
/// not a key, and what has no key in a crew file is not written.
/// </para>
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
    /// <exception cref="InvalidOperationException">
    /// Two agents or two tasks go by one name, or a reference names no entry of the crew.
    /// </exception>
    public string ExportToString(CrewConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var document = MapCrewDocument(config);
        document.Crew.Agents = document.Agents;
        document.Crew.Tasks = document.Tasks;
        return _yamlSerializer.Serialize(document.Crew);
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
        var document = MapCrewDocument(config);

        // The flat triplet LoadFromDirectoryAsync reads: crew.yaml holds the crew's own keys — the
        // single file's, without its agents: and tasks: —, and each table has its own file.
        await _fs.WriteAllTextAsync(baseDir + "/crew.yaml", _yamlSerializer.Serialize(document.Crew), ct).ConfigureAwait(false);
        await _fs.WriteAllTextAsync(baseDir + "/agents.yaml", _yamlSerializer.Serialize(document.Agents), ct).ConfigureAwait(false);
        await _fs.WriteAllTextAsync(baseDir + "/tasks.yaml", _yamlSerializer.Serialize(document.Tasks), ct).ConfigureAwait(false);

        LogExportedCrewConfigurationToDirectory(directoryPath);
    }

    /// <summary>A crew file in three parts: the crew's own keys, its agents and its tasks, each under its name.</summary>
    private sealed record CrewDocument(
        CrewYamlConfig Crew,
        Dictionary<string, AgentYamlConfig> Agents,
        Dictionary<string, TaskYamlConfig> Tasks);

    private static CrewDocument MapCrewDocument(CrewConfiguration config)
    {
        var names = EntryNames.For(config);
        return new CrewDocument(
            MapCrewSettings(config, names),
            config.Agents.ToDictionary(agent => names.Of(agent.Id), MapSingleAgent, StringComparer.Ordinal),
            config.Tasks.ToDictionary(task => names.Of(task.Id), task => MapSingleTask(task, names), StringComparer.Ordinal));
    }

    /// <summary>
    /// The crew's own keys: one projection for the single file and the directory's <c>crew.yaml</c>.
    /// The crew's <c>llm:</c> is not among them — the loader merged it into each agent's block, which
    /// is written under the agent.
    /// </summary>
    private static CrewYamlConfig MapCrewSettings(CrewConfiguration config, EntryNames names)
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
            MemoryProvider = config.MemoryProvider,
            Planning = config.Planning ? true : null,
            MaxRpm = config.MaxRpm,
            ManagerAgent = config.ManagerAgentId is { } manager ? names.Of(manager) : null,
            GraphConfig = MapGraphConfig(config.GraphConfig),
            Rag = MapRag(config.Rag),
            Links = MapLinks(config.Links),
            Mounts = MapMounts(config),
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

    /// <summary>
    /// The <c>links:</c> block (HUB-03): null when the crew never wrote one, an empty list when it wrote
    /// one that grants nothing — a closed door, which the loader reads apart from an absent block. The
    /// direction is always written, in lowercase; the topics when the link restricts them.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null",
        Justification = "null means the crew never declared a links: block, which the ACL arbitrates by policy; an empty " +
                        "collection means it declared one that grants nothing, and `links: []` is written. The mirror of " +
                        "YamlCrewMapper.MapLinks.")]
    private static Collection<LinkYamlConfig>? MapLinks(IReadOnlyList<CrewLink>? links)
    {
        if (links is null)
            return null;

        return new Collection<LinkYamlConfig>([.. links.Select(link => new LinkYamlConfig
        {
            To = link.To,
            Direction = WrittenDirection(link.Direction),
            AllowedTopics = link.AllowedTopics.IsDefaultOrEmpty ? null : new Collection<string>([.. link.AllowedTopics]),
        })]);
    }

    private static string WrittenDirection(CrewLinkDirection direction) => direction switch
    {
        CrewLinkDirection.Outbound => "outbound",
        CrewLinkDirection.Inbound => "inbound",
        CrewLinkDirection.Bidirectional => "bidirectional",
        _ => throw new InvalidOperationException($"A links: direction the loader cannot read: {direction}."),
    };

    /// <summary>
    /// The <c>graphConfig:</c> block; null when the crew has none. Its two settings with a default
    /// (<c>maxRetryCycles</c>, <c>circuitBreakerPreset</c>) are written as the configuration carries them.
    /// </summary>
    private static GraphYamlConfig? MapGraphConfig(GraphConfig? graph) =>
        graph is null
            ? null
            : new GraphYamlConfig
            {
                MaxRetryCycles = graph.MaxRetryCycles,
                CircuitBreakerPreset = graph.CircuitBreakerPreset,
                MaxTransitions = graph.MaxTransitions,
                MaxStateVisits = graph.MaxStateVisits,
                MaxTotalDurationSeconds = graph.MaxTotalDurationSeconds,
            };

    /// <summary>
    /// The <c>rag:</c> block; null when the crew has none, <c>{}</c> when it declares an empty one. A
    /// collection's sources are written as they were — a relative one is read against the folder the
    /// file is loaded from —, and its chunking when it sets one.
    /// </summary>
    private static RagYamlConfig? MapRag(RagCrewConfig? rag)
    {
        if (rag is null)
            return null;

        return new RagYamlConfig
        {
            Collections = rag.Collections.Count == 0
                ? null
                : rag.Collections.ToDictionary(collection => collection.Key, collection => MapRagCollection(collection.Value), StringComparer.Ordinal),
            Defaults = rag.DefaultProfile is { } profile ? new RagDefaultsYamlConfig { Profile = profile } : null,
        };
    }

    private static RagCollectionYamlConfig MapRagCollection(RagCollectionConfig collection) => new()
    {
        Sources = collection.Sources.Count == 0 ? null : new Collection<string>([.. collection.Sources]),
        Chunking = collection.Chunking is { } chunking
            ? new RagChunkingYamlConfig { Strategy = chunking.Strategy, MaxTokens = chunking.MaxTokens, Overlap = chunking.Overlap }
            : null,
    };

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
            Guardrails = MapGuardrails(agent.Guardrails),
            Knowledge = MapKnowledge(agent.KnowledgeAttachments),
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
    private static CacheYamlConfig? MapCache(LlmCacheConfig? cache)
    {
        if (cache is not { RequestsAnyBreakpoint: true })
            return null;

        return new CacheYamlConfig
        {
            System = cache.CacheSystemPrompt ? true : null,
            Tools = cache.CacheTools ? true : null,
            Ttl = cache.Ttl,
        };
    }

    /// <summary>
    /// A <c>guardrails:</c> block, agent's or task's, as the configuration carries it: a preset is
    /// written as the header, the rules and the tool rules it brought, never by its name (decision 1).
    /// Null when the block sets nothing, as the loader reads an empty one.
    /// </summary>
    private static GuardrailsYamlConfig? MapGuardrails(GuardrailsConfig? guardrails)
    {
        if (guardrails is null || (guardrails.Header is null && guardrails.IsEmpty))
            return null;

        return new GuardrailsYamlConfig
        {
            Header = guardrails.Header,
            Rules = guardrails.Rules.Count == 0 ? null : new Collection<string>([.. guardrails.Rules]),
            ToolRules = guardrails.ToolRules.Count == 0
                ? null
                : guardrails.ToolRules.ToDictionary(rule => rule.Key, rule => rule.Value.ToList(), StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// The <c>knowledge:</c> list: an attachment that only names its collection, the rest at its
    /// default, in the short form (<c>- produits</c>); any other in the long form, every key it sets
    /// in camelCase. Null when the agent attaches nothing.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null",
        Justification = "null writes no knowledge: key at all; an empty list would write `knowledge: []` for an agent that " +
                        "attaches nothing, a key nobody set.")]
    private static Collection<object>? MapKnowledge(IReadOnlyList<KnowledgeAttachment> attachments)
    {
        if (attachments.Count == 0)
            return null;

        var items = new Collection<object>();
        foreach (var attachment in attachments)
        {
            if (attachment is { TopK: KnowledgeAttachment.DefaultTopK, MinScore: null, Profile: null, MaxContextTokens: null })
            {
                items.Add(attachment.Collection);
                continue;
            }

            var longForm = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["collection"] = attachment.Collection,
                ["topK"] = attachment.TopK,
            };
            if (attachment.MinScore is { } minScore)
                longForm["minScore"] = minScore;
            if (attachment.Profile is { } profile)
                longForm["profile"] = profile;
            if (attachment.MaxContextTokens is { } maxContextTokens)
                longForm["maxContextTokens"] = maxContextTokens;
            items.Add(longForm);
        }

        return items;
    }

    private static TaskYamlConfig MapSingleTask(TaskConfiguration task, EntryNames names)
    {
        return new TaskYamlConfig
        {
            Description = task.Description,
            ExpectedOutput = task.ExpectedOutput,
            Agent = task.AssignedAgentId is { } agent ? names.Of(agent) : null,
            Tools = task.Tools.Count > 0 ? new Collection<string>(task.Tools.ToList()) : null,
            Dependencies = task.Dependencies.Count > 0
                ? new Collection<string>([.. task.Dependencies.Select(names.Of)])
                : null,
            AsyncExecution = task.AsyncExecution ? true : null,
            HumanInput = task.HumanInput ? true : null,
            Context = task.Context.Count > 0 ? task.Context : null,
            Deliverable = MapDeliverable(task.Deliverable),
            LlmOverride = MapLlmOverride(task.LlmOverride),
            Guardrails = MapGuardrails(task.Guardrails),
        };
    }

    /// <summary>
    /// A task's <c>deliverable:</c> block; null when the task has none. The source is written in the
    /// form the loader reads (<c>tool_call</c>, <c>final_message</c>, <c>structured_output</c>,
    /// <c>none</c>), the format and the sanitizing as the configuration carries them.
    /// </summary>
    private static DeliverableYamlConfig? MapDeliverable(TaskDeliverable? deliverable) =>
        deliverable is null
            ? null
            : new DeliverableYamlConfig
            {
                Path = deliverable.Path,
                Source = WrittenSource(deliverable.Source),
                Format = deliverable.Format,
                Sanitize = deliverable.Sanitize,
                SchemaPath = deliverable.SchemaPath,
                SchemaInline = deliverable.SchemaInline,
            };

    private static string WrittenSource(DeliverableSource source) => source switch
    {
        DeliverableSource.None => "none",
        DeliverableSource.ToolCall => "tool_call",
        DeliverableSource.FinalMessage => "final_message",
        DeliverableSource.StructuredOutput => "structured_output",
        _ => throw new InvalidOperationException($"A deliverable source the loader cannot read: {source}."),
    };

    /// <summary>
    /// The name of each agent and each task in the file — its key, else its identifier — for the entry
    /// and for every reference to it. Built once per export, refusing what the loader would refuse: two
    /// entries under one name, an identifier two entries share, a reference that names no entry. Every
    /// fault is named at once.
    /// </summary>
    private sealed class EntryNames
    {
        private readonly Dictionary<AgentId, string> _agents;
        private readonly Dictionary<TaskId, string> _tasks;

        private EntryNames(Dictionary<AgentId, string> agents, Dictionary<TaskId, string> tasks)
        {
            _agents = agents;
            _tasks = tasks;
        }

        public string Of(AgentId agent) => _agents[agent];

        public string Of(TaskId task) => _tasks[task];

        public static EntryNames For(CrewConfiguration config)
        {
            var faults = new List<string>();
            var agents = Name(config.Agents, agent => agent.Id, CrewEntryNames.Of, "agents", faults);
            var tasks = Name(config.Tasks, task => task.Id, CrewEntryNames.Of, "tasks", faults);

            if (config.ManagerAgentId is { } manager && !agents.ContainsKey(manager))
                faults.Add($"managerAgent names {manager}, which is no agent of the crew");

            foreach (var task in config.Tasks)
            {
                if (task.AssignedAgentId is { } agent && !agents.ContainsKey(agent))
                    faults.Add($"task '{CrewEntryNames.Of(task)}' names agent {agent}, which is no agent of the crew");

                foreach (var dependency in task.Dependencies.Where(dependency => !tasks.ContainsKey(dependency)))
                    faults.Add($"task '{CrewEntryNames.Of(task)}' depends on {dependency}, which is no task of the crew");
            }

            if (faults.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Crew '{config.Name}' cannot be exported: {string.Join("; ", faults)}. The loader would refuse the file.");
            }

            return new EntryNames(agents, tasks);
        }

        private static Dictionary<TId, string> Name<TEntry, TId>(
            IReadOnlyList<TEntry> entries, Func<TEntry, TId> idOf, Func<TEntry, string> nameOf, string kind, List<string> faults)
            where TId : notnull
        {
            foreach (var shared in entries.GroupBy(nameOf, StringComparer.Ordinal).Where(group => group.Count() > 1))
                faults.Add($"{shared.Count()} {kind} go by the name '{shared.Key}'");

            foreach (var shared in entries.GroupBy(idOf).Where(group => group.Count() > 1))
                faults.Add($"{shared.Count()} {kind} share the identifier {shared.Key}");

            var names = new Dictionary<TId, string>();
            foreach (var entry in entries)
                names.TryAdd(idOf(entry), nameOf(entry));
            return names;
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Exported crew configuration to file: {FilePath}")]
    private partial void LogExportedCrewConfigurationToFile(object filePath);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Exported crew configuration to directory: {DirectoryPath}")]
    private partial void LogExportedCrewConfigurationToDirectory(object directoryPath);

}
