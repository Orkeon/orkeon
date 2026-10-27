namespace Orkeon.Tools.Code.Confinement;

/// <summary>What a confined process may do with a path bound into its view.</summary>
internal enum ConfinedAccess
{
    /// <summary>Bound read-only: the kernel refuses a write with a read-only file system error.</summary>
    ReadOnly,

    /// <summary>Bound read-write.</summary>
    ReadWrite,

    /// <summary>A directory the VFS grants no right on: an empty, read-only tmpfs covers it.</summary>
    HiddenDirectory,

    /// <summary>A file the VFS grants no right on: <c>/dev/null</c> covers it.</summary>
    HiddenFile,
}

/// <summary>A host path bound into the confined view at the virtual path a mount gives it.</summary>
/// <param name="Source">The physical path, as the VFS resolved it.</param>
/// <param name="Destination">The virtual path the process sees it at.</param>
/// <param name="Access">What the process may do there.</param>
internal sealed record ConfinedPath(string Source, string Destination, ConfinedAccess Access);

/// <summary>
/// The arguments of <c>bwrap</c> that build a process's view of the file system from the VFS. Pure:
/// it reads no file and starts nothing, so the view can be checked argument by argument.
/// </summary>
internal static class BubblewrapCommandLine
{
    /// <summary>
    /// The host directories every confined process gets read-only, at their own path, when they exist:
    /// the programs, their libraries and the system configuration they read (the dynamic loader's cache,
    /// the users, the certificates, the resolver).
    /// </summary>
    public static readonly IReadOnlyList<string> SystemPaths =
        ["/usr", "/bin", "/sbin", "/lib", "/lib32", "/lib64", "/libx32", "/etc"];

    /// <summary>
    /// The arguments that come before the command: namespaces, the system directories, the extra
    /// read-only paths, then each bound path parents first, the environment the view needs and the
    /// working directory, ending with <c>--</c>.
    /// </summary>
    /// <param name="paths">The mounts and their sub paths, in any order.</param>
    /// <param name="readOnlyPaths">Extra absolute host directories bound read-only at their own path.</param>
    /// <param name="allowNetwork">Whether the process shares the host's network.</param>
    /// <param name="workingDirectory">The virtual directory the process starts in.</param>
    /// <param name="socketPaths">
    /// Host sockets of the operator's session (the ssh agent, the session bus) bound read-write at
    /// their own path, after the mounts; ignored when <paramref name="allowNetwork"/> is false.
    /// </param>
    public static IReadOnlyList<string> Build(
        IEnumerable<ConfinedPath> paths,
        IEnumerable<string> readOnlyPaths,
        bool allowNetwork,
        string workingDirectory,
        IEnumerable<string> socketPaths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(readOnlyPaths);
        ArgumentNullException.ThrowIfNull(socketPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        // Every namespace bwrap can unshare: user, ipc, pid, net, uts, cgroup. The process dies with
        // its parent, and a new session keeps it off the caller's terminal.
        var arguments = new List<string> { "--unshare-all" };
        if (allowNetwork)
            arguments.Add("--share-net");
        arguments.AddRange(["--die-with-parent", "--new-session", "--proc", "/proc", "--dev", "/dev", "--tmpfs", "/tmp"]);

        foreach (var path in SystemPaths.Concat(readOnlyPaths).Distinct(StringComparer.Ordinal))
            arguments.AddRange(["--ro-bind-try", path, path]);

        // A parent is bound before what it contains, so a sub path's own rights cover the parent's.
        foreach (var path in paths.OrderBy(p => Depth(p.Destination)).ThenBy(p => p.Destination, StringComparer.Ordinal))
            arguments.AddRange(Bind(path));

        // The sockets alone, never the directory that holds them, and only alongside the network the
        // credential they lead to is used over. Last, so a mount over their parent does not cover them.
        if (allowNetwork)
        {
            foreach (var socket in socketPaths.Distinct(StringComparer.Ordinal))
                arguments.AddRange(["--bind-try", socket, socket]);
        }

        // The host's HOME and TMPDIR name directories the view does not hold.
        arguments.AddRange(["--setenv", "HOME", "/tmp", "--setenv", "TMPDIR", "/tmp", "--chdir", workingDirectory, "--"]);
        return arguments;
    }

    private static string[] Bind(ConfinedPath path) => path.Access switch
    {
        ConfinedAccess.ReadOnly => ["--ro-bind-try", path.Source, path.Destination],
        ConfinedAccess.ReadWrite => ["--bind-try", path.Source, path.Destination],
        ConfinedAccess.HiddenDirectory => ["--tmpfs", path.Destination, "--remount-ro", path.Destination],
        ConfinedAccess.HiddenFile => ["--ro-bind", "/dev/null", path.Destination],
        _ => throw new ArgumentOutOfRangeException(nameof(path), path.Access, "Unknown confined access."),
    };

    private static int Depth(string path) => path.Count(c => c == '/');
}
