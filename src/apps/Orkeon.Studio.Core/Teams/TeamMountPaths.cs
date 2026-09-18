using System.Diagnostics.CodeAnalysis;
using Orkeon.Compliance.Vfs;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Core.Teams;

/// <summary>
/// The sidecar's convention for a folder that lives INSIDE the team: a physical segment
/// that starts with <c>./</c> is relative to the team folder — the one that carries
/// <c>studio-team.json</c>. Canonical forms: <c>./input:/workspace:ro</c>,
/// <c>./output:/output:rw</c>, <c>./rapports:/rapports:rw</c> — one folder name, <c>/</c>
/// on both OSes, never <c>..</c>, never quoted. An absolute entry stays what it is: a folder
/// of the user's, outside the team. An entry nobody can parse passes through untouched, both
/// ways — the screens say "unreadable" (ADR-008), nothing rewrites it.
/// <para>
/// Why a convention rather than a remedy: the sidecar used to record absolute paths, so a
/// team copied, exported or moved kept writing into the old folder unless every copy was
/// "rebased" on the way. A relative entry needs no such step — a team is a folder one
/// carries. The runtime, for its part, resolves a relative physical path against the process
/// cwd and nothing else, so the catalog resolves these entries ONCE at its boundary
/// (<see cref="TeamCatalog.Describe"/>, <see cref="TeamCatalog.DescribeTarget"/>) and every
/// launcher keeps receiving absolute paths, exactly as before.
/// </para>
/// <para>
/// A <c>./x</c> segment can never be mistaken for a Windows drive: the mount grammar reads
/// a drive-letter prefix (<c>X:/</c>) on every OS, so a bare one-letter root written
/// <c>x:/x:rw</c> would split into the wrong segments — <c>./x:/x:rw</c> never does.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; a team's mounts name user-owned folders " +
    "on the physical disk, and this resolves them before any VFS mount exists — it reads nothing.")]
public static class TeamMountPaths
{
    /// <summary>What a team-relative physical segment starts with.</summary>
    public const string RelativePrefix = "./";

    /// <summary>The virtual root a team reads from; its in-team folder is <see cref="ReadFolderName"/>.</summary>
    public const string ReadRoot = RunnerVirtualRoots.Workspace;

    /// <summary>The virtual root a team writes its deliverables to.</summary>
    public const string WriteRoot = RunnerVirtualRoots.Output;

    /// <summary>
    /// Folder inside a team backing <see cref="ReadRoot"/>. Not the team's root: a settings
    /// file copied into the team (<c>--with-settings</c>) holds API keys, and a read mount
    /// over the root would hand them to any agent with a file tool. The same name
    /// <c>forge promote</c> creates — the CLI keeps its own literal, pinned by its suite.
    /// </summary>
    public const string ReadFolderName = "input";

    /// <summary>
    /// Whether the physical segment of <paramref name="mountString"/> starts with
    /// <see cref="RelativePrefix"/> — the spelling alone, well-formed or not (see
    /// <see cref="TryGetRelativeFolder"/> for the folder it names).
    /// </summary>
    public static bool IsTeamRelative(string? mountString) =>
        mountString is not null
        && FileSystemMount.TryGetBasePath(mountString) is { } physical
        && physical.StartsWith(RelativePrefix, StringComparison.Ordinal);

