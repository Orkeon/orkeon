namespace Orkeon.Studio.Core.Teams;

/// <summary>
/// Where a team's folders live, as the wizard's first step asks it — one answer for the two
/// canonical roots (<see cref="TeamMountPaths.ReadRoot"/>, <see cref="TeamMountPaths.WriteRoot"/>),
/// the only ones a team can address before it has a blueprint.
/// </summary>
public enum FolderPolicy
{
    /// <summary>Decide later: the rows stay unanswered, and the Composer step offers them again.</summary>
    Later = 0,

    /// <summary>
    /// Created inside the team folder at adoption and recorded team-relative in the sidecar
    /// (<c>./input</c>, <c>./output</c>) — the team is a folder one carries.
    /// </summary>
    InsideTeam,

    /// <summary>
    /// Existing folders on this machine, where data already lives: picked from the disk and
    /// declared in Settings › Allowed folders on the way.
    /// </summary>
    ExistingFolders,
}
