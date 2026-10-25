using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Code.Confinement;

/// <summary>A git directory bound for a linked-worktree agent root, with the virtual path it was read at.</summary>
/// <param name="VirtualPath">Where the VFS shows the directory.</param>
/// <param name="Path">How the confined view binds it.</param>
internal sealed record GitWorktreeBind(string VirtualPath, ConfinedPath Path);

/// <summary>A sub-path override of the mount that holds the git dirs, with what it grants there.</summary>
/// <param name="VirtualPath">Where the override applies.</param>
/// <param name="Source">The physical path it covers.</param>
/// <param name="Access">What it grants, a hidden kind when it grants neither Read nor Write.</param>
internal sealed record GitOverride(string VirtualPath, string Source, ConfinedAccess Access);

/// <summary>The mount that holds an agent root, its git dirs included.</summary>
/// <param name="VirtualRoot">The virtual root of the mount.</param>
/// <param name="PhysicalRoot">The physical root that mount resolves to.</param>
internal readonly record struct GitWorktreeMount(string VirtualRoot, string PhysicalRoot);

/// <summary>
/// The git directories a linked worktree needs when it is an agent's working root (#188). A root whose
/// <c>.git</c> is a file (<c>gitdir: &lt;path&gt;</c>, written by <c>git worktree add</c>) commits through
/// the worktree's git dir (<c>&lt;common&gt;/worktrees/&lt;name&gt;</c>) and the repository's common dir,
/// both outside the root: the root alone is not a repository.
/// <para>
/// The common dir is shared with the main checkout, whose git (run by the user on the host) reads its
/// <c>config</c>, <c>hooks</c>, <c>info</c>, <c>modules</c> and the other worktrees' git dirs. It is
/// therefore bound READ-ONLY as a whole, and only what a commit writes is bound read-write over it:
/// <c>objects</c>, <c>refs</c>, <c>logs</c> and this worktree's own git dir. That git dir was already the
/// agent's in effect: git run in the worktree trusts its <c>.git</c> file, which lies in the root.
/// </para>
/// <para>
/// Nothing is bound unless every check holds: the gitdir sits in a <c>worktrees</c> directory of the
/// common dir, its <c>commondir</c> names that common dir, its back-link <c>gitdir</c> names this root's
/// <c>.git</c>, both directories have the git layout, both lie on the mount outside the root, and no
/// component of their path below the mount's root is a symlink (the VFS resolves a path lexically, the
/// kernel follows links when it binds the source). Anything else is skipped and logged at Debug.
/// </para>
/// <para>
/// The checks run when the shell command starts; a job of the same agent could run meanwhile. What it
/// may write under these rules lies in <c>objects</c>, <c>refs</c>, <c>logs</c> or the gitdir, none of
/// which is a component of the gitdir or common dir path, so it cannot swap a checked component.
/// </para>
/// </summary>
internal static partial class GitWorktreeBinds
{
    private const string GitDirPrefix = "gitdir:";

    /// <summary>The directories of the common dir a commit writes, bound read-write over it.</summary>
    private static readonly string[] s_writableCommonDirs = ["objects", "refs", "logs"];

    /// <summary>A git directory: its virtual path, and the path the confined process looks it up at.</summary>
    private readonly record struct GitDir(string VirtualPath, string Destination, bool AtHostPath);

    /// <summary>
    /// The binds the worktree at <paramref name="agentRoot"/> needs: the common dir read-only, then its
    /// <c>objects</c>, <c>refs</c> and <c>logs</c>, then the worktree's git dir; empty when the root is
    /// not a linked worktree that passes every check.
    /// </summary>
    /// <param name="fileSystem">The VFS the files are read through.</param>
    /// <param name="agentRoot">The agent root, a virtual path in the mount, without a trailing slash.</param>
    /// <param name="mount">The mount that holds the root.</param>
    /// <param name="rootAccess">How the root is bound; no git dir is bound with more.</param>
    /// <param name="accessAt">How a virtual path is bound by the rights in force there, null when it grants nothing.</param>
    /// <param name="logger">Where a skipped worktree is logged, at Debug.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public static async Task<IReadOnlyList<GitWorktreeBind>> FindAsync(
        IFileSystemService fileSystem,
        string agentRoot,
        GitWorktreeMount mount,
        ConfinedAccess rootAccess,
        Func<string, ConfinedAccess?> accessAt,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(accessAt);
        ArgumentNullException.ThrowIfNull(logger);

