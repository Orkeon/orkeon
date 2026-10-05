using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Launch;

/// <summary>
/// What a launch prepared of a team's own folders (<see cref="TeamFolderPreparation.Prepare"/>).
/// </summary>
/// <param name="Created">The folders created, absolute, in team order.</param>
/// <param name="Failures">The folders the disk refused to create, each with the disk's reason.</param>
/// <param name="MissingReadOnly">The read-only folders that do not exist, each with its mount point.</param>
public sealed record TeamFolderPreparationResult(
    IReadOnlyList<string> Created,
    IReadOnlyList<(string Folder, string Error)> Failures,
    IReadOnlyList<(string Folder, string Virtual)> MissingReadOnly)
{
    /// <summary>Whether the launch may go on: nothing refused, nothing missing.</summary>
    public bool Succeeded => Failures.Count == 0 && MissingReadOnly.Count == 0;
}

/// <summary>
/// Prepares a team's own folders before a launch (STUDIO-60), the rule the workshop's
/// <c>run.sh</c>/<c>run.cmd</c> already apply: a missing writable folder is created, a
/// missing read-only one refuses the launch by name — an empty input is an error to show,
/// not a folder to invent. Only a well-formed <c>./x</c> entry of the team is looked at;
/// an absolute entry, a copy, a settings declaration, an unknown id, an unreadable entry and
/// a <c>./../x</c> are left as they are, so nothing is ever created outside the team.
/// <para>
/// Additive and idempotent: the sidecar is not written, no <c>.gitkeep</c> appears, and
/// <c>TeamCatalog</c> keeps creating every folder when it writes the sidecar. Pure over
/// <see cref="IDirectoryProbe"/>, which owns the disk access.
/// </para>
/// </summary>
public static class TeamFolderPreparation
{
    /// <summary>
    /// Examines every in-team mount of <paramref name="mounts"/> under <paramref name="teamDirectory"/>.
    /// </summary>
    /// <param name="teamDirectory">The team folder; null or blank prepares nothing.</param>
    /// <param name="mounts">The team's resolved mounts, as the target description carries them.</param>
    /// <param name="directories">The probe that owns the disk access.</param>
    public static TeamFolderPreparationResult Prepare(
        string? teamDirectory,
        IReadOnlyList<ResolvedTeamMount> mounts,
        IDirectoryProbe directories)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        ArgumentNullException.ThrowIfNull(directories);

        var created = new List<string>();
        var failures = new List<(string Folder, string Error)>();
        var missing = new List<(string Folder, string Virtual)>();

        if (string.IsNullOrWhiteSpace(teamDirectory))
            return new TeamFolderPreparationResult(created, failures, missing);

        foreach (var mount in mounts)
        {
            if (mount.Source != TeamMountSource.InsideTeam
                || !TeamMountPaths.TryGetRelativeFolder(mount.Raw, out var folder)
                || !MountDefinition.TryParse(mount.Raw, out var definition, out _))
            {
                continue;
            }

            var path = Path.Combine(teamDirectory, folder);
            if (directories.Exists(path))
                continue;

            if (definition.Rights == MountRights.ReadOnly)
            {
                missing.Add((path, definition.VirtualPath));
            }
            else if (directories.TryCreate(path, out var error))
            {
                created.Add(path);
            }
            else
            {
                failures.Add((path, error));
            }
        }

        return new TeamFolderPreparationResult(created, failures, missing);
    }
}
