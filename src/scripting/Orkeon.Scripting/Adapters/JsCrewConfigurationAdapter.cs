using System.Text.Json;
using Jint;
using Jint.Native;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Adapters;

/// <summary>
/// Converts a <see cref="JsCrew"/> (the value handed off via
/// <c>(globalThis as any).crew = crewBuilder()…build()</c> by a <c>.ork.ts</c> script)
/// into a <see cref="CrewConfiguration"/>, so the script-authored crew can be executed
/// through the same <c>ICrewOrchestrationService</c> pipeline as YAML crews — getting
/// deliverable resolvers, AutoSummaryWriter, per-task telemetry, and per-tool logging
/// for free.
/// </summary>
/// <remarks>
/// Before this adapter, the only way to execute a <c>.ork.ts</c> crew was
/// <see cref="ScriptHost.RunFromFileAsync"/> which invokes <c>JsCrew.RunAsync</c> — the
/// lean script-runtime path that does NOT honor task deliverables or AUTO_SUMMARY.md.
/// </remarks>
public static class JsCrewConfigurationAdapter
{
    /// <summary>
    /// Converts <paramref name="crew"/> into a <see cref="CrewConfiguration"/>.
    /// <see cref="JsAgent"/> identity is preserved by reference: every <c>JsAgent</c>
    /// in the crew gets a freshly minted <see cref="AgentId"/>; every <c>JsTask</c>
    /// gets a freshly minted <see cref="TaskId"/>. Cross-references (task agent
    /// assignment, task→task <c>withContext</c> chains, manager) are resolved through
    /// those reference maps, so the resulting DAG matches the script-side topology.
    /// </summary>
    public static CrewConfiguration ToConfiguration(JsCrew crew)
    {
        ArgumentNullException.ThrowIfNull(crew);

        var agentMap = new Dictionary<JsAgent, AgentId>(ReferenceEqualityComparer.Instance);
        var agentConfigs = new List<AgentConfiguration>();
        foreach (var jsAgent in crew.agents)
        {
            var agentId = AgentId.Create();
            agentMap[jsAgent] = agentId;
            agentConfigs.Add(BuildAgentConfiguration(agentId, jsAgent));
        }

        // First pass: mint TaskIds so dependencies in the second pass can reference them.
        var jsTasks = crew.Tasks.OfType<JsTask>().ToList();
        var taskMap = new Dictionary<JsTask, TaskId>(ReferenceEqualityComparer.Instance);
        foreach (var jsTask in jsTasks)
            taskMap[jsTask] = TaskId.Create();

        var taskConfigs = new List<TaskConfiguration>();
        foreach (var jsTask in jsTasks)
            taskConfigs.Add(BuildTaskConfiguration(taskMap[jsTask], jsTask, agentMap, taskMap));

        AgentId? managerId = null;
        if (crew.Manager is { } mgr && agentMap.TryGetValue(mgr, out var mid))
            managerId = mid;

        return new CrewConfiguration
        {
            Name = crew.name,
            Goal = ResolveCrewGoal(crew),
            Process = ParseProcessType(crew.Process),
            Verbose = crew.Verbose,
            Memory = crew.Memory,
            ManagerAgentId = managerId,
            Agents = agentConfigs,
            Tasks = taskConfigs,
        };
    }

