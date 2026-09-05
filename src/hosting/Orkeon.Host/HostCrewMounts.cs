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

        var directories = DistinctDirectories(materialized);
        var roots = RootsByDirectory(directories);
        var mounts = directories
            .Select(directory => $"{Orkeon.Domain.FileSystem.FileSystemMount.Quote(directory)}:{roots[directory]}:ro")
            .ToList();

        return new HostCrewMountPlan(mounts, VirtualPathsByCrewName(materialized, roots), [.. roots.Values]);
    }

    /// <summary>
    /// Every distinct directory the crews live in, ordinal-sorted so a given configuration
    /// always hands the same directory the same numeric suffix.
    /// </summary>
    private static SortedSet<string> DistinctDirectories(IEnumerable<HostedCrewOptions> crews) =>
        new(crews.Select(crew => Resolve(crew.Path)?.DirectoryPath).OfType<string>(), StringComparer.Ordinal);

    /// <summary>The virtual root each directory is mounted under: /crews, /crews-1, and so on.</summary>
    private static Dictionary<string, string> RootsByDirectory(IEnumerable<string> directories) =>
        directories
            .Select((directory, index) => (directory, root: index == 0 ? VirtualPathPrefix : $"{VirtualPathPrefix}-{index}"))
            .ToDictionary(entry => entry.directory, entry => entry.root, StringComparer.Ordinal);

    /// <summary>
    /// The virtual spelling of each crew, keyed by its configured name. A crew whose directory
    /// got no mount (an empty or rootless path) is simply absent.
    /// </summary>
    private static Dictionary<string, string> VirtualPathsByCrewName(
        IEnumerable<HostedCrewOptions> crews,
        IReadOnlyDictionary<string, string> roots)
    {
        // First declaration wins, matching CrewHostRegistry.Find (FirstOrDefault by name): two
        // crews sharing a Name must not resolve to different definitions on the two paths.
        var virtualPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var crew in crews)
        {
            if (string.IsNullOrWhiteSpace(crew.Name) || virtualPaths.ContainsKey(crew.Name))
                continue;

            if (VirtualPathOf(crew.Path, roots) is { } virtualPath)
                virtualPaths[crew.Name] = virtualPath;
        }

        return virtualPaths;
    }

    /// <summary>
    /// How one crew path is spelled inside the VFS: the mount root itself for a crew directory,
    /// the file under that root otherwise. Null when nothing mounted that path.
    /// </summary>
    private static string? VirtualPathOf(string path, IReadOnlyDictionary<string, string> roots)
    {
        if (Resolve(path) is not { } resolved || !roots.TryGetValue(resolved.DirectoryPath, out var root))
            return null;

        return resolved.IsDirectory ? root : $"{root}/{Path.GetFileName(resolved.FullPath)}";
    }

    /// <summary>
    /// A configured crew path resolved on the physical disk, or null when it names no
    /// directory the host could mount (blank, or a path with no parent).
    /// </summary>
    private static ResolvedCrewPath? Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var full = Path.GetFullPath(path);
        var isDirectory = Directory.Exists(full);
        var directory = isDirectory ? full : Path.GetDirectoryName(full);

        return string.IsNullOrEmpty(directory) ? null : new ResolvedCrewPath(full, directory, isDirectory);
    }

    /// <summary>
    /// A crew path resolved on disk: its full form, the directory that has to be mounted, and
    /// whether the path IS that directory (a multi-file crew) rather than a file inside it.
    /// </summary>
    private readonly record struct ResolvedCrewPath(string FullPath, string DirectoryPath, bool IsDirectory);
}
