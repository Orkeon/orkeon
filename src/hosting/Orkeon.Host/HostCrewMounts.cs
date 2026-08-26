namespace Orkeon.Host;

/// <summary>
/// The mounts a set of hosted crews needs, plus the virtual spelling of each crew.
/// </summary>
/// <param name="Mounts">Mount strings, one per distinct crew directory.</param>
/// <param name="VirtualPaths">
/// Virtual path of each crew, keyed by the crew's configured <c>Name</c> — what
/// <c>RunnerExecution.LoadCrewAsync</c> is given.
/// </param>
/// <param name="Roots">
/// The virtual roots those mounts claim (<c>/crews</c>, <c>/crews-1</c>, …). The daemon
/// refuses an operator <c>--mount</c> claiming one of them, so it needs them without
/// re-parsing the specs it just built.
/// </param>
internal sealed record HostCrewMountPlan(
    IReadOnlyList<string> Mounts,
    IReadOnlyDictionary<string, string> VirtualPaths,
    IReadOnlyList<string> Roots);

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
    /// <summary>The first crew directory's virtual root; further ones get a numeric suffix.</summary>
    public const string VirtualPathPrefix = "/crews";

    /// <summary>
    /// One read-only mount per distinct crew directory, each under a <b>name</b>
    /// (<c>/crews</c>, <c>/crews-1</c>, …) — never identity-mapped (ADR-008), so an agent
    /// hosted by the daemon is never handed the operator's disk layout. The naming rule is
    /// the one <c>CliCrewMountBootstrapper</c> already uses, so the repo has one convention.
    /// </summary>
    public static HostCrewMountPlan For(IEnumerable<HostedCrewOptions> crews)
    {
        ArgumentNullException.ThrowIfNull(crews);

        var materialized = crews.ToList();

        var directories = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var crew in materialized)
        {
            if (string.IsNullOrWhiteSpace(crew.Path))
                continue;

            var full = Path.GetFullPath(crew.Path);
            var directory = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
                directories.Add(directory);
        }

        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        var mounts = new List<string>();
        var index = 0;
        foreach (var directory in directories)
        {
            var root = index == 0 ? VirtualPathPrefix : $"{VirtualPathPrefix}-{index}";
            roots[directory] = root;
            mounts.Add($"{Orkeon.Domain.FileSystem.FileSystemMount.Quote(directory)}:{root}:ro");
            index++;
        }

        // First declaration wins, matching CrewHostRegistry.Find (FirstOrDefault by name): two
        // crews sharing a Name must not resolve to different definitions on the two paths.
        var virtualPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var crew in materialized)
        {
            if (string.IsNullOrWhiteSpace(crew.Path) || string.IsNullOrWhiteSpace(crew.Name))
                continue;
            if (virtualPaths.ContainsKey(crew.Name))
                continue;

            var full = Path.GetFullPath(crew.Path);
            var isDirectory = Directory.Exists(full);
            var directory = isDirectory ? full : Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(directory) || !roots.TryGetValue(directory, out var root))
                continue;

            virtualPaths[crew.Name] = isDirectory ? root : $"{root}/{Path.GetFileName(full)}";
        }

        return new HostCrewMountPlan(mounts, virtualPaths, [.. roots.Values]);
    }
}
