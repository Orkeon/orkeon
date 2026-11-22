using System.Globalization;

namespace Orkeon.Application.Crew.Compaction;

/// <summary>
/// How the agent loops prune their older read-tool results before they compact, bound to the
/// <see cref="SectionName"/> section by <c>AddOrkeonInfrastructure()</c>. Once the estimated prompt reaches
/// <see cref="FillShare"/> of the usable window (or the compaction trigger, whichever comes first), the decision
/// model judges each result of a read tool older than the <see cref="KeepRecentMessages"/> last messages: keep it,
/// keep its first <see cref="HeadChars"/> characters, or drop it. It sees the call (tool, input, size, age), never
/// the result. With the decision circuit off, nothing is pruned. A crew loop prunes where it compacts: an agent that
/// does not compact does not prune. A script's <c>ctx.llm.act</c> does not compact, and prunes only when the call
/// asks for it (<c>prune</c>, #461), whatever <see cref="Enabled"/> says.
/// </summary>
public sealed class ToolResultPruningOptions
{
    /// <summary>The configuration section: <c>Orkeon:Compaction:Prune</c>.</summary>
    public const string SectionName = "Orkeon:Compaction:Prune";

    /// <summary>The fill share by default: 0.4, as in Orkeon Code.</summary>
    public const double DefaultFillShare = 0.4;

    /// <summary>The last messages never offered by default: 6. A result of the turn the model has not answered yet is never offered, whatever its place.</summary>
    public const int DefaultKeepRecentMessages = 6;

    /// <summary>What a <c>head</c> verdict keeps by default: 300 characters, enough to recognise a file or a listing.</summary>
    public const int DefaultHeadChars = 300;

    /// <summary>
    /// Whether the loops prune. False by default: each judgement is a request to the decision model. An agent's
    /// <c>compaction: { prune }</c> overrides it.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The share of the usable window (the context window less the output reserve: the call's cap, else the model's
    /// documented maximum, else 4096, at most half the window) the estimated prompt reaches for the results to be judged, above 0 and at most 1. 0.4 by default, under the compaction trigger, so a loop
    /// prunes before it compacts.
    /// </summary>
    public double FillShare { get; set; } = DefaultFillShare;

    /// <summary>How many of the last messages have their results never offered. 6 by default.</summary>
    public int KeepRecentMessages { get; set; } = DefaultKeepRecentMessages;

    /// <summary>
    /// The characters a <c>head</c> verdict keeps of a result, 1 or more. 300 by default; a result of twice that
    /// or less is never offered, since cutting it frees nothing.
    /// </summary>
    public int HeadChars { get; set; } = DefaultHeadChars;

    /// <summary>What is wrong with <paramref name="options"/>, empty when nothing is.</summary>
    /// <param name="options">The options to judge.</param>
    /// <returns>One message per problem, each naming the key to fix.</returns>
    public static IReadOnlyList<string> Problems(ToolResultPruningOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        if (options.FillShare is not (> 0 and <= 1))
        {
            problems.Add($"{SectionName}:FillShare is '{options.FillShare.ToString(CultureInfo.InvariantCulture)}', which is not a share of the window: " +
                $"write a number above 0 and at most 1, such as {DefaultFillShare.ToString(CultureInfo.InvariantCulture)}.");
        }
        if (options.KeepRecentMessages < 0)
        {
            problems.Add($"{SectionName}:KeepRecentMessages is '{options.KeepRecentMessages.ToString(CultureInfo.InvariantCulture)}', " +
                $"which is not a number of messages: write 0 or more, such as {DefaultKeepRecentMessages.ToString(CultureInfo.InvariantCulture)}.");
        }
        if (options.HeadChars < 1)
        {
            problems.Add($"{SectionName}:HeadChars is '{options.HeadChars.ToString(CultureInfo.InvariantCulture)}', " +
                $"which keeps nothing: write 1 or more, such as {DefaultHeadChars.ToString(CultureInfo.InvariantCulture)}.");
        }
        return problems;
    }
}
