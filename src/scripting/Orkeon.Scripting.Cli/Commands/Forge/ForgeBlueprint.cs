using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>Crew-level settings of a blueprint (SPEC-ORKEON-FORGE §7.3).</summary>
internal sealed record ForgeBlueprintCrew
{
    /// <summary>Crew display name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Crew goal.</summary>
    [JsonPropertyName("goal")]
    public string? Goal { get; init; }

    /// <summary>Orchestration mode; <c>sequential</c> is the default and the only one with fine test progress.</summary>
    [JsonPropertyName("process")]
    public string? Process { get; init; }

    /// <summary>Verbose crew logging.</summary>
    [JsonPropertyName("verbose")]
    public bool? Verbose { get; init; }

    /// <summary>Crew memory.</summary>
    [JsonPropertyName("memory")]
    public bool? Memory { get; init; }
}

/// <summary>One agent of the plan.</summary>
internal sealed record ForgeBlueprintAgent
{
    /// <summary>Stable key — the per-entity file stem and the value tasks refer to.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    /// <summary>Role, phrased as a job title.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    /// <summary>What this agent is for.</summary>
    [JsonPropertyName("goal")]
    public string? Goal { get; init; }

    /// <summary>Grounding persona.</summary>
    [JsonPropertyName("backstory")]
    public string? Backstory { get; init; }

    /// <summary>Tool names, from the injected catalogue only — validation refuses anything else.</summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>Whether the agent may delegate.</summary>
    [JsonPropertyName("allowDelegation")]
    public bool? AllowDelegation { get; init; }

    /// <summary>Iteration cap of the agent's own loop.</summary>
    [JsonPropertyName("maxIterations")]
    public int? MaxIterations { get; init; }
}

/// <summary>One task of the plan.</summary>
internal sealed record ForgeBlueprintTask
{
    /// <summary>Stable key — the per-entity file stem and the value dependencies refer to.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    /// <summary>What must be done.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>What done looks like.</summary>
    [JsonPropertyName("expectedOutput")]
    public string? ExpectedOutput { get; init; }

    /// <summary>Key of the agent in charge.</summary>
    [JsonPropertyName("agent")]
    public string? Agent { get; init; }

    /// <summary>Keys of the tasks this one waits for.</summary>
    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string>? Dependencies { get; init; }

    /// <summary>Virtual path of a framework-managed output file (<c>/output/…</c>), when the task produces one.</summary>
    [JsonPropertyName("deliverable")]
    public string? Deliverable { get; init; }
}

/// <summary>
/// The team plan the assistant submits through <c>blueprint_submit</c> (SPEC-ORKEON-FORGE
/// §7.3). Structural rules live here, and so do the by-key references — the manager, each
/// task's agent and dependencies — checked before compilation: the YAML mapper refuses a
/// reference that names nothing, and a manager in a mode that has none (GAP-33), by an
/// exception that would end the session. What survives the mapping (cycles, empties, the
/// hierarchical manager requirement) belongs to the shared <c>CrewDefinitionValidator</c>.
/// </summary>
internal sealed record ForgeBlueprint
{
    /// <summary>Crew-level settings.</summary>
    [JsonPropertyName("crew")]
    public ForgeBlueprintCrew? Crew { get; init; }

    /// <summary>The agents, in presentation order.</summary>
    [JsonPropertyName("agents")]
    public IReadOnlyList<ForgeBlueprintAgent>? Agents { get; init; }

    /// <summary>The tasks, in execution order.</summary>
    [JsonPropertyName("tasks")]
    public IReadOnlyList<ForgeBlueprintTask>? Tasks { get; init; }

    /// <summary>
    /// Key of the manager agent, hierarchical or consensual crews only: it assigns each task and
    /// reviews its output, or arbitrates the vote.
    /// </summary>
    [JsonPropertyName("manager")]
    public string? Manager { get; init; }