    /// <summary>
    /// The folder a team-relative entry names, without its <c>./</c> — <c>input</c> for
    /// <c>./input:/workspace:ro</c>. False for anything else: an absolute or unreadable entry,
    /// and a <c>./</c> segment that escapes the team (<c>./../x</c>), names no folder
    /// (<c>./</c>) or is rooted — those are not team-relative, whatever their prefix says.
    /// </summary>
    public static bool TryGetRelativeFolder(string? mountString, [NotNullWhen(true)] out string? folder)
    {
        folder = null;

        if (mountString is null
            || FileSystemMount.TryGetBasePath(mountString) is not { } physical
            || !physical.StartsWith(RelativePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = physical[RelativePrefix.Length..];
        if (!IsWellFormedFolder(candidate))
            return false;

        folder = candidate;
        return true;
    }

    /// <summary>
    /// The in-team folder behind a virtual root: <c>input</c> for <see cref="ReadRoot"/>,
    /// the root's own name otherwise (<c>/output</c> → <c>output</c>, <c>/rapports</c> →
    /// <c>rapports</c>).
    /// </summary>
    /// <exception cref="ArgumentException">The virtual path names no folder (<c>/</c>).</exception>
    public static string FolderFor(string virtualPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        var name = virtualPath.Trim().Trim('/');
        if (name.Length == 0)
            throw new ArgumentException("A virtual root needs a name to derive its folder from.", nameof(virtualPath));

        return string.Equals(name, ReadRoot.TrimStart('/'), StringComparison.Ordinal)
            ? ReadFolderName
            : name;
    }

    /// <summary>
    /// The canonical entry binding <paramref name="virtualPath"/> to its own folder inside the
    /// team: <c>./output:/output:rw</c>, <c>./input:/workspace:ro</c>.
    /// </summary>
    public static string InsideTeam(string virtualPath, MountRights rights)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        return new MountDefinition
        {
            PhysicalPath = RelativePrefix + FolderFor(virtualPath),
            VirtualPath = virtualPath.Trim(),
            Rights = rights,
        }.ToMountString();
    }

    /// <summary>
    /// The entry with its team-relative folder resolved under <paramref name="teamDirectory"/>
    /// — quoted when the resulting path needs it (a <c>:</c> in a Windows segment, say).
    /// Anything that is not team-relative comes back verbatim.
    /// </summary>
    public static string Resolve(string teamDirectory, string mountString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(mountString);

        if (!TryGetRelativeFolder(mountString, out var folder))
            return mountString;

        try
        {
            return FileSystemMount.WithBasePath(
                mountString,
                Path.GetFullPath(Path.Combine(teamDirectory, folder)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or FormatException)
        {
            // A path the platform refuses to resolve is not one we can bind; the entry keeps
            // its spelling and the screens say it is unreadable.
            return mountString;
        }
    }

    /// <summary>
    /// The entry rewritten team-relative when its physical folder sits strictly inside
    /// <paramref name="teamDirectory"/> — <c>&lt;team&gt;/output:/output:rw</c> becomes
    /// <c>./output:/output:rw</c>. An entry already relative, one outside the team, one on
    /// the team folder itself, and one nobody can parse all come back verbatim.
    /// </summary>
    public static string Relativize(string teamDirectory, string mountString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(mountString);

        if (IsTeamRelative(mountString) || FileSystemMount.TryGetBasePath(mountString) is not { } physical)
            return mountString;

        try
        {
            var team = Path.TrimEndingDirectorySeparator(Path.GetFullPath(teamDirectory));
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physical));

            // Strictly under, through the one containment predicate: the team folder itself
            // names no sub-folder, and `<team>-old` is not inside `<team>`.
            if (full.Length <= team.Length || !PhysicalPathContainment.IsUnder(full, team))
                return mountString;

            var folder = full[(team.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
            return FileSystemMount.WithBasePath(mountString, RelativePrefix + folder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or FormatException)
        {
            return mountString;
        }
    }

    /// <summary><see cref="Resolve"/> over a list; empty for a null or empty list.</summary>
    public static IReadOnlyList<string> ResolveAll(string teamDirectory, IReadOnlyList<string>? mounts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        if (mounts is not { Count: > 0 })
            return [];

        return [.. mounts.Select(mount => Resolve(teamDirectory, mount))];
    }

    /// <summary><see cref="Relativize"/> over a list; empty for a null or empty list.</summary>
    public static IReadOnlyList<string> RelativizeAll(string teamDirectory, IReadOnlyList<string>? mounts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        if (mounts is not { Count: > 0 })
            return [];

        return [.. mounts.Select(mount => Relativize(teamDirectory, mount))];
    }

    /// <summary>
    /// A folder that stays inside the team whatever the OS makes of it: at least one segment,
    /// none empty, none <c>.</c> or <c>..</c>, and not rooted — <c>./C:\x</c> on Windows would
    /// leave the team through the drive, not through a parent.
    /// </summary>
    private static bool IsWellFormedFolder(string folder)
    {
        if (folder.Length == 0 || Path.IsPathRooted(folder))
            return false;

        foreach (var segment in folder.Split('/', '\\'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
                return false;
        }

        return true;
    }
}
