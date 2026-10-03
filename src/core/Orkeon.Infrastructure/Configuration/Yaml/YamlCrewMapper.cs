using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.EventHub;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Maps YAML crew DTOs onto domain <see cref="CrewConfiguration"/> children (agents, tasks)
/// and resolves the three-level LLM cascade (crew default → agent → task override).
/// Extracted from <see cref="YamlCrewDefinitionLoader"/> (R4.2 god-file decomposition).
/// </summary>
public sealed partial class YamlCrewMapper
{
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of <see cref="YamlCrewMapper"/>.</summary>
    /// <param name="logger">Logger used to surface malformed-but-recoverable YAML values.</param>
    public YamlCrewMapper(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Builds a full crew configuration from the single-file or multi-file crew/agents/tasks DTOs.</summary>
    public CrewConfiguration BuildConfiguration(
        CrewMappingSettings settings,
        Dictionary<string, AgentYamlConfig>? agents,
        Dictionary<string, TaskYamlConfig>? tasks)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var process = ParseProcessType(settings.Process);
        RefuseAsyncExecutionTheModeIgnores(process, settings.Process, tasks);
        RefuseManagerTheModeHasNoneOf(process, settings.Process, settings.ManagerAgent);

        var mappedAgents = agents != null
            ? MapAgents(agents, settings.CrewDefaultLlm, out var agentNameMap)
            : MapAgentsEmpty(out agentNameMap);
        var managerAgentId = ResolveManagerAgentId(settings.ManagerAgent, agentNameMap);
        var mappedTasks = tasks != null ? MapTasks(tasks, agentNameMap) : [];

        return new CrewConfiguration
        {
            Name = settings.Name ?? string.Empty,
            Goal = settings.Goal ?? string.Empty,
            Process = process,
            Verbose = settings.Verbose ?? false,
            Memory = settings.Memory ?? false,
            MemoryProvider = settings.MemoryProvider,
            Planning = settings.Planning ?? false,
            Agents = mappedAgents,
            Tasks = mappedTasks,
            ManagerAgentId = managerAgentId,
            GraphConfig = MapGraphConfig(settings.GraphConfig),
            Rag = MapRag(settings.Rag),
            Links = MapLinks(settings.Name ?? string.Empty, settings.Links),
            Mounts = MapMounts(settings.Name ?? string.Empty, settings.Mounts),
        };
    }

