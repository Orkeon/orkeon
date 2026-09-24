namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The session folder follows its team (STUDIO-26). A session is named after the need it was
/// opened with, and the team it makes after whatever the user calls it: left alone, the atelier
/// lists its sessions under names no team carries. Once a promotion is written, the session
/// under <c>.orkeon/forge/</c> takes the name of the folder it promoted to — the name as it is,
/// no slug recomputed (D-02). Another session already so named is never overwritten: the name
/// is suffixed <c>-2</c>, <c>-3</c>… and the event says so (D-04). A move the disk refuses — a
/// handle held open on Windows, an antivirus — leaves the promotion as it stands, said by a
/// structured warning: the team is linked to its session by the id either way, never by a
/// folder's name (D-05, rule R). There is no lock to wait for — none exists.
/// </summary>
internal static class ForgeSessionFolder
{
    /// <summary>
    /// Renames <paramref name="session"/>'s folder after <paramref name="teamDirectory"/>, rewrites
    /// the slug its <c>session.json</c> and the team's <c>forge.json</c> carry, and says it on
    /// <paramref name="events"/>: <c>session.renamed</c> when the folder moved, a <c>warning</c>
    /// when the disk refused. Returns the session as it now stands — reloaded from its new folder,
    /// or <paramref name="session"/> itself when nothing moved. Never throws for the disk: the
    /// promotion it follows is already written.
    /// </summary>
    public static ForgeSession FollowTeam(
        ForgeSession session, string teamDirectory, ForgeEventWriter events, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(events);

        var teamFolderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(teamDirectory));
        if (teamFolderName.Length == 0 || session.FolderNameAfter(teamFolderName) is not { } slug)
            return session;

        var from = session.Document.Slug;
        ForgeSession moved;
        try
        {
            moved = session.MoveTo(slug, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            events.Warning(
                ForgeErrorCodes.SessionNotRenamed,
                $"The session folder '{from}' keeps its name: renaming it '{slug}' after its team was refused ({ex.Message})."
                + " The team is linked to its session by its id all the same.");
            return session;
        }

        events.SessionRenamed(from, slug, moved.Directory, suffixed: !string.Equals(slug, teamFolderName, StringComparison.Ordinal));

        // The record a rebuild names its session after (FORGE-09) follows too. Its id is what
        // links the two, so a record the disk will not rewrite costs a stale slug, nothing more.
        try
        {
            ForgeTeamRecord.Relink(teamDirectory, moved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            events.Warning(
                ForgeErrorCodes.SessionNotRenamed,
                $"The session is now '{slug}', but the team's {ForgeTeamRecord.FileName} still names it '{from}' ({ex.Message})."
                + " The team is linked to its session by its id all the same.");
        }

        return moved;
    }
}
