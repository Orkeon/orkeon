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
/// §7.3). Structural rules live here; referential rules split by necessity: what survives
/// the mapping (cycles, empties, manager id) belongs to the shared
/// <c>CrewDefinitionValidator</c>, while by-key references are checked pre-mapping in
/// <c>ForgeBlueprintCompiler.Validate</c> — the mapper erases an unresolvable name, so the
/// shared validator can no longer see it.
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

    /// <summary>Key of the manager agent, hierarchical crews only.</summary>
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

    /// <summary>The structural rules a blueprint must satisfy before compilation.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

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
        if (!string.IsNullOrWhiteSpace(Crew?.Process)
            && !Orkeon.Domain.SharedKernel.ValueObjects.ProcessType.All.Any(
                p => string.Equals(p.Value, Crew.Process.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(
                $"'crew.process' is '{Crew.Process}', which is not an orchestration mode. Use one of: "
                + string.Join(", ", Orkeon.Domain.SharedKernel.ValueObjects.ProcessType.All.Select(p => p.Value).Order(StringComparer.Ordinal))
                + ".");
        }

        ValidateEntities(errors, Agents, "agents", static a => a.Key, (list, i, agent) =>
        {
            if (string.IsNullOrWhiteSpace(agent.Role))
                list.Add($"agents[{i}] ('{agent.Key}'): 'role' is required.");
            if (string.IsNullOrWhiteSpace(agent.Goal))
                list.Add($"agents[{i}] ('{agent.Key}'): 'goal' is required.");
        });

        ValidateEntities(errors, Tasks, "tasks", static t => t.Key, (list, i, task) =>
        {
            if (string.IsNullOrWhiteSpace(task.Description))
                list.Add($"tasks[{i}] ('{task.Key}'): 'description' is required.");
            if (string.IsNullOrWhiteSpace(task.ExpectedOutput))
                list.Add($"tasks[{i}] ('{task.Key}'): 'expectedOutput' is required.");
            if (string.IsNullOrWhiteSpace(task.Agent))
                list.Add($"tasks[{i}] ('{task.Key}'): 'agent' is required.");
        });

        return errors;
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
