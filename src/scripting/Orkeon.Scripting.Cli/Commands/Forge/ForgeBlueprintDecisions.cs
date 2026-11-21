using System.Collections.ObjectModel;
using System.Text.Json;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The decision side of a plan (#431): the rules the session's decision mode puts on it, and the translation of
/// its <c>decision</c> and <c>when</c> fields to the crew file's blocks and back. The engine's load rules
/// (<c>DecisionTaskRules</c>, run by the mapper) stay the judge: a plan that breaks one is refused with its message.
/// </summary>
internal static class ForgeBlueprintDecisions
{
    /// <summary>Adds to <paramref name="errors"/> what <paramref name="mode"/> refuses in <paramref name="blueprint"/>.</summary>
    public static void Validate(ForgeBlueprint blueprint, string mode, List<string> errors)
    {
        var tasks = blueprint.Tasks ?? [];
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            if (task.Decision is not null)
                ValidateDecisionTask(errors, i, task);
        }

        var decideHolders = DecideToolHolders(blueprint);

        switch (mode)
        {
            case ForgeCrewDecision.None:
                ValidateNone(errors, tasks, decideHolders);
                return;
            case ForgeCrewDecision.Required when !tasks.Any(static t => t.Decision is not null) && decideHolders.Count == 0:
                errors.Add(
                    "The crew's decision mode is 'required', and no task declares a 'decision' and no agent holds "
                    + $"'{ForgeCrewDecision.DecideTool}': give the plan at least one decision task, or the session was "
                    + "meant to be 'none'.");
                return;
        }

        if (errors.Count > 0)
            return;

        // The engine's own load rules of the mode (relays, fallbacks, processes, conditions): the mapper throws
        // on the first fault, which becomes an error the repair loop can act on.
        try
        {
            ForgeBlueprintCompiler.Compile(blueprint, crewDecision: mode);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            errors.Add(ex.Message);
        }
    }

    /// <summary>The crew file's <c>decision:</c> block of <paramref name="decision"/>.</summary>
    public static DecisionYamlConfig ToYaml(ForgeBlueprintDecision decision) => new()
    {
        Questions = decision.Questions?.ToDictionary(
            static pair => pair.Key,
            static pair => new QuestionYamlConfig
            {
                Choice = pair.Value.Choice is { } choice ? new Dictionary<string, string>(choice, StringComparer.Ordinal) : null,
                Score = pair.Value.Score is { } score ? new Collection<string>([.. score]) : null,
                Noul = pair.Value.Noul,
                Instructions = pair.Value.Instructions,
            },
            StringComparer.Ordinal),
        MinConfidence = decision.MinConfidence,
        State = decision.State is { Count: > 0 } state ? new Collection<string>([.. state]) : null,
        Fallback = decision.Fallback is { } fallback ? new Dictionary<string, string>(fallback, StringComparer.Ordinal) : null,
        Without = decision.Without is { } relay
            ? new DecisionRelayYamlConfig { Agent = relay.Agent, Instructions = relay.Instructions }
            : null,
    };

    /// <summary>The crew file's <c>when:</c> map of <paramref name="when"/>: a string, or a list of strings.</summary>
    public static Dictionary<string, object> ToYaml(IReadOnlyDictionary<string, JsonElement> when) =>
        when.ToDictionary(
            static pair => pair.Key,
            static pair => WhenValue(pair.Value),
            StringComparer.Ordinal);

    /// <summary>
    /// One value of a <c>when:</c> map as the crew file holds it: a string, or a list of strings, a JSON number or
    /// boolean written as its text (<c>[1]</c> names the option <c>"1"</c>). The script's <c>.when(...)</c> is rendered
    /// from this same value, so the two formats of one plan load alike.
    /// </summary>
    public static object WhenValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Array => value.EnumerateArray()
            .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText())
            .ToList(),
        _ => value.GetRawText(),
    };

    /// <summary>The plan's decision of a crew file's <c>decision:</c> block, for a reopen.</summary>
    public static ForgeBlueprintDecision FromYaml(DecisionYamlConfig decision) => new()
    {
        Questions = decision.Questions?.ToDictionary(
            static pair => pair.Key,
            static pair => new ForgeBlueprintQuestion
            {
                Choice = pair.Value.Choice,
                Score = pair.Value.Score is { } score ? [.. score] : null,
                Noul = pair.Value.Noul,
                Instructions = pair.Value.Instructions,
            },
            StringComparer.Ordinal),
        MinConfidence = decision.MinConfidence,
        State = decision.State is { Count: > 0 } state ? [.. state] : null,
        Fallback = decision.Fallback,
        Without = decision.Without is { } relay ? new ForgeBlueprintRelay { Agent = relay.Agent, Instructions = relay.Instructions } : null,
    };

    /// <summary>The plan's condition of a crew file's <c>when:</c> map, for a reopen.</summary>
    public static IReadOnlyDictionary<string, JsonElement> FromYaml(Dictionary<string, object> when) =>
        when.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value switch
            {
                string text => JsonSerializer.SerializeToElement(text),
                IEnumerable<object> list => JsonSerializer.SerializeToElement(list.Select(static o => o?.ToString()).ToList()),
                var other => JsonSerializer.SerializeToElement(other?.ToString()),
            },
            StringComparer.Ordinal);

    private static void ValidateDecisionTask(List<string> errors, int index, ForgeBlueprintTask task)
    {
        var name = task.Key ?? $"tasks[{index}]";
        if (!string.IsNullOrWhiteSpace(task.Agent))
            errors.Add($"tasks[{index}] ('{task.Key}'): {DecisionTaskRules.AgentAndDecisionMessage(name)}");
        if (!string.IsNullOrWhiteSpace(task.Deliverable))
            errors.Add($"tasks[{index}] ('{task.Key}'): Task '{name}' declares both a decision and a deliverable: a decision task runs no agent and writes no file, remove the deliverable.");
        if (task.Tools is { Count: > 0 })
            errors.Add($"tasks[{index}] ('{task.Key}'): Task '{name}' declares both a decision and tools: a decision task runs no agent and calls no tool, remove the tools.");
        if (task.Decision!.Questions is not { Count: > 0 } questions)
            errors.Add($"tasks[{index}] ('{task.Key}'): 'decision.questions' must name at least one question.");
        else
        {
            foreach (var (question, spec) in questions)
                ValidateQuestion(errors, $"tasks[{index}] ('{task.Key}'): question '{question}'", spec);
        }

        // The script refuses a null where the crew file reads a missing value: refused here, so both formats load alike.
        if (task.Decision.Fallback?.Any(static pair => pair.Value is null) ?? false)
            errors.Add($"tasks[{index}] ('{task.Key}'): 'decision.fallback' gives a question no answer: give each one an option, or leave it out.");
        if (task.Decision.State?.Any(static name => name is null) ?? false)
            errors.Add($"tasks[{index}] ('{task.Key}'): 'decision.state' holds a null: name each task by its key.");
    }

    /// <summary>
    /// One question of a decision: exactly one kind, every option described, every label named, and no
    /// <c>instructions</c> on a yes/no question, which reads none (its statement is all System One is told). Without
    /// these checks the loaders would drop a part without a word, or one format would refuse what the other accepts.
    /// </summary>
    private static void ValidateQuestion(List<string> errors, string where, ForgeBlueprintQuestion? spec)
    {
        var kinds = new[] { ("choice", spec?.Choice is not null), ("score", spec?.Score is not null), ("noul", spec?.Noul is not null) }
            .Where(static kind => kind.Item2)
            .Select(static kind => kind.Item1)
            .ToList();
        if (kinds.Count == 0)
        {
            errors.Add($"{where} declares no kind: give it exactly one of 'choice' (option to what it means), 'score' (labels) or 'noul' (a yes/no statement).");
            return;
        }

        if (kinds.Count > 1)
            errors.Add($"{where} declares more than one kind ({string.Join(", ", kinds)}): a question has exactly one.");
        foreach (var (option, description) in spec!.Choice ?? new Dictionary<string, string>())
        {
            if (description is null)
                errors.Add($"{where}: option '{option}' has no description: say what it means.");
        }

        if (spec.Score?.Any(static label => string.IsNullOrWhiteSpace(label)) ?? false)
            errors.Add($"{where}: 'score' holds an empty label: name each label.");
        if (spec.Noul is { } statement && string.IsNullOrWhiteSpace(statement))
            errors.Add($"{where}: 'noul' is empty: write the yes/no statement System One tests.");
        if (spec.Noul is not null && spec.Instructions is not null)
            errors.Add($"{where} is a yes/no question, which reads no 'instructions': put what System One must weigh in its 'noul' statement, and remove the instructions.");
    }

    private static void ValidateNone(List<string> errors, IReadOnlyList<ForgeBlueprintTask> tasks, IReadOnlyList<string> decideHolders)
    {
        const string Remedy = "the crew's decision mode is 'none' (--crew-decision), which asks System One nothing: remove it.";
        for (var i = 0; i < tasks.Count; i++)
        {
            if (tasks[i].Decision is not null)
                errors.Add($"tasks[{i}] ('{tasks[i].Key}'): declares a 'decision', and {Remedy}");
            if (tasks[i].When is not null)
                errors.Add($"tasks[{i}] ('{tasks[i].Key}'): declares a 'when', and {Remedy}");
        }

        foreach (var holder in decideHolders)
            errors.Add($"{holder}: holds '{ForgeCrewDecision.DecideTool}', and {Remedy}");
    }

    /// <summary>Which agents and tasks hold <c>system_one_decide</c>, as an error names them.</summary>
    private static List<string> DecideToolHolders(ForgeBlueprint blueprint)
    {
        var holders = new List<string>();
        var agents = blueprint.Agents ?? [];
        for (var i = 0; i < agents.Count; i++)
        {
            if (agents[i].Tools?.Contains(ForgeCrewDecision.DecideTool, StringComparer.Ordinal) ?? false)
                holders.Add($"agents[{i}] ('{agents[i].Key}')");
        }

        var tasks = blueprint.Tasks ?? [];
        for (var i = 0; i < tasks.Count; i++)
        {
            if (tasks[i].Tools?.Contains(ForgeCrewDecision.DecideTool, StringComparer.Ordinal) ?? false)
                holders.Add($"tasks[{i}] ('{tasks[i].Key}')");
        }

        return holders;
    }
}
