using System.Collections.Immutable;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.EventHub;

/// <summary>Which way a <see cref="CrewLink"/> lets messages travel (spec §10.3).</summary>
public enum CrewLinkDirection
{
    /// <summary>The declaring crew may send to the target, not the other way around.</summary>
    Outbound,

    /// <summary>The target may send to the declaring crew, not the other way around.</summary>
    Inbound,

    /// <summary>Both directions.</summary>
    Bidirectional
}

/// <summary>
/// Declarative authorization between two participants of the hub (spec §10.2). It lives in the
/// Domain because it is what a crew *declares* — the <c>links:</c> block of its YAML — rather
/// than a transport detail; matching it against a mailbox address is the Application's job
/// (<c>CrewLinkMailboxExtensions</c>).
/// <para>
/// Checked on the **sender** side, at <c>Publish</c> (scoped), <c>Post</c> and <c>Send</c>
/// time — a global <c>Publish</c> with no target stays free, and subscribers filter on their
/// own side.
/// </para>
/// <para>
/// The YAML shape is <c>links: [{ to, direction, allowed_topics }]</c> under a crew.
/// </para>
/// </summary>
public sealed record CrewLink
{
    /// <summary>
    /// Who the link points at. A crew id, or the reserved form <c>client:{name}</c> for an
    /// external peer — that peer is not a crew, and the ACL has to be able to name it
    /// without pretending otherwise (RC2 R3).
    /// </summary>
    public required string To { get; init; }

    /// <summary>Which way messages may travel.</summary>
    public CrewLinkDirection Direction { get; init; } = CrewLinkDirection.Outbound;

    /// <summary>
    /// Topics the link authorizes. **Empty means every topic**: a link declared without a
    /// topic list is a decision to trust the peer broadly, not an accident — an empty list
    /// that authorized nothing would make the link pointless.
    /// </summary>
    public ImmutableArray<string> AllowedTopics { get; init; } = [];

    /// <summary>Prefix marking a link that points at an external peer rather than a crew.</summary>
    public const string ClientPrefix = "client:";

    /// <summary>Builds the <see cref="To"/> value naming an external peer.</summary>
    public static string ForClient(string name) => ClientPrefix + name;

    /// <summary>Whether this link points at an external peer.</summary>
    public bool TargetsClient => To.StartsWith(ClientPrefix, StringComparison.Ordinal);

    /// <summary>The external peer's name, when <see cref="TargetsClient"/>.</summary>
    public string? ClientName => TargetsClient ? To[ClientPrefix.Length..] : null;

    /// <summary>Whether the link names <paramref name="topic"/> (or authorizes every topic).</summary>
    public bool Authorizes(string topic) =>
        AllowedTopics.IsDefaultOrEmpty || AllowedTopics.Contains(topic, StringComparer.Ordinal);

    /// <summary>Whether the link lets the declaring side send outwards.</summary>
    public bool AllowsOutbound =>
        Direction is CrewLinkDirection.Outbound or CrewLinkDirection.Bidirectional;

    /// <summary>Whether the link matches a crew named directly (the scoped-publish case).</summary>
    public bool MatchesCrew(CrewId target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return !TargetsClient && string.Equals(To, target.ToString(), StringComparison.Ordinal);
    }
}
