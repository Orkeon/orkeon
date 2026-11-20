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
        var hasDecisionContent = false;
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            hasDecisionContent |= task.Decision is not null || task.When is not null;
            if (task.Decision is not null)
                ValidateDecisionTask(errors, i, task);
        }

        var decideHolders = DecideToolHolders(blueprint);
        hasDecisionContent |= decideHolders.Count > 0;

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

        if (errors.Count > 0 || !hasDecisionContent && mode == ForgeCrewDecision.Required)
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
            static pair => pair.Value.ValueKind switch
            {
                JsonValueKind.String => (object)pair.Value.GetString()!,
                JsonValueKind.Array => pair.Value.EnumerateArray()
                    .Select(static item => item.ValueKind == JsonValueKind.String ? (object)item.GetString()! : item.GetRawText())
                    .ToList(),
                _ => pair.Value.GetRawText(),
            },
            StringComparer.Ordinal);

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
        if (task.Decision!.Questions is not { Count: > 0 })
            errors.Add($"tasks[{index}] ('{task.Key}'): 'decision.questions' must name at least one question.");
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