    /// <summary>
    /// Maps the <c>mounts:</c> block (VFS-90). Strict, unlike <see cref="MapLinks"/>: an item
    /// that is neither <c>/root</c> nor <c>&lt;ulid&gt;|/root</c> is a selection that would
    /// otherwise be dropped in silence and resurface at startup as a refusal about a root the
    /// author never meant. Null when the crew wrote no block.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null",
        Justification = "null means the crew never wrote a mounts: block; an empty list means it wrote an empty one. " +
                        "Both leave the settings' entries in force, but the summary and the exporter tell them apart.")]
    private static List<MountReference>? MapMounts(string crewName, Collection<string>? yaml)
    {
        if (yaml is null)
            return null;

        var references = new List<MountReference>(yaml.Count);
        foreach (var item in yaml)
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;

            if (!MountReference.TryParse(item, out var reference))
            {
                throw new InvalidOperationException(
                    $"Crew '{crewName}' mounts: entry '{item.Trim()}' is neither '/root' nor '<ulid>|/root' "
                    + "(a virtual root starts with '/', a mount id is the 26-character ULID a settings entry carries before its '|').");
            }

            references.Add(reference);
        }

        return references;
    }

    private static List<AgentConfiguration> MapAgentsEmpty(out Dictionary<string, AgentId> nameToId)
    {
        nameToId = [];
        return [];
    }

    private List<AgentConfiguration> MapAgents(
        Dictionary<string, AgentYamlConfig> agents,
        LlmYamlConfig? crewDefaultLlm,
        out Dictionary<string, AgentId> nameToId)
    {
        nameToId = [];
        var result = new List<AgentConfiguration>();

        foreach (var kvp in agents)
        {
            var agentId = AgentId.Create();
            nameToId[kvp.Key] = agentId;

            var effectiveLlm = MergeLlmYamlConfig(crewDefaultLlm, kvp.Value.Llm);
            result.Add(new AgentConfiguration
            {
                Id = agentId,
                Role = kvp.Value.Role ?? kvp.Key,
                Goal = kvp.Value.Goal ?? string.Empty,
                Backstory = kvp.Value.Backstory ?? string.Empty,
                Tools = kvp.Value.Tools ?? [],
                AllowDelegation = kvp.Value.AllowDelegation ?? true,
                MaxIterations = kvp.Value.MaxIter ?? 20,
                MaxRPM = kvp.Value.MaxRpm ?? 10,
                Verbose = kvp.Value.Verbose ?? false,
                // No model named: the profile's own (GAP-17) — never the framework's default
                // model, which a block setting only a temperature used to pin on any vendor.
                // No temperature or top_p named: none set — the profile's, else the model's own
                // (GAP-36); the loader used to fill in the engine's 0.7 and 1.0.
                LlmConfig = effectiveLlm != null
                    ? (string.IsNullOrWhiteSpace(effectiveLlm.Model) ? LlmConfig.OnProfile() : LlmConfig.Create(effectiveLlm.Model)) with
                    {
                        Profile = string.IsNullOrWhiteSpace(effectiveLlm.Profile) ? null : effectiveLlm.Profile.Trim(),
                        Temperature = effectiveLlm.Temperature,
                        MaxTokens = effectiveLlm.MaxTokens,   // null = the model's documented maximum (LLM-10)
                        TopP = effectiveLlm.TopP,
                        Thinking = MapThinking(effectiveLlm.Thinking),
                        ResponseFormat = MapResponseFormat(effectiveLlm.ResponseFormat, effectiveLlm.ResponseSchema),
                        Cache = MapCache(effectiveLlm.Cache),
                    }
                    : null,
                Guardrails = MapGuardrails(kvp.Value.Guardrails),
                KnowledgeAttachments = MapKnowledge(kvp.Key, kvp.Value.Knowledge),
            });
        }

        return result;
    }

    /// <summary>
    /// Maps the tasks, resolving each <c>agent:</c> and dependency by its key. A reference that names
    /// nothing — a typo — fails the load, every one named at once with what the crew has (GAP-33,
    /// decision 6): erased, it let the task run on another agent, or without waiting for what it cited.
    /// </summary>
    private List<TaskConfiguration> MapTasks(
        Dictionary<string, TaskYamlConfig> tasks, Dictionary<string, AgentId> agentNameMap)
    {
        // First pass: create TaskIds for all tasks so we can resolve dependencies
        var taskNameToId = new Dictionary<string, TaskId>();
        foreach (var kvp in tasks)
        {
            taskNameToId[kvp.Key] = TaskId.Create();
        }

        // Second pass: build TaskConfigurations with resolved references
        var result = new List<TaskConfiguration>();
        var unknownAgents = new List<string>();
        var unknownDependencies = new List<string>();
        foreach (var kvp in tasks)
        {
            var dependencies = new List<TaskId>();
            foreach (var dep in kvp.Value.Dependencies ?? [])
            {
                if (taskNameToId.TryGetValue(dep, out var depId))
                    dependencies.Add(depId);
                else
                    unknownDependencies.Add($"task '{kvp.Key}' depends on '{dep}', which is no task of the crew");
            }

            AgentId? assignedAgentId = null;
            if (!string.IsNullOrWhiteSpace(kvp.Value.Agent))
            {
                if (agentNameMap.TryGetValue(kvp.Value.Agent, out var agentId))
                    assignedAgentId = agentId;
                else
                    unknownAgents.Add($"task '{kvp.Key}' names agent: {kvp.Value.Agent}, which is no agent of the crew");
            }

            result.Add(new TaskConfiguration
            {
                Id = taskNameToId[kvp.Key],
                Description = kvp.Value.Description ?? string.Empty,
                ExpectedOutput = kvp.Value.ExpectedOutput ?? string.Empty,
                AssignedAgentId = assignedAgentId,
                Dependencies = dependencies,
                Tools = kvp.Value.Tools ?? (IReadOnlyList<string>)Array.Empty<string>(),
                AsyncExecution = kvp.Value.AsyncExecution ?? false,
                HumanInput = kvp.Value.HumanInput ?? false,
                Context = kvp.Value.Context ?? [],
                Deliverable = MapDeliverable(kvp.Value.Deliverable),
                LlmOverride = MapTaskLlmOverride(kvp.Value.LlmOverride),
                Guardrails = MapGuardrails(kvp.Value.Guardrails),
            });
        }

        if (unknownAgents.Count > 0 || unknownDependencies.Count > 0)
        {
            var faults = string.Join("; ", unknownAgents.Concat(unknownDependencies));
            var known = (unknownAgents.Count > 0 ? $" Its agents: {Known(agentNameMap.Keys)}." : string.Empty)
                + (unknownDependencies.Count > 0 ? $" Its tasks: {Known(taskNameToId.Keys)}." : string.Empty);
            throw new InvalidOperationException(
                $"A task reference names nothing: {faults}.{known} Name an agent or a task by its key.");
        }

        return result;
    }

    /// <summary>The keys a crew declares, in its order, for a message: <c>none</c> when it has none.</summary>
    private static string Known(IEnumerable<string> keys)
    {
        var list = string.Join(", ", keys);
        return list.Length == 0 ? "none" : list;
    }

    /// <summary>
    /// Maps the YAML deliverable block to a <see cref="Orkeon.Domain.Task.ValueObjects.TaskDeliverable"/>.
    /// Returns <c>null</c> when the block is absent so the task falls back to legacy tool_call behavior.
    /// </summary>
    private static Orkeon.Domain.Task.ValueObjects.TaskDeliverable? MapDeliverable(DeliverableYamlConfig? yaml)
    {
        if (yaml == null) return null;
        if (string.IsNullOrWhiteSpace(yaml.Path)) return null;

        var source = ParseDeliverableSource(yaml.Source);
        var deliverable = new Orkeon.Domain.Task.ValueObjects.TaskDeliverable
        {
            Path = yaml.Path,
            Source = source,
            Format = string.IsNullOrWhiteSpace(yaml.Format) ? "markdown" : yaml.Format,
            Sanitize = yaml.Sanitize ?? true,
            SchemaPath = yaml.SchemaPath,
            SchemaInline = yaml.SchemaInline,
        };
        deliverable.Validate();
        return deliverable;
    }

    private static Orkeon.Domain.Task.ValueObjects.DeliverableSource ParseDeliverableSource(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Orkeon.Domain.Task.ValueObjects.DeliverableSource.ToolCall;

#pragma warning disable CA1308 // lowercase is the normalized switch subject the YAML keys are matched against
        return raw.Trim().ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "none" => Orkeon.Domain.Task.ValueObjects.DeliverableSource.None,
            "tool_call" or "toolcall" => Orkeon.Domain.Task.ValueObjects.DeliverableSource.ToolCall,
            "final_message" or "finalmessage" => Orkeon.Domain.Task.ValueObjects.DeliverableSource.FinalMessage,
            "structured_output" or "structuredoutput" => Orkeon.Domain.Task.ValueObjects.DeliverableSource.StructuredOutput,
            _ => throw new InvalidOperationException(
                $"Unknown deliverable source '{raw}'. Expected: final_message, structured_output, tool_call, none."),
        };
    }

    /// <summary>
    /// Field-by-field merge of an agent's <c>llm:</c> block onto the crew-level default.
    /// Each agent field that is set wins over the crew default; unset agent fields fall
    /// back to the crew default. Returns null when neither side declares any LLM block.
    /// Experiment 07 friction #7: prior behavior was whole-block replacement, which
    /// silently dropped crew-default MaxTokens / Temperature when an agent only wanted
    /// to override the model name.
    /// </summary>
    private static LlmYamlConfig? MergeLlmYamlConfig(LlmYamlConfig? crewLevel, LlmYamlConfig? agentLevel)
    {
        if (crewLevel is null && agentLevel is null) return null;
        if (agentLevel is null) return crewLevel;
        if (crewLevel is null) return agentLevel;

        return new LlmYamlConfig
        {
            Profile = string.IsNullOrWhiteSpace(agentLevel.Profile) ? crewLevel.Profile : agentLevel.Profile,
            Model = agentLevel.Model ?? crewLevel.Model,
            Temperature = agentLevel.Temperature ?? crewLevel.Temperature,
            MaxTokens = agentLevel.MaxTokens ?? crewLevel.MaxTokens,
            TopP = agentLevel.TopP ?? crewLevel.TopP,
            Thinking = MergeThinkingYamlConfig(crewLevel.Thinking, agentLevel.Thinking),
            ResponseFormat = string.IsNullOrWhiteSpace(agentLevel.ResponseFormat) ? crewLevel.ResponseFormat : agentLevel.ResponseFormat,
            ResponseSchema = agentLevel.ResponseSchema ?? crewLevel.ResponseSchema,
            Cache = agentLevel.Cache ?? crewLevel.Cache,
        };
    }

    /// <summary>Field-by-field merge of an agent's <c>thinking:</c> sub-block onto the crew default.</summary>
    private static ThinkingYamlConfig? MergeThinkingYamlConfig(ThinkingYamlConfig? crewLevel, ThinkingYamlConfig? agentLevel)
    {
        if (crewLevel is null && agentLevel is null) return null;
        if (agentLevel is null) return crewLevel;
        if (crewLevel is null) return agentLevel;

        return new ThinkingYamlConfig
        {
            Enabled = agentLevel.Enabled ?? crewLevel.Enabled,
            Effort = string.IsNullOrWhiteSpace(agentLevel.Effort) ? crewLevel.Effort : agentLevel.Effort,
            BudgetTokens = agentLevel.BudgetTokens ?? crewLevel.BudgetTokens,
        };
    }

    /// <summary>
    /// Maps a YAML thinking block to its domain value object. Returns null when no
    /// fields were provided so the provider default stays in effect.
    /// </summary>
    private static LlmThinkingConfig? MapThinking(ThinkingYamlConfig? yaml)
    {
        if (yaml is null) return null;
        if (yaml.Enabled is null && string.IsNullOrWhiteSpace(yaml.Effort) && yaml.BudgetTokens is null) return null;
        return new LlmThinkingConfig
        {
            Enabled = yaml.Enabled,
            Effort = yaml.Effort,
            BudgetTokens = yaml.BudgetTokens,
        };
    }

    /// <summary>
    /// Maps the YAML <c>response_format:</c> string, and its optional
    /// <c>response_schema:</c> companion, to the domain value object.
    /// </summary>
    /// <remarks>
    /// <c>"text"</c> means "provider default" and maps to <see langword="null"/> — writing
    /// nothing and writing <c>text</c> are equivalent on the wire. Any other value is
    /// forwarded as-is: the mapper used to allow-list two values and downgrade everything
    /// else to <see langword="null"/>, which silently discarded <c>json_schema</c> before it
    /// could reach a provider. An unrecognised value now travels with a warning, so a new
    /// vendor value works without a framework release and a typo surfaces as a provider error
    /// rather than as nothing at all.
    /// </remarks>
    private LlmResponseFormat? MapResponseFormat(string? yaml, ResponseSchemaYamlConfig? schema)
    {
        var trimmed = yaml?.Trim();
        var hasSchema = !string.IsNullOrWhiteSpace(schema?.Schema);

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            // A schema on its own is unambiguous: it can only mean json_schema.
            return hasSchema ? BuildJsonSchemaFormat(schema!) : null;
        }

        if (string.Equals(trimmed, "text", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.Equals(trimmed, "json_schema", StringComparison.OrdinalIgnoreCase))
        {
            if (hasSchema)
                return BuildJsonSchemaFormat(schema!);

            LogJsonSchemaWithoutSchema();
            return LlmResponseFormat.JsonObject();
        }

        // Known values are normalised to their canonical lowercase wire form; anything else
        // travels verbatim, since only the provider can judge it.
        if (string.Equals(trimmed, "json_object", StringComparison.OrdinalIgnoreCase))
            return LlmResponseFormat.JsonObject();

        LogUnknownResponseFormat(trimmed);
        return new LlmResponseFormat { Type = trimmed };
    }

    /// <summary>
    /// Maps the YAML <c>cache:</c> block to its domain value object. Returns null when the
    /// block asks for no breakpoint, so caching stays off unless it was actually requested.
    /// </summary>
    private static LlmCacheConfig? MapCache(CacheYamlConfig? yaml)
    {
        if (yaml is null) return null;

        var config = new LlmCacheConfig
        {
            CacheSystemPrompt = yaml.System ?? false,
            CacheTools = yaml.Tools ?? false,
            Ttl = yaml.Ttl,
        };

        return config.RequestsAnyBreakpoint ? config : null;
    }

    private static LlmResponseFormat BuildJsonSchemaFormat(ResponseSchemaYamlConfig schema) =>
        LlmResponseFormat.JsonSchema(
            string.IsNullOrWhiteSpace(schema.Name) ? "response" : schema.Name.Trim(),
            schema.Schema!,
            schema.Strict ?? true);

    /// <summary>
    /// Maps the YAML <c>llm_override:</c> block to a <see cref="LlmConfigOverride"/>.
    /// Returns <c>null</c> when no field is set (empty block = no patch).
    /// </summary>
    private LlmConfigOverride? MapTaskLlmOverride(LlmOverrideYamlConfig? yaml)
    {
        if (yaml is null) return null;
        if (string.IsNullOrWhiteSpace(yaml.Profile)
            && string.IsNullOrWhiteSpace(yaml.ResponseFormat)
            && yaml.ResponseSchema is null
            && yaml.Temperature is null
            && yaml.MaxTokens is null
            && yaml.TopP is null
            && yaml.Thinking is null)
        {
            return null;
        }

        return new LlmConfigOverride
        {
            Profile = string.IsNullOrWhiteSpace(yaml.Profile) ? null : yaml.Profile.Trim(),
            ResponseFormat = MapResponseFormat(yaml.ResponseFormat, yaml.ResponseSchema),
            Temperature = yaml.Temperature,
            MaxTokens = yaml.MaxTokens,
            TopP = yaml.TopP,
            Thinking = MapThinking(yaml.Thinking),
        };
    }

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning,
        Message = "response_format value '{RawValue}' in crew YAML is not one of the values Orkeon knows ('text' | 'json_object' | 'json_schema'). It is forwarded to the provider as-is; check your provider's documentation if the call fails.")]
    private partial void LogUnknownResponseFormat(string rawValue);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning,
        Message = "response_format is 'json_schema' but no response_schema block was provided — falling back to 'json_object' (well-formed JSON, no validation).")]
    private partial void LogJsonSchemaWithoutSchema();

    /// <summary>
    /// Normalizes the agent-level <c>knowledge:</c> block into validated
    /// <see cref="KnowledgeAttachment"/> values. Two item forms are accepted:
    /// a plain string (short form: the collection name with default options) and a mapping
    /// (long form: <c>collection</c> required, plus <c>top_k</c> / <c>min_score</c> /
    /// <c>profile</c> / <c>max_context_tokens</c>, snake_case or camelCase). Malformed
    /// entries are skipped with a structured warning — a slightly broken crew.yaml must
    /// not crash the loader (same tolerance policy as <c>response_format</c>).
    /// </summary>
    private IReadOnlyList<KnowledgeAttachment> MapKnowledge(string agentKey, IEnumerable<object>? knowledge)
    {
        if (knowledge is null)
            return Array.Empty<KnowledgeAttachment>();

        var result = new List<KnowledgeAttachment>();
        foreach (var entry in knowledge)
        {
            var attachment = MapKnowledgeEntry(agentKey, entry);
            if (attachment is not null)
                result.Add(attachment);
        }

        return result;
    }

    private KnowledgeAttachment? MapKnowledgeEntry(string agentKey, object? entry)
    {
        try
        {
            switch (entry)
            {
                // Short form: `knowledge: [produits, procedures]`
                case string shortForm:
                    return KnowledgeAttachment.Create(shortForm);

                // Long form: `knowledge: [{ collection: procedures, top_k: 8, … }]`.
                // YamlDotNet materializes untyped mappings as Dictionary<object, object>.
                case System.Collections.IDictionary longForm:
                {
                    var fields = NormalizeKnowledgeKeys(longForm);
                    if (!fields.TryGetValue("collection", out var collection) || string.IsNullOrWhiteSpace(collection))
                    {
                        LogKnowledgeEntryMissingCollection(agentKey);
                        return null;
                    }

                    return KnowledgeAttachment.Create(
                        collection,
                        topK: ParseIntField(agentKey, fields, "topk") ?? KnowledgeAttachment.DefaultTopK,
                        minScore: ParseDoubleField(agentKey, fields, "minscore"),
                        profile: fields.GetValueOrDefault("profile"),
                        maxContextTokens: ParseIntField(agentKey, fields, "maxcontexttokens"));
                }

                default:
                    LogKnowledgeEntryUnsupportedShape(agentKey, entry?.GetType().Name ?? "null");
                    return null;
            }
        }
        catch (ArgumentException ex)
        {
            LogKnowledgeEntryInvalid(agentKey, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Flattens an untyped YAML mapping into string fields keyed by their normalized name
    /// (lowercase, underscores stripped) so <c>top_k</c>, <c>topK</c> and <c>TopK</c> all
    /// resolve to <c>topk</c> — the same camelCase/snake_case tolerance the typed models get
    /// from the serializer's type inspector.
    /// </summary>
    private static Dictionary<string, string> NormalizeKnowledgeKeys(System.Collections.IDictionary mapping)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry kv in mapping)
        {
            var key = kv.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                continue;
#pragma warning disable CA1308 // lowercase is the normalized lookup key the YAML fields are matched against
            var normalized = key.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
#pragma warning restore CA1308
            fields[normalized] = kv.Value?.ToString() ?? string.Empty;
        }
        return fields;
    }

    private int? ParseIntField(string agentKey, Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        LogKnowledgeFieldNotNumeric(agentKey, key, raw);
        return null;
    }

    private double? ParseDoubleField(string agentKey, Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;
        LogKnowledgeFieldNotNumeric(agentKey, key, raw);
        return null;
    }

    /// <summary>
    /// Maps the crew-level <c>rag:</c> YAML block to its typed model. Returns null when the
    /// block is absent. Pure parsing — no ingestion is triggered here (kickoff is a later lot).
    /// </summary>
    /// <summary>
    /// Maps the <c>links:</c> block (HUB-03). An absent block maps to <see langword="null"/> —
    /// "never declared", which the ACL arbitrates by policy. A present block always yields a
    /// list, even when every entry had to be dropped: a malformed authorization must close the
    /// door, never open it. An entry without a <c>to:</c>, or with a <c>direction:</c> nobody
    /// can read, is dropped **with a warning** rather than guessed at — a guessed direction is
    /// an authorization the author never wrote.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null",
        Justification = "null is a third state here, not the absence of a value: the ACL reads null as \"the crew never " +
                        "declared a links: block\" and arbitrates it through ICrewLinkPolicy, while an empty list means " +
                        "\"declared, and every entry was dropped\" and closes the door on everything. Returning an empty " +
                        "list would silently lock out every crew that never wrote a links: block.")]
    private List<CrewLink>? MapLinks(string crewName, Collection<LinkYamlConfig>? yaml)
    {
        if (yaml is null)
            return null;

        var links = new List<CrewLink>(yaml.Count);
        foreach (var entry in yaml)
        {
            if (string.IsNullOrWhiteSpace(entry?.To))
            {
                LogLinkDroppedNoTarget(crewName);
                continue;
            }

            if (!TryParseLinkDirection(entry.Direction, out var direction))
            {
                LogLinkDroppedBadDirection(crewName, entry.To.Trim(), entry.Direction!);
                continue;
            }

            links.Add(new CrewLink
            {
                To = entry.To.Trim(),
                Direction = direction,
                AllowedTopics = entry.AllowedTopics is { Count: > 0 } topics ? [.. topics] : [],
            });
        }

        return links;
    }

    private static bool TryParseLinkDirection(string? value, out CrewLinkDirection direction)
    {
        // An absent direction is the common case and means "I want to talk to them": outbound.
        // A present-but-unreadable one is a typo in an authorization, and the entry is dropped
        // by the caller rather than silently granted a direction.
        switch (value?.Trim().ToUpperInvariant())
        {
            case null or "":
            case "OUTBOUND":
                direction = CrewLinkDirection.Outbound;
                return true;
            case "INBOUND":
                direction = CrewLinkDirection.Inbound;
                return true;
            case "BIDIRECTIONAL" or "BOTH":
                direction = CrewLinkDirection.Bidirectional;
                return true;
            default:
                direction = default;
                return false;
        }
    }

    [LoggerMessage(EventId = 111, Level = LogLevel.Warning,
        Message = "Crew '{CrewName}': a links: entry has no to: and was dropped — the door stays closed, but the authorization the author meant is not enforced.")]
    private partial void LogLinkDroppedNoTarget(string crewName);

    [LoggerMessage(EventId = 112, Level = LogLevel.Warning,
        Message = "Crew '{CrewName}': the links: entry for '{To}' carries an unreadable direction '{Direction}' and was dropped — the door stays closed, but the authorization the author meant is not enforced.")]
    private partial void LogLinkDroppedBadDirection(string crewName, string to, string direction);

    private static RagCrewConfig? MapRag(RagYamlConfig? yaml)
    {
        if (yaml is null)
            return null;

        var collections = new Dictionary<string, RagCollectionConfig>(StringComparer.Ordinal);
        if (yaml.Collections is not null)
        {
            foreach (var kvp in yaml.Collections)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                    continue;

                collections[kvp.Key] = new RagCollectionConfig
                {
                    Sources = kvp.Value?.Sources?.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>(),
                    Chunking = MapRagChunking(kvp.Value?.Chunking),
                };
            }
        }

        return new RagCrewConfig
        {
            Collections = collections,
            DefaultProfile = string.IsNullOrWhiteSpace(yaml.Defaults?.Profile) ? null : yaml.Defaults.Profile.Trim(),
        };
    }

    private static RagChunkingConfig? MapRagChunking(RagChunkingYamlConfig? yaml)
    {
        if (yaml is null)
            return null;
        if (string.IsNullOrWhiteSpace(yaml.Strategy) && yaml.MaxTokens is null && yaml.Overlap is null)
            return null;

        var defaults = new RagChunkingConfig();
        return new RagChunkingConfig
        {
            Strategy = string.IsNullOrWhiteSpace(yaml.Strategy) ? defaults.Strategy : yaml.Strategy.Trim(),
            MaxTokens = yaml.MaxTokens ?? defaults.MaxTokens,
            Overlap = yaml.Overlap ?? defaults.Overlap,
        };
    }

    [LoggerMessage(EventId = 106, Level = LogLevel.Warning,
        Message = "Agent '{AgentKey}': knowledge entry (long form) has no 'collection' key — entry skipped.")]
    private partial void LogKnowledgeEntryMissingCollection(string agentKey);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning,
        Message = "Agent '{AgentKey}': knowledge entry of unsupported shape '{Shape}' — expected a collection name (string) or a mapping with 'collection'. Entry skipped.")]
    private partial void LogKnowledgeEntryUnsupportedShape(string agentKey, string shape);

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning,
        Message = "Agent '{AgentKey}': invalid knowledge entry — {Reason} Entry skipped.")]
    private partial void LogKnowledgeEntryInvalid(string agentKey, string reason);

    [LoggerMessage(EventId = 105, Level = LogLevel.Warning,
        Message = "Agent '{AgentKey}': knowledge field '{Field}' value '{RawValue}' is not numeric — field ignored.")]
    private partial void LogKnowledgeFieldNotNumeric(string agentKey, string field, string rawValue);

    /// <summary>
    /// Maps a YAML guardrails section to a <see cref="GuardrailsConfig"/> domain model.
    /// Supports preset resolution, custom rules, and tool-specific clauses — or any combination.
    /// </summary>
    private static GuardrailsConfig? MapGuardrails(GuardrailsYamlConfig? yaml)
    {
        if (yaml == null)
            return null;

        // Start from preset if specified
        var baseConfig = GuardrailPresets.FromName(yaml.Preset);

        // Build custom rules from YAML
        var hasCustomRules = (yaml.Rules?.Count ?? 0) > 0 || (yaml.ToolRules?.Count ?? 0) > 0 || yaml.Header != null;

        if (!hasCustomRules && baseConfig != null)
            return baseConfig;

        if (!hasCustomRules && baseConfig == null)
            return null; // Empty guardrails section — nothing to do

        var customConfig = new GuardrailsConfig
        {
            Header = yaml.Header,
            Rules = yaml.Rules?.ToList() ?? [],
            ToolRules = yaml.ToolRules?
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IReadOnlyList<string>)kvp.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        };

        return baseConfig != null
            ? baseConfig.MergeWith(customConfig)
            : customConfig;
    }

    /// <summary>
    /// Maps a YAML graph config section to a <see cref="GraphConfig"/> domain model.
    /// </summary>
    private static GraphConfig? MapGraphConfig(GraphYamlConfig? yaml)
    {
        if (yaml == null)
            return null;

        return new GraphConfig
        {
            MaxRetryCycles = yaml.MaxRetryCycles ?? 2,
            CircuitBreakerPreset = yaml.CircuitBreakerPreset ?? "strict",
            MaxTransitions = yaml.MaxTransitions,
            MaxStateVisits = yaml.MaxStateVisits,
            MaxTotalDurationSeconds = yaml.MaxTotalDurationSeconds,
        };
    }

    /// <summary>
    /// The agent <c>managerAgent:</c> names, by its key. A name the crew does not have — a typo —
    /// fails the load, listing its agents (GAP-33, decision 4): erased, it told a hierarchical crew it
    /// "requires a manager agent", and took a consensual crew's arbiter away without a word.
    /// </summary>
    private static AgentId? ResolveManagerAgentId(string? name, Dictionary<string, AgentId> agentNameMap)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (agentNameMap.TryGetValue(name, out var id))
            return id;

        throw new InvalidOperationException(
            $"managerAgent: {name} names no agent of the crew. Its agents: {Known(agentNameMap.Keys)}. " +
            "Name the manager by its key under agents:.");
    }

    /// <summary>
    /// <c>managerAgent:</c> means something in two modes only (GAP-33): Hierarchical — the manager
    /// assigns each task and reviews its output — and Consensual — it arbitrates the vote under the
    /// <c>ManagerDecision</c> fallback. In the four others the agent was read, then ran tasks like any
    /// other: the load fails, naming the key and the process as the crew file writes them.
    /// </summary>
    /// <param name="process">The crew's process.</param>
    /// <param name="writtenProcess">The process as the crew file spells it, for the message.</param>
    /// <param name="managerAgent">The crew's <c>managerAgent:</c>.</param>
    private static void RefuseManagerTheModeHasNoneOf(ProcessType process, string? writtenProcess, string? managerAgent)
    {
        if (process.AcceptsManagerAgent || string.IsNullOrWhiteSpace(managerAgent))
            return;

        var mode = string.IsNullOrWhiteSpace(writtenProcess)
            ? "the crew's process — sequential, the default when process: is absent —"
            : $"process: {writtenProcess.Trim()}";
        throw new InvalidOperationException(
            $"managerAgent: {managerAgent} is set, but {mode} has no manager: the agent would only be one more worker. " +
            "Remove managerAgent:, or use process: hierarchical (the manager assigns each task and reviews its output) or " +
            "process: consensual (it arbitrates the vote when it fails, under the host's Orkeon:Consensus:FallbackStrategy: " +
            "ManagerDecision)." +
            (process == ProcessType.Autonomous
                ? " An autonomous crew's manager is an LLM: the host's default profile (in C#, the provider " +
                  "CrewBuilder.WithManagerLlm sets)."
                : string.Empty));
    }

    /// <summary>
    /// A task's <c>asyncExecution: true</c> is a promise only Sequential keeps — the task runs
    /// alongside the tasks after it — and Parallel accepts, a wave running at once anyway (GAP-22).
    /// The four other modes order their tasks themselves, so the load fails, naming every task that
    /// asks for it by its key: kept, the flag would be ignored in silence (the rule of GAP-07).
    /// </summary>
    /// <param name="process">The crew's process.</param>
    /// <param name="writtenProcess">The process as the crew file spells it, for the message.</param>
    /// <param name="tasks">The crew's tasks, by key.</param>
    private static void RefuseAsyncExecutionTheModeIgnores(
        ProcessType process, string? writtenProcess, Dictionary<string, TaskYamlConfig>? tasks)
    {
        if (process.AcceptsAsyncExecution || tasks is null)
            return;

        var asking = tasks.Where(t => t.Value?.AsyncExecution == true).Select(t => $"'{t.Key}'").ToList();
        if (asking.Count == 0)
            return;

        throw new InvalidOperationException(
            (asking.Count == 1 ? $"Task {asking[0]} sets" : $"Tasks {string.Join(", ", asking)} set") +
            $" asyncExecution: true, which process: {writtenProcess?.Trim() ?? process.Value} does not honour: that mode " +
            "orders its tasks itself. Remove asyncExecution, or use process: sequential (an async task runs alongside " +
            "the tasks after it) or process: parallel (the tasks whose dependencies are met already run at once).");
    }

    /// <summary>
    /// Parses a process-type string (case-insensitive) into the domain value object. Absent
    /// means <see cref="ProcessType.Sequential"/>; anything the domain does not know is an
    /// error naming what it does.
    /// <para>
    /// A hand-rolled switch used to fall through to Sequential, so <c>process: graf</c> — or
    /// <c>process: paralell</c>, or a mode added to the domain and not to this list — ran a
    /// pipeline the author did not ask for and never said so. The neighbouring
    /// <c>deliverable:</c> parser refuses an unknown value; the scripting authoring path
    /// refuses one; only the YAML entry point guessed.
    /// </para>
    /// </summary>
    public static ProcessType ParseProcessType(string? processStr)
    {
        if (string.IsNullOrWhiteSpace(processStr))
            return ProcessType.Sequential;

        if (ProcessType.TryFrom(processStr.Trim(), out var process))
            return process!;

        throw new InvalidOperationException(
            $"Unknown crew process '{processStr}'. Expected one of: "
            + string.Join(", ", ProcessType.All.Select(p => p.Value)) + ".");
    }
}