    /// <summary>Why this shape of team, in plain words — what the UI shows under the proposal.</summary>
    [JsonPropertyName("rationale")]
    public string? Rationale { get; init; }

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parses a submitted blueprint; errors, never exceptions.</summary>
    public static bool TryParse(string json, out ForgeBlueprint? blueprint, out IReadOnlyList<string> errors)
    {
        blueprint = null;

        try
        {
            blueprint = JsonSerializer.Deserialize<ForgeBlueprint>(json, ParseOptions);
        }
        catch (JsonException ex)
        {
            errors = [$"The blueprint is not valid JSON: {ex.Message}"];
            return false;
        }

        if (blueprint is null)
        {
            errors = ["The blueprint is empty."];
            return false;
        }

        errors = blueprint.Validate();
        return errors.Count == 0;
    }

    /// <summary>
    /// The first path segment of a deliverable — the folder that becomes a mount root — or
    /// <see langword="null"/> when the value is not a rooted path.
    /// </summary>
    private static string? DeliverableRoot(string? deliverable)
    {
        if (deliverable is not { Length: > 1 } value || value[0] != '/')
            return null;

        var slash = value.IndexOf('/', 1);
        var root = slash > 1 ? value[..slash] : value;
        return root.Length > 1 ? root[1..] : null;
    }

    /// <summary>The structural rules a blueprint must satisfy before compilation.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        ValidateCrew(errors);
        ValidateEntities(errors, Agents, "agents", static a => a.Key, ValidateAgent);
        ValidateEntities(errors, Tasks, "tasks", static t => t.Key, ValidateTask);
        ValidateReferences(errors);

