using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Task;

/// <summary>Crew-level rules of decision tasks.</summary>
public static class DecisionTaskRules
{
    /// <summary>The refusal of a task that names an agent and declares a decision.</summary>
    public static string AgentAndDecisionMessage(string taskName) =>
        $"Task '{taskName}' declares both a decision and an agent: a decision task runs no agent, remove one of decision and agent.";

    /// <summary>The refusal of a decision or a conditioned task in a process that does not run them.</summary>
    public static string UnsupportedProcessMessage(string taskName, ProcessType process) =>
        $"Task '{taskName}' declares a decision or a condition, which the {process} process does not support.";

    /// <summary>
    /// The refusal of a crew whose <c>decision.mode</c> breaks a rule of <see cref="CheckMode"/>: one spelling for
    /// every loader (YAML, <c>.ork.ts</c>, the crew factory).
    /// </summary>
    /// <param name="refusals">The refusals <see cref="CheckMode"/> found.</param>
    public static string ModeRefusal(IEnumerable<string> refusals) =>
        "Invalid decision.mode declaration: " + string.Join(" ", refusals);

    /// <summary>Checks the declarations, one message per fault.</summary>
    public static IReadOnlyList<string> Check(ProcessType process, IReadOnlyList<DecisionTaskDeclaration> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var messages = new List<string>();
        var supported = process.RunsDecisionTasks();
        var otherwiseSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var task in tasks)
        {
            if (task.Decision is not null && task.HasAgent)
                messages.Add(AgentAndDecisionMessage(task.Name));

            if (!supported && (task.Decision is not null || task.Condition is not null))
                messages.Add(UnsupportedProcessMessage(task.Name, process));

            if (task.Condition is { } condition)
                CheckCondition(task.Name, condition, tasks, otherwiseSeen, messages);
        }