/// <summary>
/// The crew's own settings block (everything sourced from the <c>crew:</c> section), as opposed
/// to the agent and task children. Bundles the crew-level scalars (name, goal, process, flags,
/// manager) together with the structured crew-level config blocks (graph, default LLM, RAG, links, mounts).
/// Consumed by <see cref="YamlCrewMapper.BuildConfiguration"/>.
/// </summary>
public sealed record CrewMappingSettings
{
    /// <summary>Crew display name (<c>crew.name</c>).</summary>
    public string? Name { get; init; }

    /// <summary>Crew goal / mission statement (<c>crew.goal</c>).</summary>
    public string? Goal { get; init; }

    /// <summary>Raw process-type string (<c>crew.process</c>); parsed via <see cref="YamlCrewMapper.ParseProcessType"/>.</summary>
    public string? Process { get; init; }

    /// <summary>Verbose logging flag (<c>crew.verbose</c>).</summary>
    public bool? Verbose { get; init; }

    /// <summary>Whether crew memory is enabled (<c>crew.memory</c>).</summary>
    public bool? Memory { get; init; }

    /// <summary>Memory provider identifier (<c>crew.memory_provider</c>).</summary>
    public string? MemoryProvider { get; init; }

    /// <summary>Whether planning is enabled (<c>crew.planning</c>).</summary>
    public bool? Planning { get; init; }

    /// <summary>
    /// Key of the crew's manager agent (<c>crew.managerAgent</c>): Hierarchical and Consensual only,
    /// and one of the crew's agents (GAP-33).
    /// </summary>
    public string? ManagerAgent { get; init; }

    /// <summary>Crew-level graph orchestration configuration (<c>crew.graph</c>).</summary>
    public GraphYamlConfig? GraphConfig { get; init; }

    /// <summary>Crew-level default LLM block, merged onto each agent (<c>crew.llm</c>).</summary>
    public LlmYamlConfig? CrewDefaultLlm { get; init; }

    /// <summary>Crew-level RAG block — provider, declared collections, retrieval defaults (<c>crew.rag</c>).</summary>
    public RagYamlConfig? Rag { get; init; }

    /// <summary>Crew-level EventHub authorizations (<c>crew.links</c>).</summary>
    public Collection<LinkYamlConfig>? Links { get; init; }

    /// <summary>Crew-level mount references (<c>crew.mounts</c>, VFS-90): roots, optionally pinned to a settings entry by id.</summary>
    public Collection<string>? Mounts { get; init; }
}