    private static AgentConfiguration BuildAgentConfiguration(AgentId id, JsAgent jsAgent)
    {
        var builder = jsAgent.Builder;
        var llmConfig = ExtractLlmConfig(builder.LlmConfig);

        // Merge agentBuilder().withResponseFormat("json_object") / .withResponseSchema(...)
        // onto the LlmConfig. Seed an empty LlmConfig if the script didn't call .llm({...})
        // — otherwise the constraint gets dropped silently.
        if (!string.IsNullOrWhiteSpace(builder.ResponseFormatValue) || builder.ResponseSchemaValue is not null)
        {
            llmConfig ??= LlmConfig.Default();
            llmConfig = llmConfig with
            {
                ResponseFormat = BuildResponseFormat(
                    builder.ResponseFormatValue,
                    builder.ResponseSchemaName,
                    builder.ResponseSchemaValue,
                    builder.ResponseSchemaStrict),
            };
        }

        return new AgentConfiguration
        {
            Id = id,
            Role = builder.AgentRole ?? jsAgent.name,
            Goal = builder.AgentGoal ?? string.Empty,
            Backstory = builder.AgentBackstoryText ?? string.Empty,
            // .tools(["file_read", ...]) is the canonical YAML-parity surface —
            // names resolve through IToolRegistry at runtime. Strings passed through
            // .withAutonomousTool(...) are accepted for backward compatibility, and
            // JsTool INSTANCES now contribute their names too: the loader registers
            // the instances with the runtime registry before the crew is created
            // (CollectScriptTools), so the names resolve like any built-in (EX-01).
            Tools = CollectAgentToolNames(builder),
            AllowDelegation = builder.AllowDelegationFlag,
            MaxIterations = builder.MaxIterationsValue,
            // MaxRPM has no DSL surface yet; keep the loader-equivalent default.
            MaxRPM = 10,
            Verbose = builder.VerboseFlag,
            LlmConfig = llmConfig,
        };
    }

    private static TaskConfiguration BuildTaskConfiguration(
        TaskId id,
        JsTask jsTask,
        Dictionary<JsAgent, AgentId> agentMap,
        Dictionary<JsTask, TaskId> taskMap)
    {
        AgentId? assigned = null;
        if (jsTask.AssignedAgent is { } a && agentMap.TryGetValue(a, out var aid))
            assigned = aid;

        var deps = new List<TaskId>();
        foreach (var ctxTask in jsTask.Context)
        {
            if (taskMap.TryGetValue(ctxTask, out var depId))
                deps.Add(depId);
        }

        return new TaskConfiguration
        {
            Id = id,
            Description = jsTask.description,
            ExpectedOutput = jsTask.expectedOutput,
            AssignedAgentId = assigned,
            Dependencies = deps,
            // .deliverable({...}) is the YAML-parity surface — produces a full
            // TaskDeliverable contract (path + source + format + schema). When absent,
            // the legacy .expect({...}) JSON-schema is captured in Context for
            // observability (the orchestrator doesn't currently validate against it,
            // matching the YAML "no deliverable block" behavior).
            Deliverable = MapDeliverable(jsTask.DeliverableSpec),
            Context = ExtractTaskContext(jsTask),
            LlmOverride = BuildTaskLlmOverride(jsTask),
            HumanInput = jsTask.HumanInputFlag,
            AsyncExecution = jsTask.AsyncExecutionFlag,
            RequiredTools = CollectToolNames(jsTask.Tools, seed: null),
        };
    }

    /// <summary>
    /// Materialises <c>taskBuilder().withResponseFormat(...)</c> / <c>.withResponseSchema(...)</c>
    /// into a <see cref="LlmConfigOverride"/>. Returns <c>null</c> for the no-op cases
    /// (unset, or <c>"text"</c> which is the provider default).
    /// </summary>
    private static LlmConfigOverride? BuildTaskLlmOverride(JsTask jsTask)
    {
        var format = BuildResponseFormat(
            jsTask.ResponseFormatValue,
            jsTask.ResponseSchemaName,
            jsTask.ResponseSchema,
            jsTask.ResponseSchemaStrict);

        return format is null ? null : LlmConfigOverride.ForResponseFormat(format);
    }

    /// <summary>
    /// Builds the domain response-format constraint from the two DSL entry points. A schema
    /// implies <c>json_schema</c>, so a script only needs <c>withResponseSchema</c>; an
    /// explicit <c>"text"</c> clears the constraint back to the provider default.
    /// </summary>
    private static LlmResponseFormat? BuildResponseFormat(
        string? type, string? schemaName, JsValue? schema, bool strict)
    {
        var schemaJson = schema is null ? null : SerializeJsValueToJson(schema);
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            return LlmResponseFormat.JsonSchema(
                string.IsNullOrWhiteSpace(schemaName) ? "response" : schemaName!, schemaJson!, strict);
        }

