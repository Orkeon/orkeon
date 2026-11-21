using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Crew.Routing;

/// <summary>
/// The thinking levels System One picks among for an agent or a task on <c>thinking: { effort: auto }</c> (#439),
/// bound to the <c>Orkeon:Decision:Effort</c> section by <c>AddOrkeonInfrastructure()</c>. At the start of each task,
/// the decision model picks one of <see cref="Levels"/>, and the task keeps it; under a probability of 0.6, without
/// an answer or with the decision circuit off, the effort the agent would have had without <c>auto</c> stands.
/// </summary>
public sealed class AgentEffortOptions
{
    /// <summary>The configuration section: <c>Orkeon:Decision:Effort</c>.</summary>
    public const string SectionName = "Orkeon:Decision:Effort";

    /// <summary>The key of the levels, as a message names it.</summary>
    public const string LevelsKey = SectionName + ":Levels";

    /// <summary>The levels a host allows when it lists none: <c>low</c>, <c>medium</c>, <c>high</c>.</summary>
    public static IReadOnlyList<string> DefaultLevels => LlmThinkingConfig.ThreeLevels;

    /// <summary>
    /// The thinking levels System One may pick for an <c>effort: auto</c> agent or task, cheapest first: each one of
    /// <c>low</c>, <c>medium</c>, <c>high</c>, <c>xhigh</c>, <c>max</c>, in lower case, each once, in that order; a
    /// blank, unknown or repeated level, and a list that does not rise strictly in that order, are refused at start.
    /// Empty by default, which allows <c>low</c>, <c>medium</c> and <c>high</c>.
    /// </summary>
    public IList<string> Levels { get; init; } = [];

    /// <summary>The levels the host allows: <see cref="Levels"/> trimmed, else <see cref="DefaultLevels"/>.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The allowed levels, cheapest first.</returns>
    public static IReadOnlyList<string> AllowedLevels(AgentEffortOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Levels.Count == 0 ? DefaultLevels : [.. options.Levels.Select(level => level?.Trim() ?? string.Empty)];
    }

    /// <summary>What is wrong with <paramref name="options"/>, empty when nothing is.</summary>
    /// <param name="options">The options to judge.</param>
    /// <returns>One message per problem, each naming the key to fix.</returns>
    public static IReadOnlyList<string> Problems(AgentEffortOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        var levels = options.Levels.Select(level => level?.Trim() ?? string.Empty).ToList();
        for (var i = 0; i < levels.Count; i++)
        {
            if (levels[i].Length == 0)
                problems.Add($"{LevelsKey}:{i} is blank: write a level ({string.Join(", ", LlmThinkingConfig.Levels)}).");
            else if (LlmAutoEffort.IsAuto(levels[i]))
                problems.Add($"{LevelsKey}:{i} is '{LlmAutoEffort.Name}', the name of the pick itself: write a level ({string.Join(", ", LlmThinkingConfig.Levels)}).");
            else if (!LlmThinkingConfig.Levels.Contains(levels[i], StringComparer.Ordinal))
                problems.Add($"{LevelsKey}:{i} is '{levels[i]}', which is not a thinking level: write one of {string.Join(", ", LlmThinkingConfig.Levels)} (lower case).");
        }

        problems.AddRange(levels.Where(level => level.Length > 0)
            .GroupBy(level => level, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{LevelsKey} names the level '{group.Key}' more than once: each level is listed once."));

        // A raise steps to the next level of the list and the decision model reads it cheapest first, so a
        // list out of rank order would lower the effort it claims to raise (#439).
        var known = levels.Where(level => LlmThinkingConfig.RankOf(level) >= 0).ToList();
        for (var i = 1; i < known.Count; i++)
        {
            if (LlmThinkingConfig.RankOf(known[i]) >= LlmThinkingConfig.RankOf(known[i - 1]))
                continue;
            problems.Add($"{LevelsKey} lists '{known[i]}' after '{known[i - 1]}': list the levels cheapest first, in the order {string.Join(", ", LlmThinkingConfig.Levels)}.");
            break;
        }

        return problems;
    }
}
