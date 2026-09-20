using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Launch;

/// <summary>
/// What of a team's folders a launch has to say on the command line (STUDIO-15 D-05, VFS-90).
/// <para>
/// A settings entry a team names is selected by its id — <c>--mount-id</c>, no path on the
/// command line, the machine's own declaration in force as it stands today (D-01); an entry
/// without an id that the settings hold as recorded needs nothing at all. The team's own
/// folders and any copy the settings do not hold are the team's intent and go as
/// <c>--mount</c>, which replaces every settings entry of that root for the run. An id this
/// machine does not declare stops the launch: the team refers to a declaration that is not
/// here, and only the person can say which folder stands behind it (D-06).
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "OUT-OF-SCOPE: turns the mount strings a team recorded into command-line arguments, before " +
    "any VFS mount exists. Nothing is opened — this is string arithmetic.")]
public sealed record LaunchMountPlan
{
    /// <summary>The <c>--mount-id</c> values, in team order, each once.</summary>
    public IReadOnlyList<string> MountIds { get; init; } = [];

    /// <summary>The <c>--mount</c> values, in team order.</summary>
    public IReadOnlyList<string> Mounts { get; init; } = [];

    /// <summary>The ids the team names that this machine does not declare; non-empty blocks the launch.</summary>
    public IReadOnlyList<string> UnknownIds { get; init; } = [];

    /// <summary>The plan for a team's resolved mounts (<see cref="TeamMountResolution.Resolve"/>).</summary>
    public static LaunchMountPlan For(IReadOnlyList<ResolvedTeamMount> teamMounts)
    {
        ArgumentNullException.ThrowIfNull(teamMounts);

        var mountIds = new List<string>();
        var mounts = new List<string>();
        var unknown = new List<string>();
        foreach (var mount in teamMounts)
        {
            switch (mount.Source)
            {
                case TeamMountSource.Settings:
                    if (mount.SettingsEntry?.Id is { } id && !mountIds.Contains(id.ToString()))
                        mountIds.Add(id.ToString());
                    break;
                case TeamMountSource.UnknownId:
                    if (mount.Id is { } unknownId && !unknown.Contains(unknownId.ToString()))
                        unknown.Add(unknownId.ToString());
                    break;
                default:
                    // InsideTeam and Copy are the team's own intent; an unreadable entry is
                    // passed on so the runner reports it with its own message.
                    mounts.Add(mount.Effective);
                    break;
            }
        }

        return new LaunchMountPlan { MountIds = mountIds, Mounts = mounts, UnknownIds = unknown };
    }
}
