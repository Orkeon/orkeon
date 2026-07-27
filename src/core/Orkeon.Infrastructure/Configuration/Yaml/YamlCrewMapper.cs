using System.Globalization;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Constants.Llm;

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

        var mappedAgents = agents != null
            ? MapAgents(agents, settings.CrewDefaultLlm, out var agentNameMap)
            : MapAgentsEmpty(out agentNameMap);

        return new CrewConfiguration
        {
            Name = settings.Name ?? string.Empty,
            Goal = settings.Goal ?? string.Empty,
            Process = ParseProcessType(settings.Process),
            Verbose = settings.Verbose ?? false,
            Memory = settings.Memory ?? false,
            MemoryProvider = settings.MemoryProvider,
            Planning = settings.Planning ?? false,
            Agents = mappedAgents,
            Tasks = tasks != null ? MapTasks(tasks, agentNameMap) : [],
            ManagerAgentId = ResolveAgentId(settings.ManagerAgent, agentNameMap),
            CircuitBreaker = MapCircuitBreaker(settings.CircuitBreaker),
            GraphConfig = MapGraphConfig(settings.GraphConfig),
            Rag = MapRag(settings.Rag),
        };
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
                LlmConfig = effectiveLlm != null
                    ? LlmConfig.Create(effectiveLlm.Model ?? LlmDefaults.DefaultModelName) with
                    {
                        Temperature = effectiveLlm.Temperature ?? LlmDefaults.DefaultTemperature,
                        MaxTokens = effectiveLlm.MaxTokens ?? 4096,
                        TopP = effectiveLlm.TopP ?? 1.0,
                        Thinking = MapThinking(effectiveLlm.Thinking),
                        ResponseFormat = MapResponseFormat(effectiveLlm.ResponseFormat, effectiveLlm.ResponseSchema),
                    }
                    : null,
                Guardrails = MapGuardrails(kvp.Value.Guardrails),
                KnowledgeAttachments = MapKnowledge(kvp.Key, kvp.Value.Knowledge),
            });
        }

        return result;
    }

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
        foreach (var kvp in tasks)
        {
            var dependencies = new List<TaskId>();
            if (kvp.Value.Dependencies != null)
            {
                foreach (var dep in kvp.Value.Dependencies)
                {
                    if (taskNameToId.TryGetValue(dep, out var depId))
                        dependencies.Add(depId);
                }
            }

            result.Add(new TaskConfiguration
            {
                Id = taskNameToId[kvp.Key],
                Description = kvp.Value.Description ?? string.Empty,
                ExpectedOutput = kvp.Value.ExpectedOutput ?? string.Empty,
                AssignedAgentId = kvp.Value.Agent != null && agentNameMap.TryGetValue(kvp.Value.Agent, out var agentId)
                    ? agentId : null,
                Dependencies = dependencies,
                RequiredTools = kvp.Value.Tools ?? (IReadOnlyList<string>)Array.Empty<string>(),
                AsyncExecution = kvp.Value.AsyncExecution ?? false,
                HumanInput = kvp.Value.HumanInput ?? false,
                Context = kvp.Value.Context ?? [],
                CircuitBreaker = MapCircuitBreaker(kvp.Value.CircuitBreaker),
                Deliverable = MapDeliverable(kvp.Value.Deliverable),
                LlmOverride = MapTaskLlmOverride(kvp.Value.LlmOverride),
                Guardrails = MapGuardrails(kvp.Value.Guardrails),
            });
        }

        return result;
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
            Model = agentLevel.Model ?? crewLevel.Model,
            Temperature = agentLevel.Temperature ?? crewLevel.Temperature,
            MaxTokens = agentLevel.MaxTokens ?? crewLevel.MaxTokens,
            TopP = agentLevel.TopP ?? crewLevel.TopP,
            Thinking = MergeThinkingYamlConfig(crewLevel.Thinking, agentLevel.Thinking),
            ResponseFormat = string.IsNullOrWhiteSpace(agentLevel.ResponseFormat) ? crewLevel.ResponseFormat : agentLevel.ResponseFormat,
            ResponseSchema = agentLevel.ResponseSchema ?? crewLevel.ResponseSchema,
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
        if (string.IsNullOrWhiteSpace(yaml.ResponseFormat)
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
            Provider = string.IsNullOrWhiteSpace(yaml.Provider) ? null : yaml.Provider.Trim(),
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

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning,
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
    /// Maps a YAML circuit breaker section to a <see cref="CircuitBreakerConfig"/> domain model.
    /// </summary>
    private static CircuitBreakerConfig? MapCircuitBreaker(CircuitBreakerYamlConfig? yaml)
    {
        if (yaml == null)
            return null;

        return new CircuitBreakerConfig
        {
            Preset = yaml.Preset,
            MaxTransitions = yaml.MaxTransitions,
            StateTimeoutSeconds = yaml.StateTimeoutSeconds,
            MaxStateVisits = yaml.MaxStateVisits,
            MaxTotalDurationSeconds = yaml.MaxTotalDurationSeconds,
            UseDegradedMode = yaml.UseDegradedMode,
            MaxRetries = yaml.MaxRetries,
            MaxToolCallsPerRound = yaml.MaxToolCallsPerRound,
            MaxValidationRetries = yaml.MaxValidationRetries,
        };
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

    private static AgentId? ResolveAgentId(string? name, Dictionary<string, AgentId> agentNameMap)
    {
        if (name == null) return null;
        return agentNameMap.TryGetValue(name, out var id) ? id : null;
    }

    /// <summary>Parses a process-type string (case-insensitive) into the domain value object.</summary>
    public static ProcessType ParseProcessType(string? processStr)
    {
        if (string.IsNullOrWhiteSpace(processStr))
            return ProcessType.Sequential;

#pragma warning disable CA1308 // lowercase is the normalized switch subject the YAML keys are matched against
        return processStr.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "sequential" => ProcessType.Sequential,
            "hierarchical" => ProcessType.Hierarchical,
            "consensual" => ProcessType.Consensual,
            "parallel" => ProcessType.Parallel,
            "graph" => ProcessType.Graph,
            "autonomous" => ProcessType.Autonomous,
            _ => ProcessType.Sequential,
        };
    }
}

/// <summary>
/// The crew's own settings block (everything sourced from the <c>crew:</c> section), as opposed
/// to the agent and task children. Bundles the crew-level scalars (name, goal, process, flags,
/// manager) together with the structured crew-level config blocks (circuit breaker, graph, default LLM).
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

    /// <summary>Name of the manager agent for hierarchical crews (<c>crew.manager_agent</c>).</summary>
    public string? ManagerAgent { get; init; }

    /// <summary>Crew-level circuit breaker configuration (<c>crew.circuit_breaker</c>).</summary>
    public CircuitBreakerYamlConfig? CircuitBreaker { get; init; }

    /// <summary>Crew-level graph orchestration configuration (<c>crew.graph</c>).</summary>
    public GraphYamlConfig? GraphConfig { get; init; }

    /// <summary>Crew-level default LLM block, merged onto each agent (<c>crew.llm</c>).</summary>
    public LlmYamlConfig? CrewDefaultLlm { get; init; }

    /// <summary>Crew-level RAG block — provider, declared collections, retrieval defaults (<c>crew.rag</c>).</summary>
    public RagYamlConfig? Rag { get; init; }
}
