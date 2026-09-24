namespace Orkeon.Domain.FileSystem;

/// <summary>
/// A forge session as <see cref="TeamSessionLink"/> reads it: the id it was created with and
/// the folder it last promoted to — or was rebuilt from.
/// </summary>
/// <param name="Id">The session's stable id, the <c>id</c> of its <c>session.json</c>.</param>
/// <param name="PromotedTo">Its <c>promotedTo</c>; null when it names no folder.</param>
public sealed record SessionPromotion(Guid Id, string? PromotedTo);

/// <summary>What rule R says about one team folder (<see cref="TeamSessionLink.Resolve"/>).</summary>
public enum TeamSessionLinkKind
{
    /// <summary>The folder records no session id — no <c>forge.json</c>, or one without an id. Not linked.</summary>
    NoIdentity,

    /// <summary>No session carries the folder's id: it was deleted, or the team was forged elsewhere. Not linked.</summary>
    NoSession,

    /// <summary>
    /// Case 2: the session names another folder, which exists and carries the same id — that
    /// one is the original, this one a copy of it. Not linked.
    /// </summary>
    Copy,

    /// <summary>Case 1: the session's <c>promotedTo</c> designates this very folder. Linked.</summary>
    Linked,

    /// <summary>
    /// Case 3: the session names no folder, or one that no longer carries its id — this folder
    /// is the original, moved or renamed. Linked; the session's <c>promotedTo</c> is to be
    /// pointed at it by the next write.
    /// </summary>
    Moved,
}

/// <summary>
/// Rule R (STUDIO-25): the one rule that decides whether a team folder is linked to a forge
/// session, written once for the CLI and Studio. The link used to be an absolute path alone —
/// the session's <c>promotedTo</c> — which a moved team lost and a copied one could not
/// disprove; the two implementations of the lookup disagreed on top of it (directory order in
/// the CLI, newest first in Studio). Now a session carries a stable id, its promotion copies
/// the id into the team's <c>forge.json</c>, and the path only arbitrates between the folders
/// that carry the same id.
/// <para>
/// Let X be the id the team folder T carries and S the session whose id is X:
/// </para>
/// <list type="number">
/// <item><description>S's <c>promotedTo</c> designates T: linked.</description></item>
/// <item><description>It designates another existing folder that carries X too: T is a copy, not linked.</description></item>
/// <item><description>Otherwise — no <c>promotedTo</c>, or a folder gone or holding another team: T is the original, moved; linked.</description></item>
/// </list>
/// <para>
/// A folder without an id, or with one no session carries, is linked to nothing: there is no
/// fallback on the path (D-06).
/// </para>
/// <para>
/// Pure: the caller reads the files — T's id, the session's document — and passes a reader for
/// the id another folder carries, consulted only in the one case that needs it. Paths compare
/// full-path, trailing-separator-blind, case-blind on Windows.
/// </para>
/// </summary>
public static class TeamSessionLink
{
    /// <summary>Rule R's verdict on <paramref name="teamDirectory"/>.</summary>
    /// <param name="teamDirectory">The team folder T.</param>
    /// <param name="teamSessionId">
    /// The id T's <c>forge.json</c> carries; null when T has no <c>forge.json</c>, or one without a
    /// readable id. <see cref="Guid.Empty"/> is no id.
    /// </param>
    /// <param name="session">
    /// The session whose id is <paramref name="teamSessionId"/>, as the caller found it; null when
    /// none carries it. A session carrying another id is not T's session.
    /// </param>
    /// <param name="recordedSessionIdAt">
    /// Reads the id a folder's <c>forge.json</c> carries; null when the folder, its record or the id
    /// is absent or unreadable. Must not throw.
    /// </param>
    public static TeamSessionLinkKind Resolve(
        string teamDirectory,
        Guid? teamSessionId,
        SessionPromotion? session,
        Func<string, Guid?> recordedSessionIdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(recordedSessionIdAt);

        if (teamSessionId is not { } id || id == Guid.Empty)
            return TeamSessionLinkKind.NoIdentity;

        if (session is null || session.Id != id)
            return TeamSessionLinkKind.NoSession;

        if (IsSameDirectory(session.PromotedTo, teamDirectory))
            return TeamSessionLinkKind.Linked;

        // A folder that no longer exists carries no forge.json, so one read answers both halves
        // of case 2: the other folder is there, and it is the same team.
        return session.PromotedTo is { Length: > 0 } elsewhere && recordedSessionIdAt(elsewhere) == id
            ? TeamSessionLinkKind.Copy
            : TeamSessionLinkKind.Moved;
    }

    /// <summary>Whether <paramref name="kind"/> links the folder to the session: <see cref="TeamSessionLinkKind.Linked"/> or <see cref="TeamSessionLinkKind.Moved"/>.</summary>
    public static bool IsLinked(TeamSessionLinkKind kind) =>
        kind is TeamSessionLinkKind.Linked or TeamSessionLinkKind.Moved;

    /// <summary>
    /// Whether <paramref name="path"/> names <paramref name="directory"/>. A <c>promotedTo</c> is
    /// data read off a disk: one that is no path at all names no folder, and never throws.
    /// </summary>
    private static bool IsSameDirectory(string? path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
                PhysicalPathContainment.Comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
