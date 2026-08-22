namespace Orkeon.Host;

/// <summary>
/// Derives the VFS mounts a hosted crew needs from its configured path (GATE-02).
/// <para>
/// The crew loader reads through <c>IFileSystemService</c>, never the raw disk — the same
/// rule as everything else in the framework. The CLI auto-mounts its config's directory for
/// exactly that reason; the host has to do the same for every crew it hosts, or the startup
/// probe passes on the physical path and every message then fails with "file not found" on
/// the virtual one. The review's first real-composition runner test caught precisely that.
/// </para>
/// </summary>
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: classifies operator-supplied crew paths on the physical disk to provision the mounts, before the VFS exists.")]
internal static class HostCrewMounts
{
    /// <summary>
    /// One read-only 1:1 mount per distinct crew directory: physical and virtual coincide,
    /// so the configured <c>Path</c> works verbatim on both sides of the abstraction.
    /// </summary>
    public static IReadOnlyList<string> For(IEnumerable<HostedCrewOptions> crews)
    {
        ArgumentNullException.ThrowIfNull(crews);

        var directories = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var crew in crews)
        {
            if (string.IsNullOrWhiteSpace(crew.Path))
                continue;

            var full = Path.GetFullPath(crew.Path);
            var directory = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
                directories.Add(directory);
        }

        return [.. directories.Select(d => $"{d}:{d}:ro")];
    }
}