        return errors;
    }

    /// <summary>
    /// The by-key references of the plan, before compilation (GAP-33): the manager names an agent
    /// of a hierarchical or consensual crew; each task's agent and dependencies name an agent and a
    /// task of the plan. The YAML mapper refuses each of these by an exception, and
    /// <see cref="ForgeBlueprintCompiler.Compile"/> is called un-guarded by the render stage, the
    /// validate stage and an edited plan's check — so they are errors here, where
    /// <c>blueprint_submit</c> answers the model and the repair loop can act on them (the unknown
    /// <c>process</c> precedent, <see cref="ValidateCrew"/>).
    /// </summary>
    private void ValidateReferences(List<string> errors)
    {
        var agentKeys = KeysOf(Agents, static a => a.Key);
        var taskKeys = KeysOf(Tasks, static t => t.Key);

        if (!string.IsNullOrWhiteSpace(Manager))
        {
            var process = string.IsNullOrWhiteSpace(Crew?.Process) ? "sequential" : Crew.Process.Trim();
            if (IsKnownProcess(process) && !Orkeon.Domain.SharedKernel.ValueObjects.ProcessType.From(process).AcceptsManagerAgent)
            {
                errors.Add(
                    $"'manager' is '{Manager}', but 'crew.process' is '{process}', which has no manager: the agent would only be "
                    + "one more worker. Remove 'manager', or use 'hierarchical' (the manager assigns each task and reviews its "
                    + "output) or 'consensual' (it arbitrates the vote when it fails).");
            }

            if (!agentKeys.Contains(Manager))
                errors.Add($"'manager' names agent '{Manager}', which does not exist. Agents: {Listed(agentKeys)}.");
        }

        for (var i = 0; i < (Tasks?.Count ?? 0); i++)
        {
            var task = Tasks![i];
            if (!string.IsNullOrWhiteSpace(task.Agent) && !agentKeys.Contains(task.Agent))
                errors.Add($"tasks[{i}] ('{task.Key}'): 'agent' names '{task.Agent}', which does not exist. Agents: {Listed(agentKeys)}.");

            foreach (var dependency in task.Dependencies ?? [])
            {
                if (!taskKeys.Contains(dependency))
                    errors.Add($"tasks[{i}] ('{task.Key}'): depends on '{dependency}', which does not exist. Tasks: {Listed(taskKeys)}.");
            }
        }
    }

    /// <summary>The keys an entity list declares, in its order, blanks left out.</summary>
    private static List<string> KeysOf<T>(IReadOnlyList<T>? entities, Func<T, string?> keyOf) =>
        [.. (entities ?? []).Select(keyOf).Where(static key => !string.IsNullOrWhiteSpace(key)).Select(static key => key!).Distinct(StringComparer.Ordinal)];

    private static string Listed(List<string> keys) => keys.Count == 0 ? "none" : string.Join(", ", keys);

    /// <summary>Crew-level rules: a short display name, a goal, and a known orchestration mode.</summary>
    private void ValidateCrew(List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(Crew?.Name))
            errors.Add("'crew.name' is required.");
        else if (Crew.Name.Trim().Length > 60)
            errors.Add("'crew.name' must be a short display name (60 characters max), never the goal sentence.");
        if (string.IsNullOrWhiteSpace(Crew?.Goal))
            errors.Add("'crew.goal' is required.");

        // An unknown `process` is a recoverable LLM slip and has to be reported HERE, where
        // blueprint_submit answers the model and the stage's repair loop can act on it.
        // Unknown values are refused at compilation rather than silently becoming Sequential —
        // correct, but ForgeBlueprintCompiler.Compile is called un-guarded from the validate
        // stage, the render stage and ValidateEditedBlueprint, none of which turn a throw into
        // a validation error. So a blueprint carrying `process: pipeline` was accepted, the
        // interview and blueprint turns were paid for, and the session died at the CLI
        // boundary — and `forge resume` reloaded the same artifact and died at the same point.
        if (string.IsNullOrWhiteSpace(Crew?.Process) || IsKnownProcess(Crew.Process))
            return;

        errors.Add(
            $"'crew.process' is '{Crew.Process}', which is not an orchestration mode. Use one of: "
            + string.Join(", ", Orkeon.Domain.SharedKernel.ValueObjects.ProcessType.All.Select(p => p.Value).Order(StringComparer.Ordinal))
            + ".");
    }

    private static bool IsKnownProcess(string process) =>
        Orkeon.Domain.SharedKernel.ValueObjects.ProcessType.All.Any(
            p => string.Equals(p.Value, process.Trim(), StringComparison.OrdinalIgnoreCase));

    private static void ValidateAgent(List<string> errors, int index, ForgeBlueprintAgent agent)
    {
        if (string.IsNullOrWhiteSpace(agent.Role))
            errors.Add($"agents[{index}] ('{agent.Key}'): 'role' is required.");
        if (string.IsNullOrWhiteSpace(agent.Goal))
            errors.Add($"agents[{index}] ('{agent.Key}'): 'goal' is required.");
    }

    private static void ValidateTask(List<string> errors, int index, ForgeBlueprintTask task)
    {
        if (string.IsNullOrWhiteSpace(task.Description))
            errors.Add($"tasks[{index}] ('{task.Key}'): 'description' is required.");
        if (string.IsNullOrWhiteSpace(task.ExpectedOutput))
            errors.Add($"tasks[{index}] ('{task.Key}'): 'expectedOutput' is required.");
        if (string.IsNullOrWhiteSpace(task.Agent))
            errors.Add($"tasks[{index}] ('{task.Key}'): 'agent' is required.");

        // A deliverable's first segment becomes a virtual mount root that the promoted
        // team's launcher spells on its command line, and the mount grammar splits on ':'
        // outside quotes while the override list splits on ';'. A root carrying either
        // produced a run.sh that died at EVERY launch with a FormatException — after
        // ForgePromoter had already created the folder, so the team looked complete. The
        // blueprint is LLM-authored, and renaming a folder is exactly what a repair turn
        // is for.
        if (DeliverableRoot(task.Deliverable) is { } root
            && root.AsSpan().ContainsAny(':', ';'))
        {
            errors.Add(
                $"tasks[{index}] ('{task.Key}'): 'deliverable' starts with '{root}', and a "
                + "deliverable's first folder cannot contain ':' or ';' — those separate "
                + "the parts of a mount. Rename the folder.");
        }
    }

    private static void ValidateEntities<T>(
        List<string> errors,
        IReadOnlyList<T>? entities,
        string section,
        Func<T, string?> keyOf,
        Action<List<string>, int, T> validateOne)
    {
        if (entities is not { Count: > 0 })
        {
            errors.Add($"'{section}' must hold at least one entry.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entities.Count; i++)
        {
            var key = keyOf(entities[i]);
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"{section}[{i}]: 'key' is required."));
                continue;
            }

            if (!seen.Add(key))
                errors.Add($"{section}[{i}]: key '{key}' is used twice.");

            validateOne(errors, i, entities[i]);
        }
    }
}
