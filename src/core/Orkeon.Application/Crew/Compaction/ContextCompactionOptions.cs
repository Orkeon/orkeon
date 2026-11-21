using System.Globalization;

namespace Orkeon.Application.Crew.Compaction;

/// <summary>
/// How the agent loops keep a long conversation inside the model's window, bound to the
/// <see cref="SectionName"/> section by <c>AddOrkeonInfrastructure()</c>. Before each model call the
/// native and the chat-client loops estimate the prompt in tokens; past <see cref="TriggerShare"/> of
/// the usable window (the window less the output reserve) they replace the middle of the
/// conversation with one summary, keeping the system prompt, the task and the
/// <see cref="KeepRecentTurns"/> most recent turns. The text loop keeps its own character trim.
/// </summary>
public sealed class ContextCompactionOptions
{
    /// <summary>The configuration section: <c>Orkeon:Compaction</c>.</summary>
    public const string SectionName = "Orkeon:Compaction";

    /// <summary>The trigger share by default: 0.75.</summary>
    public const double DefaultTriggerShare = 0.75;

    /// <summary>The recent turns kept verbatim by default: 6.</summary>
    public const int DefaultKeepRecentTurns = 6;

    /// <summary>
    /// Whether the loops compact their conversation. True by default: a long loop pays one summary
    /// call instead of losing its middle to the 40-message trim. False brings that trim back, as an
    /// agent's <c>compaction: { enabled: false }</c> does for that agent alone.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The share of the usable window (the context window less the output reserve: the call's cap, else
    /// the model's documented maximum, else 4096, at most half the window) the estimated prompt reaches
    /// for the loop to compact, above 0 and at most 1. 0.75 by default; an agent's
    /// <c>compaction: { triggerShare }</c> overrides it.
    /// </summary>
    public double TriggerShare { get; set; } = DefaultTriggerShare;

    /// <summary>
    /// How many of the most recent turns a compaction keeps verbatim, a turn being one assistant
    /// response, in one message or several, with its tool results (a tool call is never parted from its
    /// result). Fewer are kept when they are all the conversation holds or alone pass the trigger. 6 by
    /// default; 0 summarises every turn.
    /// </summary>
    public int KeepRecentTurns { get; set; } = DefaultKeepRecentTurns;

    /// <summary>
    /// The LLM profile that writes the summary: <c>default</c>, a profile of <c>Llm:Profiles</c> or a
    /// decision profile of <c>Orkeon:Decision:Profiles</c>; a name the host does not offer (one neither
    /// section defines, or one <c>Orkeon:Host:LlmProfiles</c> leaves out), or <c>auto</c>, refuses the start.
    /// Left out, the agent's own model and profile. The summary's tokens are charged to the task either way.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>What is wrong with <paramref name="options"/>, empty when nothing is.</summary>
    /// <param name="options">The options to judge.</param>
    public static IReadOnlyList<string> Problems(ContextCompactionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        if (Domain.Agent.AgentCompaction.TriggerShareProblem(options.TriggerShare, $"{SectionName}:TriggerShare") is { } share)
            problems.Add(share);
        if (options.KeepRecentTurns < 0)
        {
            problems.Add($"{SectionName}:KeepRecentTurns is '{options.KeepRecentTurns.ToString(CultureInfo.InvariantCulture)}', " +
                $"which is not a number of turns: write 0 or more, such as {DefaultKeepRecentTurns.ToString(CultureInfo.InvariantCulture)}.");
        }
        return problems;
    }
}
