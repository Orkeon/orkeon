using System.Globalization;

namespace Orkeon.Domain.Agent;

/// <summary>
/// What an agent overrides of the host's conversation compaction (<c>compaction:</c>, #432): each
/// field left null follows <c>Orkeon:Compaction</c>.
/// </summary>
public sealed record AgentCompaction
{
    /// <summary>Whether the agent's loops compact their conversation; null follows <c>Orkeon:Compaction:Enabled</c>.</summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// The share of the usable window (the window less the call's output cap) at which the loop
    /// compacts, above 0 and at most 1; null follows <c>Orkeon:Compaction:TriggerShare</c>.
    /// </summary>
    public double? TriggerShare { get; init; }

    /// <summary>What is wrong with <paramref name="triggerShare"/> as a trigger share, or null when it is one.</summary>
    /// <param name="triggerShare">The share to judge; null is accepted (it follows the host).</param>
    /// <param name="where">Who declares it, for the message (<c>Agent 'writer' compaction.triggerShare</c>).</param>
    public static string? TriggerShareProblem(double? triggerShare, string where) =>
        triggerShare is null or (> 0 and <= 1)
            ? null
            : $"{where} is '{triggerShare.Value.ToString(CultureInfo.InvariantCulture)}', which is not a share of the window: " +
              "write a number above 0 and at most 1, such as 0.75.";
}