        // A root without a .git, or with a .git directory, is not a linked worktree: nothing to add.
        var dotGit = $"{agentRoot}/.git";
        if (await KindAsync(fileSystem, dotGit, cancellationToken).ConfigureAwait(false) != VirtualEntryKind.File)
            return [];

        var context = new Context(fileSystem, agentRoot, mount.VirtualRoot, mount.PhysicalRoot);
        var link = FirstLine(await ReadAsync(fileSystem, dotGit, cancellationToken).ConfigureAwait(false));
        if (link is null || !link.StartsWith(GitDirPrefix, StringComparison.Ordinal))
            return Skipped(logger, agentRoot, "the .git file names no gitdir");

        if (context.Locate(link[GitDirPrefix.Length..].Trim(), new GitDir(agentRoot, agentRoot, AtHostPath: false)) is not { } gitDir)
            return Skipped(logger, agentRoot, "its gitdir is not a path of the mount outside the root");

        var (common, reason) = await context.CheckAsync(gitDir, dotGit, cancellationToken).ConfigureAwait(false);
        if (common is not { } commonDir)
            return Skipped(logger, agentRoot, reason ?? "its gitdir fails a check");

        if (accessAt(commonDir.VirtualPath) is null || Bind(fileSystem, commonDir, ConfinedAccess.ReadOnly) is not { } commonBind)
            return Skipped(logger, agentRoot, "its common dir grants no right");