        if (string.IsNullOrWhiteSpace(type)) return null;
        if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase)) return null;
        return new LlmResponseFormat { Type = type! };
    }

    private static Dictionary<string, object> ExtractTaskContext(JsTask jsTask)
    {
        var ctx = new Dictionary<string, object>();
        // .expect() only flows to Context when no .deliverable() block consumed it.
        if (jsTask.DeliverableSpec is not null) return ctx;
        if (jsTask.ExpectSchema is null || jsTask.ExpectSchema.IsUndefined() || jsTask.ExpectSchema.IsNull())
            return ctx;

        var schemaJson = SerializeJsValueToJson(jsTask.ExpectSchema);
        if (!string.IsNullOrWhiteSpace(schemaJson))
            ctx["expect_schema"] = schemaJson;
        return ctx;
    }

    private static TaskDeliverable? MapDeliverable(JsValue? spec)
    {
        if (spec is null || spec.IsUndefined() || spec.IsNull() || !spec.IsObject())
            return null;

        var pathRaw = spec.Get("path");
        if (!pathRaw.IsString() || string.IsNullOrWhiteSpace(pathRaw.AsString()))
            // YAML-parity: an empty/missing path means "no deliverable block".
            return null;

        var sourceRaw = spec.Get("source");
        var source = ParseDeliverableSource(sourceRaw.IsString() ? sourceRaw.AsString() : null);

        var formatRaw = spec.Get("format");
        var format = formatRaw.IsString() && !string.IsNullOrWhiteSpace(formatRaw.AsString())
            ? formatRaw.AsString() : "markdown";

        var sanitizeRaw = spec.Get("sanitize");
        var sanitize = !sanitizeRaw.IsBoolean() || sanitizeRaw.AsBoolean();

        var schemaPathRaw = spec.Get("schemaPath");
        var schemaPath = schemaPathRaw.IsString() ? schemaPathRaw.AsString() : null;

        // schema (inline object) wins over schemaInline (pre-stringified).
        string? schemaInline = null;
        var schemaObj = spec.Get("schema");
        if (!schemaObj.IsUndefined() && !schemaObj.IsNull())
            schemaInline = SerializeJsValueToJson(schemaObj);
        if (string.IsNullOrWhiteSpace(schemaInline))
        {
            var schemaInlineRaw = spec.Get("schemaInline");
            if (schemaInlineRaw.IsString())
                schemaInline = schemaInlineRaw.AsString();
        }

        var deliverable = new TaskDeliverable
        {
            Path = pathRaw.AsString(),
            Source = source,
            Format = format,
            Sanitize = sanitize,
            SchemaPath = schemaPath,
            SchemaInline = schemaInline,
        };
        deliverable.Validate();
        return deliverable;
    }

    private static DeliverableSource ParseDeliverableSource(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DeliverableSource.ToolCall;
#pragma warning disable CA1308 // normalized key for a switch; lowercase is the required form, not a comparison normalization
        return raw.Trim().ToLowerInvariant() switch
        {
            "none" => DeliverableSource.None,
            "tool_call" or "toolcall" => DeliverableSource.ToolCall,
            "final_message" or "finalmessage" => DeliverableSource.FinalMessage,
            "structured_output" or "structuredoutput" => DeliverableSource.StructuredOutput,
            _ => throw new InvalidOperationException(
                $"Unknown deliverable source '{raw}'. Expected: final_message, structured_output, tool_call, none."),
        };
#pragma warning restore CA1308
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort schema flattening: the JSON schema is optional for the orchestrator, so any failure of Jint's ToObject() or JsonSerializer.Serialize is swallowed and the schema is dropped (null) rather than aborting crew configuration.")]
    private static string? SerializeJsValueToJson(JsValue value)
    {
        try
        {
            var clr = value.ToObject();
            return JsonSerializer.Serialize(clr);
        }
        catch (Exception)
        {
            // The schema isn't required for the orchestrator to run; drop it silently
            // when it can't be flattened to JSON (rare — Jint's ToObject() handles
            // plain object trees natively).
            return null;
        }
    }

    private static LlmConfig? ExtractLlmConfig(JsValue? llmValue)
    {
        if (llmValue is null || llmValue.IsUndefined() || llmValue.IsNull())
            return null;

        var clr = llmValue.ToObject();
        if (clr is not JsLlmConfig jsLlm)
            return null;

        return jsLlm.Domain with
        {
            // JsLlmConfig stores temperature/maxTokens overrides separately from Domain
            // (Domain.Create only takes a model name). Apply the overrides here so the
            // orchestrator sees the values the script set.
            Temperature = jsLlm.temperature ?? jsLlm.Domain.Temperature,
            MaxTokens = jsLlm.maxTokens ?? jsLlm.Domain.MaxTokens,
            BaseUrl = jsLlm.baseUrl ?? jsLlm.Domain.BaseUrl,
        };
    }

    private static IReadOnlyList<string> CollectAgentToolNames(JsAgentBuilder builder)
    {
        var names = new List<string>();
        // 1. Canonical YAML-parity surface — agentBuilder().tools(["file_read", ...]).
        foreach (var name in builder.BuiltInToolNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        // 2. Tool values from .withAutonomousTool*: strings (back-compat) and JsTool
        //    instances alike contribute their name — the instances themselves are
        //    registered by the loader (CollectScriptTools) before resolution runs.
        return CollectToolNames(builder.AutonomousTools, seed: names);
    }

    private static IReadOnlyList<string> CollectToolNames(IReadOnlyList<JsValue> values, List<string>? seed)
    {
        var names = seed ?? new List<string>();
        foreach (var tool in values)
        {
            if (tool is null || tool.IsUndefined() || tool.IsNull()) continue;
            string? name = null;
            if (tool.IsString()) name = tool.AsString();
            else if (tool.ToObject() is JsTool jsTool) name = jsTool.Name;
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name!, StringComparer.Ordinal))
                names.Add(name!);
        }
        return names.Count > 0 ? names : Array.Empty<string>();
    }

    /// <summary>
    /// Every <c>toolBuilder()</c> instance the crew references — through
    /// <c>agentBuilder().withAutonomousTool(...)</c> or <c>taskBuilder().tools([...])</c> —
    /// deduplicated by reference. The loader registers them with the runtime
    /// <c>IToolRegistry</c> BEFORE <c>ICrewFactory.CreateFromConfigAsync</c> runs, so the
    /// names emitted by <see cref="ToConfiguration"/> resolve under strict tool
    /// resolution exactly like built-ins (EX-01; precedent: MCP tools register the same way).
    /// </summary>
    public static IReadOnlyList<JsTool> CollectScriptTools(JsCrew crew)
    {
        ArgumentNullException.ThrowIfNull(crew);
        var tools = new List<JsTool>();

        void AddValue(JsValue? value)
        {
            if (value is null || value.IsUndefined() || value.IsNull()) return;
            if (value.ToObject() is JsTool tool && !tools.Any(t => ReferenceEquals(t, tool)))
                tools.Add(tool);
        }

        foreach (var agent in crew.agents)
            foreach (var raw in agent.Builder.AutonomousTools)
                AddValue(raw);

        foreach (var jsTask in crew.Tasks.OfType<JsTask>())
            foreach (var raw in jsTask.Tools)
                AddValue(raw);

        return tools;
    }

    private static string ResolveCrewGoal(JsCrew crew)
    {
        // YamlCrewDefinitionLoader.Validate rejects empty Goal; synthesize one from the
        // crew name when the script didn't call .goal(...). Keeps the validator happy
        // without forcing every existing .ork.ts to declare a goal.
        if (!string.IsNullOrWhiteSpace(crew.Goal))
            return crew.Goal!;
        return $"Execute crew '{crew.name}'";
    }

    /// <summary>
    /// Absent means <see cref="ProcessType.Sequential"/>; anything the domain does not know is
    /// an error naming what it does. One list, in the value object — a second copy here is how
    /// a sixth mode gets added to the domain and silently ignored by a script.
    /// </summary>
    private static ProcessType ParseProcessType(string? processStr)
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
