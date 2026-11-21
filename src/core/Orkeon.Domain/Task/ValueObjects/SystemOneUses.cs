namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// What a crew asks System One besides its decision tasks, for <see cref="DecisionTaskRules.CheckMode"/> (#429):
/// who lists <c>system_one_decide</c>, who names a decision profile, and who has the decision model pick its
/// skills or judge its read-tool results (#434).
/// </summary>
/// <param name="DecideToolHolders">Each agent or task that lists <c>system_one_decide</c>, as <c>agent 'x'</c> or <c>task 'y'</c>.</param>
/// <param name="DecisionProfiles">
/// Each agent or task that names a decision profile (a key of <c>Orkeon:Decision:Profiles</c>); null when the
/// caller cannot know the host's profiles (a loader before the host), which skips the rules that read them.
/// </param>
public sealed record SystemOneUses(IReadOnlyList<string> DecideToolHolders, IReadOnlyList<DecisionProfileUse>? DecisionProfiles)
{
    /// <summary>The name of the tool that asks System One.</summary>
    public const string DecideToolName = "system_one_decide";

    /// <summary>Gets a crew that asks nothing besides its decision tasks, its profiles unknown.</summary>
    public static SystemOneUses Unknown { get; } = new([], null);

    /// <summary>Gets each agent that declares <c>skills: auto</c>, as <c>agent 'x'</c>: the decision model picks its skills.</summary>
    public IReadOnlyList<string> SkillsAutoHolders { get; init; } = [];

    /// <summary>
    /// Gets each agent that declares <c>compaction: { prune: true }</c>, as <c>agent 'x'</c>: the decision model
    /// judges its older read-tool results.
    /// </summary>
    public IReadOnlyList<string> PruneHolders { get; init; } = [];
}

/// <summary>One agent or task naming a decision profile.</summary>
/// <param name="Holder">The agent or the task, as <c>agent 'x'</c> or <c>task 'y'</c>.</param>
/// <param name="Profile">The decision profile it names.</param>
public sealed record DecisionProfileUse(string Holder, string Profile);

/// <summary>What <see cref="DecisionTaskRules.CheckMode"/> finds: the refusals fail the load, the warnings do not.</summary>
/// <param name="Refusals">One message per fault that fails the load.</param>
/// <param name="Warnings">One message per finding the load reports and goes on.</param>
public sealed record DecisionModeFindings(IReadOnlyList<string> Refusals, IReadOnlyList<string> Warnings);