        var binds = new List<GitWorktreeBind> { commonBind };
        await AddWritableCommonDirsAsync(binds, fileSystem, commonDir, rootAccess, accessAt, cancellationToken).ConfigureAwait(false);
        if (Capped(accessAt(gitDir.VirtualPath), rootAccess) is { } gitDirAccess && Bind(fileSystem, gitDir, gitDirAccess) is { } gitDirBind)
            binds.Add(gitDirBind);
        return binds;
    }

    /// <summary>
    /// Adds the <c>objects</c>, <c>refs</c> and <c>logs</c> of <paramref name="commonDir"/> read-write
    /// (capped); a missing one, or a symlink, is left under the read-only common dir.
    /// </summary>
    private static async Task AddWritableCommonDirsAsync(
        List<GitWorktreeBind> binds, IFileSystemService fileSystem, GitDir commonDir, ConfinedAccess rootAccess,
        Func<string, ConfinedAccess?> accessAt, CancellationToken cancellationToken)
    {
        foreach (var name in s_writableCommonDirs)
        {
            var child = Child(commonDir, name);
            if (await KindAsync(fileSystem, child.VirtualPath, cancellationToken).ConfigureAwait(false) == VirtualEntryKind.Directory
                && Capped(accessAt(child.VirtualPath), rootAccess) is { } access
                && Bind(fileSystem, child, access) is { } childBind)
                binds.Add(childBind);
        }
    }

    /// <summary>
    /// The overrides of the mount that lie under one of <paramref name="binds"/>, each with the lesser
    /// of what it grants and what the deepest bind that holds it grants, at the path that bind is seen
    /// at. Added after the binds, so an override narrows a git dir but never widens one: the common dir
    /// stays read-only whatever an override says.
    /// </summary>
    public static IReadOnlyList<ConfinedPath> Narrow(IReadOnlyList<GitWorktreeBind> binds, IEnumerable<GitOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(binds);
        ArgumentNullException.ThrowIfNull(overrides);

        var narrowed = new List<ConfinedPath>();
        foreach (var subPath in overrides)
        {
            var holder = binds
                .Where(b => ShellCommandTool.MatchesMountPrefix(subPath.VirtualPath, b.VirtualPath))
                .MaxBy(b => b.VirtualPath.Length);
            if (holder is null)
                continue;

            var access = Rank(subPath.Access) < Rank(holder.Path.Access) ? subPath.Access : holder.Path.Access;
            var destination = holder.Path.Destination + subPath.VirtualPath[holder.VirtualPath.Length..];
            narrowed.Add(new ConfinedPath(subPath.Source, destination, access));
        }

        return narrowed;
    }

    /// <summary>How much an access grants: hidden, then read-only, then read-write.</summary>
    private static int Rank(ConfinedAccess access) => access switch
    {
        ConfinedAccess.ReadWrite => 2,
        ConfinedAccess.ReadOnly => 1,
        _ => 0,
    };

    /// <summary><paramref name="access"/> capped by the root's; null when it grants nothing.</summary>
    private static ConfinedAccess? Capped(ConfinedAccess? access, ConfinedAccess rootAccess) => access switch
    {
        null => null,
        ConfinedAccess.ReadWrite when rootAccess == ConfinedAccess.ReadWrite => ConfinedAccess.ReadWrite,
        _ => ConfinedAccess.ReadOnly,
    };

    /// <summary>The bind of <paramref name="dir"/> with <paramref name="access"/>; null when the VFS does not resolve it.</summary>
    private static GitWorktreeBind? Bind(IFileSystemService fileSystem, GitDir dir, ConfinedAccess access)
    {
        var resolved = fileSystem.ResolveAndValidate(dir.VirtualPath, FileAccessRights.None);
        if (!resolved.IsAllowed || string.IsNullOrEmpty(resolved.ResolvedPath))
            return null;
        return new GitWorktreeBind(dir.VirtualPath, new ConfinedPath(resolved.ResolvedPath, dir.Destination, access));
    }

    /// <summary>The entry <paramref name="name"/> of <paramref name="dir"/>, looked up the way <paramref name="dir"/> is.</summary>
    private static GitDir Child(GitDir dir, string name)
        => new($"{dir.VirtualPath}/{name}", dir.AtHostPath ? Path.Join(dir.Destination, name) : $"{dir.Destination}/{name}", dir.AtHostPath);

    /// <summary>Where the paths of one worktree are looked up: its root and the mount that holds it.</summary>
    private sealed class Context(IFileSystemService fileSystem, string agentRoot, string mountVirtualRoot, string mountPhysicalRoot)
    {
        /// <summary>
        /// The common dir of <paramref name="gitDir"/> when every check of a linked worktree holds,
        /// else the reason it is skipped.
        /// </summary>
        public async Task<(GitDir? Common, string? Reason)> CheckAsync(GitDir gitDir, string dotGit, CancellationToken cancellationToken)
        {
            // The layout git worktree add writes: <common>/worktrees/<name>.
            var parent = Parent(gitDir.VirtualPath);
            if (!string.Equals(LastSegment(parent), "worktrees", StringComparison.Ordinal) || Locate("../..", gitDir) is not { } common)
                return (null, "its gitdir is not in the worktrees directory of a common dir of the mount");

            // The common dir is a prefix of the gitdir: walking the gitdir covers both paths.
            if (!await HasNoLinkAsync(gitDir.VirtualPath, cancellationToken).ConfigureAwait(false))
                return (null, "a component of its gitdir path is a symlink or not a directory");
            if (!await IsWorktreeGitDirAsync(gitDir.VirtualPath, cancellationToken).ConfigureAwait(false))
                return (null, "its gitdir is not the git dir of a linked worktree");
            if (!await IsCommonGitDirAsync(common.VirtualPath, cancellationToken).ConfigureAwait(false))
                return (null, "its common dir is not a git dir");

            if (await ResolveFileAsync($"{gitDir.VirtualPath}/commondir", gitDir, cancellationToken).ConfigureAwait(false) is not { } named
                || !string.Equals(named, common.VirtualPath, StringComparison.Ordinal))
                return (null, "its commondir does not name the common dir that holds it");
            if (await ResolveFileAsync($"{gitDir.VirtualPath}/gitdir", gitDir, cancellationToken).ConfigureAwait(false) is not { } backLink
                || !string.Equals(backLink, dotGit, StringComparison.Ordinal))
                return (null, "the back-link of its gitdir does not name the root's .git");

            return (common, null);
        }

        /// <summary>
        /// Where <paramref name="target"/>, read in a file of <paramref name="from"/>, lies; null when it
        /// leaves the mount, is the mount itself, lies in the root (already bound) or holds the root.
        /// </summary>
        public GitDir? Locate(string target, GitDir from)
        {
            if (Resolve(target, from) is not { } dir
                || string.Equals(dir.VirtualPath, mountVirtualRoot, StringComparison.Ordinal)
                || ShellCommandTool.MatchesMountPrefix(dir.VirtualPath, agentRoot)
                || ShellCommandTool.MatchesMountPrefix(agentRoot, dir.VirtualPath))
                return null;
            return dir;
        }

        /// <summary>
        /// Where <paramref name="target"/>, read in a file of <paramref name="from"/>, lies: relative to
        /// <paramref name="from"/>, a virtual path of the mount, or a host path under the mount's physical
        /// root; null when it leaves the mount.
        /// </summary>
        private GitDir? Resolve(string target, GitDir from)
        {
            if (target.Length == 0 || target.Contains('\0', StringComparison.Ordinal))
                return null;

            GitDir? located;
            if (target.StartsWith('/') && NormalizeVirtual(target) is { } virtualTarget
                && ShellCommandTool.MatchesMountPrefix(virtualTarget, mountVirtualRoot))
                located = new GitDir(virtualTarget, virtualTarget, AtHostPath: false);
            else if (Path.IsPathFullyQualified(target))
                located = FromHostPath(target);
            else if (NormalizeVirtual($"{from.VirtualPath}/{target}") is { } relative)
                located = from.AtHostPath ? AtHostPath(relative) : new GitDir(relative, relative, AtHostPath: false);
            else
                located = null;

            return located is { } dir && ShellCommandTool.MatchesMountPrefix(dir.VirtualPath, mountVirtualRoot) ? dir : null;
        }

        /// <summary>The virtual path the first line of the file at <paramref name="virtualPath"/> names, null when it names none.</summary>
        private async Task<string?> ResolveFileAsync(string virtualPath, GitDir from, CancellationToken cancellationToken)
        {
            var line = FirstLine(await ReadAsync(fileSystem, virtualPath, cancellationToken).ConfigureAwait(false));
            return line is null ? null : Resolve(line.Trim(), from)?.VirtualPath;
        }

        /// <summary>
        /// Whether every component of <paramref name="virtualPath"/> below the mount's root is a
        /// directory, not a symlink: the kind is read without following the last link of each prefix.
        /// </summary>
        private async Task<bool> HasNoLinkAsync(string virtualPath, CancellationToken cancellationToken)
        {
            var prefix = mountVirtualRoot;
            foreach (var segment in virtualPath[mountVirtualRoot.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                prefix = $"{prefix}/{segment}";
                if (await KindAsync(fileSystem, prefix, cancellationToken).ConfigureAwait(false) != VirtualEntryKind.Directory)
                    return false;
            }

            return true;
        }

        /// <summary>A worktree's git dir: a directory holding its own <c>HEAD</c> and a <c>commondir</c> file.</summary>
        private async Task<bool> IsWorktreeGitDirAsync(string virtualPath, CancellationToken cancellationToken)
            => await KindAsync(fileSystem, virtualPath, cancellationToken).ConfigureAwait(false) == VirtualEntryKind.Directory
               && await KindAsync(fileSystem, $"{virtualPath}/HEAD", cancellationToken).ConfigureAwait(false) == VirtualEntryKind.File
               && await KindAsync(fileSystem, $"{virtualPath}/commondir", cancellationToken).ConfigureAwait(false) == VirtualEntryKind.File;

        /// <summary>A repository's git dir: a directory holding <c>HEAD</c> and an <c>objects</c> directory.</summary>
        private async Task<bool> IsCommonGitDirAsync(string virtualPath, CancellationToken cancellationToken)
            => await KindAsync(fileSystem, virtualPath, cancellationToken).ConfigureAwait(false) == VirtualEntryKind.Directory
               && await KindAsync(fileSystem, $"{virtualPath}/HEAD", cancellationToken).ConfigureAwait(false) == VirtualEntryKind.File
               && await KindAsync(fileSystem, $"{virtualPath}/objects", cancellationToken).ConfigureAwait(false) == VirtualEntryKind.Directory;

        /// <summary>
        /// A host path under the mount's physical root, mapped back to its virtual path through that root
        /// and bound at the host path; null when it lies elsewhere. Lexical: nothing on the disk is read.
        /// </summary>
        private GitDir? FromHostPath(string hostPath)
        {
            if (NormalizeHost(hostPath) is not { } full || NormalizeHost(mountPhysicalRoot) is not { } root)
                return null;

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, comparison))
                return null;

            var relative = full[prefix.Length..].Replace(Path.DirectorySeparatorChar, '/');
            return new GitDir($"{mountVirtualRoot}/{relative}", full, AtHostPath: true);
        }

        /// <summary>The virtual path <paramref name="virtualPath"/> of the mount, bound at its host path under the mount's physical root.</summary>
        private GitDir? AtHostPath(string virtualPath)
        {
            if (!ShellCommandTool.MatchesMountPrefix(virtualPath, mountVirtualRoot) || NormalizeHost(mountPhysicalRoot) is not { } root)
                return null;

            var relative = virtualPath[mountVirtualRoot.Length..].TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            return new GitDir(virtualPath, Path.Join(root, relative), AtHostPath: true);
        }
    }

    /// <summary>The virtual path above <paramref name="virtualPath"/>, <c>/</c> at the top.</summary>
    private static string Parent(string virtualPath)
    {
        var index = virtualPath.LastIndexOf('/');
        return index <= 0 ? "/" : virtualPath[..index];
    }

    /// <summary>The last segment of <paramref name="virtualPath"/>.</summary>
    private static string LastSegment(string virtualPath) => virtualPath[(virtualPath.LastIndexOf('/') + 1)..];

    /// <summary>
    /// The host path <paramref name="path"/> without <c>.</c>, <c>..</c> or empty segments, joined by the
    /// platform's separator; null when it is not fully qualified or climbs above its root.
    /// </summary>
    internal static string? NormalizeHost(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Path.GetPathRoot(path) is not { Length: > 0 } root)
            return null;

        var segments = new List<string>();
        foreach (var segment in path[root.Length..].Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    return null;
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        var normalizedRoot = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return normalizedRoot + string.Join(Path.DirectorySeparatorChar, segments);
    }

    /// <summary><paramref name="path"/> without <c>.</c>, <c>..</c> or empty segments; null when it climbs above <c>/</c>.</summary>
    internal static string? NormalizeVirtual(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    return null;
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return "/" + string.Join('/', segments);
    }

    private static string? FirstLine(string? content)
    {
        if (content is null)
            return null;
        var end = content.IndexOf('\n', StringComparison.Ordinal);
        return (end < 0 ? content : content[..end]).TrimEnd('\r');
    }

    /// <summary>
    /// The kind of the entry at <paramref name="virtualPath"/>, null when it is absent or cannot be read.
    /// The VFS reports a link to a file or a directory as <see cref="VirtualEntryKind.SymLink"/> (it reads
    /// the entry's own link target) and a dangling one as absent, so a Directory here is never a link.
    /// </summary>
    private static async Task<VirtualEntryKind?> KindAsync(IFileSystemService fileSystem, string virtualPath, CancellationToken cancellationToken)
    {
        try
        {
            return (await fileSystem.TryGetEntryAsync(virtualPath, cancellationToken).ConfigureAwait(false))?.Kind;
        }
        catch (Exception ex) when (ex is FileAccessDeniedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The text of the file at <paramref name="virtualPath"/>, null when it is absent or cannot be read.</summary>
    private static async Task<string?> ReadAsync(IFileSystemService fileSystem, string virtualPath, CancellationToken cancellationToken)
    {
        try
        {
            return await fileSystem.TryReadAllTextAsync(virtualPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileAccessDeniedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static GitWorktreeBind[] Skipped(ILogger logger, string agentRoot, string reason)
    {
        LogWorktreeSkipped(logger, agentRoot, reason);
        return [];
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent root {AgentRoot} holds a .git file, but no git dir is bound for it: {Reason}")]
    private static partial void LogWorktreeSkipped(ILogger logger, string agentRoot, string reason);
}
