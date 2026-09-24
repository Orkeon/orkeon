using Orkeon.Domain.FileSystem;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// Rule R (<see cref="TeamSessionLink"/>, STUDIO-25) over the forge's own files: a team
/// folder's <c>forge.json</c>, the workspace's sessions, and the record of the folder a session
/// names. The rule itself lives in Orkeon.Domain, once for the CLI and Studio; this type only
/// reads what it needs — the verbs that act on a team folder (<c>reopen</c>, the re-adoption of
/// <c>promote</c>) ask here, never by comparing paths themselves.
/// </summary>
/// <param name="Kind">Rule R's verdict on the folder.</param>
/// <param name="Session">
/// The session carrying the folder's id, whenever one does: the linked one, or — for a copy —
/// its original's. Null when no session carries it.
/// </param>
internal sealed record ForgeTeamLink(TeamSessionLinkKind Kind, ForgeSession? Session)
{
    /// <summary>Whether the folder is linked to <see cref="Session"/> — where <c>promotedTo</c> says, or moved there since.</summary>
    public bool IsLinked => TeamSessionLink.IsLinked(Kind);

    /// <summary>Rule R's verdict on <paramref name="teamDirectory"/> among the sessions of <paramref name="workspaceDirectory"/>.</summary>
    public static ForgeTeamLink Resolve(string workspaceDirectory, string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var teamId = ForgeTeamRecord.ReadSessionId(teamDirectory);
        var session = teamId is { } id ? ForgeSession.FindById(workspaceDirectory, id) : null;
        return new ForgeTeamLink(
            TeamSessionLink.Resolve(teamDirectory, teamId, session?.Promotion, ForgeTeamRecord.ReadSessionId),
            session);
    }

    /// <summary>
    /// Rule R's verdict on <paramref name="teamDirectory"/> for one given session — the question a
    /// re-adoption asks of a non-empty destination: is it this session's folder? A folder carrying
    /// another id answers <see cref="TeamSessionLinkKind.NoSession"/>.
    /// </summary>
    public static TeamSessionLinkKind Of(ForgeSession session, string teamDirectory)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        return TeamSessionLink.Resolve(
            teamDirectory, ForgeTeamRecord.ReadSessionId(teamDirectory), session.Promotion, ForgeTeamRecord.ReadSessionId);
    }
}
