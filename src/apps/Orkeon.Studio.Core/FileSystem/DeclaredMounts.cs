using Orkeon.Compliance.Vfs;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Studio.Core.FileSystem;

/// <summary>
/// Answers, of a team's mount, the one question the Settings › Allowed folders screen exists to
/// answer: is this folder one this machine allows?
/// <para>
/// The settings hold the allow-list. A team associates entries from it; it never declares its
/// own. Two things follow, and both live here rather than in a front-end, so the WPF screens
/// and the TUIs cannot answer them differently.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; a team's mounts name user-owned folders " +
    "on the physical disk, and this compares them before any VFS mount exists — it reads nothing.")]
public static class DeclaredMounts
{
    /// <summary>
    /// Whether the folder behind <paramref name="mountString"/> is declared in the settings.
    /// <para>
    /// The comparison is on the physical folder alone. It is the unit the question is asked in
    /// ("is this folder allowed?"); the virtual spelling is a team's own business, and two teams
    /// may address one allowed folder under different names.
    /// </para>
    /// </summary>
    public static bool IsDeclared(string mountString, IReadOnlyList<string> declaredMounts)
    {
        ArgumentNullException.ThrowIfNull(declaredMounts);

        if (!MountDefinition.TryParse(mountString, out var mount, out _) || mount is null)
            return false;

        var folder = Normalize(mount.PhysicalPath);
        if (folder.Length == 0)
            return false;

        foreach (var entry in declaredMounts)
        {
            // Platform comparison, not a blanket OrdinalIgnoreCase: on Linux /home/u/docs and
            // /home/u/Docs are two different directories, and vouching for one because the
            // other is declared grants an undeclared folder.
            if (MountDefinition.TryParse(entry, out var declared, out _) && declared is not null
                && string.Equals(Normalize(declared.PhysicalPath), folder, Orkeon.Domain.FileSystem.PhysicalPathContainment.Comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The mounts of <paramref name="teamMounts"/> that must stop a launch: a folder neither
    /// declared in the settings nor living inside the team's own directory. Returned as the
    /// virtual paths the agents address, because that is what a screen may name (ADR-008).
    /// <para>
    /// A team's own <c>/output</c> and <c>/input</c> are created inside the team at adoption and
    /// are never declared — they are the team's own plumbing, not a reach outside the allow-list,
    /// and blocking on them would make every adopted team unlaunchable. Declaring them in the
    /// settings instead would work too, but it would put one team's private folders in the list
    /// every other team picks from.
    /// </para>
    /// <para>
    /// An entry the parser refuses blocks as well: a mount nobody can read is not a mount anybody
    /// can vouch for.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> BlockingFolders(
        IReadOnlyList<string> teamMounts,
        IReadOnlyList<string> declaredMounts,
        string? teamDirectory)
    {
        ArgumentNullException.ThrowIfNull(teamMounts);
        ArgumentNullException.ThrowIfNull(declaredMounts);

        var blocking = new List<string>();
        foreach (var mountString in teamMounts)
        {
            if (!MountDefinition.TryParse(mountString, out var mount, out _) || mount is null)
            {
                blocking.Add(mountString);
                continue;
            }

            if (IsDeclared(mountString, declaredMounts) || IsInsideTeam(mount.PhysicalPath, teamDirectory))
                continue;

            blocking.Add(mount.VirtualPath);
        }

        return blocking;
    }

    /// <summary>
    /// The folder that holds the team behind <paramref name="selectedPath"/> — the value
    /// <see cref="BlockingFolders"/> takes as its third argument. The picked target may be the
    /// folder itself or a crew file inside it: the same two shapes <c>TeamCatalog.DescribeTarget</c>
    /// reads the sidecar from.
    /// <para>
    /// It lives here, beside the containment predicate that consumes it, and not in a launcher
    /// screen. A physical-path rule written in a front-end is precisely what the earlier local
    /// copy of the containment test got wrong, and there are two launchers to keep in agreement.
    /// </para>
    /// </summary>
    /// <param name="selectedPath">The path the user picked; blank yields <see langword="null"/>.</param>
    /// <param name="directories">The probe that answers whether the path is a folder.</param>
    public static string? TeamDirectoryOf(string? selectedPath, IDirectoryProbe directories)
    {
        ArgumentNullException.ThrowIfNull(directories);

        if (selectedPath is not { Length: > 0 } path)
            return null;

        try
        {
            return directories.Exists(path)
                ? path
                : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.PathTooLongException or NotSupportedException)
        {
            // A path the platform refuses to resolve names no team folder we can vouch for.
            return null;
        }
    }

    private static bool IsInsideTeam(string physicalPath, string? teamDirectory)
    {
        if (teamDirectory is not { Length: > 0 } || physicalPath is not { Length: > 0 })
            return false;

        try
        {
            // The one containment predicate: a per-OS comparison written here would know nothing
            // of AltDirectorySeparatorChar, the way an earlier local copy did not.
            return PhysicalPathContainment.IsUnder(
                System.IO.Path.GetFullPath(physicalPath),
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(teamDirectory)));
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.PathTooLongException or NotSupportedException)
        {
            // A path the platform refuses to resolve is not one we can vouch for either.
            return false;
        }
    }

    private static string Normalize(string path) => path.Trim().TrimEnd('/', '\\');
}
