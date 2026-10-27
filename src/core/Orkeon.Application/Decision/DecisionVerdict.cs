namespace Orkeon.Application.Decision;

/// <summary>
/// The verdict of an <see cref="Interfaces.Ports.IDecisionStage"/>: what happens to the call,
/// the profile it goes to when it is routed or escalated, and why.
/// </summary>
public sealed record DecisionVerdict
{
    /// <summary>The call goes on unchanged.</summary>
    public static DecisionVerdict Continue { get; } = new() { Kind = DecisionVerdictKind.Continue };

    /// <summary>What happens to the call.</summary>
    public required DecisionVerdictKind Kind { get; init; }

    /// <summary>The profile a routed or escalated call goes to; null otherwise.</summary>
    public string? Profile { get; init; }

    /// <summary>Why, for the log, the audit and a blocked call's error.</summary>
    public string? Reason { get; init; }

    /// <summary>Refuses the call.</summary>
    /// <param name="reason">Why; it reaches the caller.</param>
    /// <returns>The verdict.</returns>
    public static DecisionVerdict Block(string reason) =>
        new() { Kind = DecisionVerdictKind.Block, Reason = reason };

    /// <summary>Sends the conversation to <paramref name="profile"/> instead of the target.</summary>
    /// <param name="profile">The profile name.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The verdict.</returns>
    public static DecisionVerdict Route(string profile, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        return new() { Kind = DecisionVerdictKind.Route, Profile = profile, Reason = reason };
    }

    /// <summary>Sends the conversation again, once, to <paramref name="profile"/>.</summary>
    /// <param name="profile">The profile name.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The verdict.</returns>
    public static DecisionVerdict Escalate(string profile, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        return new() { Kind = DecisionVerdictKind.Escalate, Profile = profile, Reason = reason };
    }
}
