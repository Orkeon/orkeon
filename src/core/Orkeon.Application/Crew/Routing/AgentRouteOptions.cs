using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Crew.Routing;

/// <summary>
/// Where an agent on <c>llm.profile: auto</c> is routed, bound to the <c>Orkeon:Decision:Route</c> section
/// by <c>AddOrkeonInfrastructure()</c>. At the start of each task, the decision model picks one profile of
/// <see cref="Ladder"/>, and the task keeps it; under a probability of 0.6, without an answer or with the decision
/// circuit off, the host's default profile stands.
/// </summary>
public sealed class AgentRouteOptions
{
    /// <summary>The configuration section: <c>Orkeon:Decision:Route</c>.</summary>
    public const string SectionName = "Orkeon:Decision:Route";

    /// <summary>
    /// The profiles an <c>auto</c> agent is routed among, cheapest first: two or more names of <c>Llm:Profiles</c>
    /// or <c>Orkeon:Decision:Profiles</c> (<c>default</c> included), each once; a ladder of one rung, or with a
    /// blank, <c>auto</c> or repeated rung, is refused at start. Empty by default: a crew naming <c>auto</c> on a
    /// host without a ladder fails its load.
    /// </summary>
    public IList<string> Ladder { get; init; } = [];

    /// <summary>What is wrong with <paramref name="options"/>, empty when nothing is.</summary>
    /// <param name="options">The options to judge.</param>
    /// <returns>One message per problem, each naming the key to fix.</returns>
    public static IReadOnlyList<string> Problems(AgentRouteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Ladder.Count == 0)
            return [];

        var problems = new List<string>();
        var rungs = options.Ladder.Select(rung => rung?.Trim() ?? string.Empty).ToList();
        if (rungs.Count < 2)
            problems.Add($"{LlmProfiles.RouteLadderKey} names one profile, and a route needs two or more, cheapest first: add a profile or remove the ladder.");
        for (var i = 0; i < rungs.Count; i++)
        {
            if (rungs[i].Length == 0)
                problems.Add($"{LlmProfiles.RouteLadderKey}:{i} is blank: write a profile name.");
            else if (LlmProfiles.IsAuto(rungs[i]))
                problems.Add($"{LlmProfiles.RouteLadderKey}:{i} is '{LlmProfiles.Auto}', the name of the route itself: write a profile name.");
        }

        problems.AddRange(rungs.Where(rung => rung.Length > 0)
            .GroupBy(rung => LlmProfiles.IsDefault(rung) ? LlmProfiles.Default : rung, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{LlmProfiles.RouteLadderKey} names the profile '{group.Key}' more than once: each rung is a different profile."));
        return problems;
    }
}