        return messages;
    }

    /// <summary>Lists the decision tasks that have no fallback.</summary>
    public static IReadOnlyList<string> CheckFallbacks(IReadOnlyList<DecisionTaskDeclaration> tasks) =>
        CheckFallbacks(CrewDecisionMode.Unspecified, tasks);

    /// <summary>
    /// Lists the decision tasks that would have nothing to answer with the decision circuit off: neither a
    /// fallback, nor a <c>without:</c> relay, nor an otherwise task. A <see cref="CrewDecisionMode.Required"/>
    /// crew is never run with the circuit off, so the rule does not apply to it (#429).
    /// </summary>
    public static IReadOnlyList<string> CheckFallbacks(CrewDecisionMode mode, IReadOnlyList<DecisionTaskDeclaration> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (mode == CrewDecisionMode.Required)
            return [];
        return tasks
            .Where(t => t.Decision is { HasExplicitFallback: false, Without: null }
                && !tasks.Any(o => o.Condition is { IsOtherwise: true } c && c.DecisionTask == t.Name))
            .Select(t => $"Decision task '{t.Name}' has neither a fallback nor an otherwise task.")
            .ToList();
    }

    /// <summary>
    /// The rules of the crew's <c>decision.mode</c> (#429), one message per finding: what fails the load, and
    /// what the load reports and goes on with. A crew with no mode (<see cref="CrewDecisionMode.Unspecified"/>)
    /// keeps the rules it had before the key existed, and may not declare a <c>without:</c> relay.
    /// </summary>
    /// <param name="mode">The crew's mode.</param>
    /// <param name="process">The crew's process.</param>
    /// <param name="tasks">What each task declares about decisions.</param>
    /// <param name="uses">What else the crew asks System One.</param>
    /// <param name="agentKeys">The keys of the crew's agents, which a <c>without.agent</c> names; null when unknown.</param>
    /// <returns>The refusals and the warnings.</returns>
    public static DecisionModeFindings CheckMode(
        CrewDecisionMode mode,
        ProcessType process,
        IReadOnlyList<DecisionTaskDeclaration> tasks,
        SystemOneUses uses,
        IReadOnlyCollection<string>? agentKeys = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(uses);
        var refusals = new List<string>();
        var warnings = new List<string>();
        var decisions = tasks.Where(t => t.Decision is not null).ToList();
        var profiles = uses.DecisionProfiles ?? [];

        switch (mode)
        {
            case CrewDecisionMode.None:
                foreach (var task in decisions)
                {
                    refusals.Add($"Task '{task.Name}' is a decision task, which a crew with decision.mode: none refuses.");
                    if (task.Decision!.Without is not null)
                        refusals.Add($"Task '{task.Name}' declares a without: relay, which a crew with decision.mode: none refuses.");
                }

                refusals.AddRange(uses.DecideToolHolders.Select(h =>
                    $"{Capitalized(h)} lists {SystemOneUses.DecideToolName}, which a crew with decision.mode: none refuses."));
                refusals.AddRange(profiles.Select(p => string.Equals(p.Profile, Orkeon.Constants.Llm.LlmProfileNames.Auto, StringComparison.OrdinalIgnoreCase)
                    ? $"{Capitalized(p.Holder)} names the profile '{p.Profile}', which System One picks and a crew with decision.mode: none refuses."
                    : $"{Capitalized(p.Holder)} names the decision profile '{p.Profile}', which a crew with decision.mode: none refuses."));
                refusals.AddRange(uses.SkillsAutoHolders.Select(h =>
                    $"{Capitalized(h)} declares skills: {{ auto: true }}, which System One picks and a crew with decision.mode: none refuses: list its skills instead."));
                refusals.AddRange(uses.PruneHolders.Select(h =>
                    $"{Capitalized(h)} declares compaction: {{ prune: true }}, which the decision model judges and a crew with decision.mode: none refuses."));
                break;

            case CrewDecisionMode.Optional:
                if (!process.RunsDecisionTasks())
                    refusals.Add($"A crew with decision.mode: optional needs a decision task, which the {process} process does not support.");
                else if (decisions.Count == 0)
                    refusals.Add("A crew with decision.mode: optional declares at least one decision task, and this one has none.");

                foreach (var task in decisions)
                {
                    if (task.Decision!.Without is null)
                        refusals.Add($"Decision task '{task.Name}' has no without: relay, which a crew with decision.mode: optional requires on every decision task.");
                    if (task.Decision.HasExplicitFallback)
                        refusals.Add($"Decision task '{task.Name}' declares a fallback, which a crew with decision.mode: optional never uses (the model answers with the circuit on, the relay with it off): remove it.");
                }

                refusals.AddRange(uses.DecideToolHolders.Select(h =>
                    $"{Capitalized(h)} lists {SystemOneUses.DecideToolName}, which a crew with decision.mode: optional refuses: a run without System One must not call it."));
                break;

            case CrewDecisionMode.Required:
                foreach (var task in decisions)
                {
                    if (task.Decision!.HasExplicitFallback)
                        warnings.Add($"Decision task '{task.Name}' declares a fallback, which a crew with decision.mode: required never uses.");
                    if (task.Decision.Without is not null)
                        warnings.Add($"Decision task '{task.Name}' declares a without: relay, which a crew with decision.mode: required never uses.");
                }

                if (uses.DecisionProfiles is not null && decisions.Count == 0 && uses.DecideToolHolders.Count == 0 && profiles.Count == 0
                    && uses.SkillsAutoHolders.Count == 0 && uses.PruneHolders.Count == 0)
                    warnings.Add("The crew requires System One but asks it nothing: it has no decision task, no system_one_decide, no decision profile, " +
                        "no skills: { auto: true } and no compaction: { prune: true }.");
                break;

            default:
                refusals.AddRange(decisions.Where(t => t.Decision!.Without is not null).Select(t =>
                    $"Task '{t.Name}' declares a without: relay, which needs decision.mode: optional on the crew."));
                break;
        }

        if (agentKeys is not null && mode is CrewDecisionMode.Optional or CrewDecisionMode.Required)
        {
            refusals.AddRange(decisions
                .Where(t => t.Decision!.Without is { } relay && !agentKeys.Contains(relay.Agent, StringComparer.Ordinal))
                .Select(t => $"Task '{t.Name}' without.agent '{t.Decision!.Without!.Agent}' is not an agent of the crew; " +
                    $"its agents: {(agentKeys.Count == 0 ? "none" : string.Join(", ", agentKeys.Order(StringComparer.Ordinal)))}."));
        }

        return new DecisionModeFindings(refusals, warnings);
    }

    private static string Capitalized(string holder) =>
        holder.Length == 0 ? holder : char.ToUpperInvariant(holder[0]) + holder[1..];

    private static bool RunsDecisionTasks(this ProcessType process) =>
        process == ProcessType.Sequential || process == ProcessType.Parallel || process == ProcessType.Graph;

    private static void CheckCondition(
        string name,
        TaskCondition condition,
        IReadOnlyList<DecisionTaskDeclaration> tasks,
        HashSet<string> otherwiseSeen,
        List<string> messages)
    {
        var target = tasks.FirstOrDefault(t => t.Name == condition.DecisionTask);
        if (target?.Decision is not { } decision)
        {
            messages.Add($"Task '{name}' has a condition on '{condition.DecisionTask}', which is not a decision task.");
            return;
        }

        if (condition.IsOtherwise)
        {
            if (!otherwiseSeen.Add(condition.DecisionTask))
                messages.Add($"Task '{name}' duplicates the otherwise task of '{condition.DecisionTask}'.");
            else if (!tasks.Any(t => t.Condition is { IsOtherwise: false } c && c.DecisionTask == condition.DecisionTask))
                messages.Add($"Task '{name}' is an otherwise task of '{condition.DecisionTask}' with no sibling condition.");
            return;
        }

        foreach (var clause in condition.Clauses)
        {
            var question = decision.Question(clause.Question);
            if (question is null)
            {
                messages.Add($"Task '{name}' names the unknown question '{clause.Question}' of '{condition.DecisionTask}'.");
                continue;
            }

            var named = clause.Options.Concat(clause.Option is null ? [] : [clause.Option]);
            if (question is ChoiceQuestionSpec choice && named.FirstOrDefault(o => !choice.Options.ContainsKey(o)) is { } unknown)
                messages.Add($"Task '{name}' names the unknown option '{unknown}' of question '{clause.Question}'.");
        }
    }
}
