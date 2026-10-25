using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Code.Confinement;

/// <summary>A git directory bound for a linked-worktree agent root, with the virtual path it was read at.</summary>
/// <param name="VirtualPath">Where the VFS shows the directory or file.</param>
/// <param name="Path">How the confined view binds it.</param>
internal sealed record GitWorktreeBind(string VirtualPath, ConfinedPath Path);

/// <summary>
/// The git directories a linked worktree needs when it is an agent's working root (#188). A root whose
/// <c>.git</c> is a file (<c>gitdir: &lt;path&gt;</c>, written by <c>git worktree add</c>) commits through
/// the worktree's git dir (<c>&lt;main&gt;/.git/worktrees/&lt;name&gt;</c>) and the common dir it names
/// (<c>commondir</c>, normally <c>&lt;main&gt;/.git</c>), both outside the root: the root alone is not a
/// repository. Both are bound, nothing else of the main checkout, so its working tree stays unreachable.
/// <para>
/// The two files are read through the VFS at their virtual paths. A relative path resolves against the
/// directory that holds it, at its virtual path. An absolute one is taken as a virtual path when it lies
/// in the mount, else as a host path, kept only when it lies under the mount's physical root and then
/// bound at that host path (git looks there). Anything that leaves the mount, holds the root, cannot be
/// read or is not a worktree layout is skipped and logged at Debug. The common dir's <c>config</c> and
/// <c>hooks</c> are bound read-only: they name programs git runs on the host for the main checkout.
/// </para>
/// </summary>
internal static partial class GitWorktreeBinds
{
    private const string GitDirPrefix = "gitdir:";

    /// <summary>A git directory: its virtual path, and the path the confined process looks it up at.</summary>
    private readonly record struct GitDir(string VirtualPath, string Destination, bool AtHostPath);

    /// <summary>
    /// The binds the worktree at <paramref name="agentRoot"/> needs, common dir first; empty when the
    /// root is not a linked worktree whose git dirs lie in the mount.
    /// </summary>
    /// <param name="fileSystem">The VFS the files are read through.</param>
    /// <param name="agentRoot">The agent root, a virtual path in the mount, without a trailing slash.</param>
    /// <param name="mountVirtualRoot">The virtual root of the mount that holds the root.</param>
    /// <param name="mountPhysicalRoot">The physical root that mount resolves to.</param>
    /// <param name="rootAccess">How the root is bound; no git dir is bound with more.</param>
    /// <param name="accessAt">How a virtual path is bound by the rights in force there, null when it grants nothing.</param>
    /// <param name="logger">Where a skipped worktree is logged, at Debug.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public static async Task<IReadOnlyList<GitWorktreeBind>> FindAsync(
        IFileSystemService fileSystem,
        string agentRoot,
        string mountVirtualRoot,
        string mountPhysicalRoot,
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

        var context = new Context(fileSystem, agentRoot, mountVirtualRoot, mountPhysicalRoot);
        var link = FirstLine(await ReadAsync(fileSystem, dotGit, cancellationToken).ConfigureAwait(false));
        if (link is null || !link.StartsWith(GitDirPrefix, StringComparison.Ordinal))
            return Skipped(logger, agentRoot, "the .git file names no gitdir");

        var worktreeDir = context.Locate(link[GitDirPrefix.Length..].Trim(), new GitDir(agentRoot, agentRoot, AtHostPath: false));
        if (worktreeDir is not { } gitDir)
            return Skipped(logger, agentRoot, "its gitdir is not a path of the mount outside the root");
        if (!await context.IsWorktreeGitDirAsync(gitDir.VirtualPath, cancellationToken).ConfigureAwait(false))
            return Skipped(logger, agentRoot, "its gitdir is not the git dir of a linked worktree");

        var common = FirstLine(await ReadAsync(fileSystem, $"{gitDir.VirtualPath}/commondir", cancellationToken).ConfigureAwait(false));
        var commonDir = common is null ? null : context.Locate(common.Trim(), gitDir);
        if (commonDir is not { } commonGitDir)
            return Skipped(logger, agentRoot, "its commondir is not a path of the mount outside the root");
        if (!await context.IsCommonGitDirAsync(commonGitDir.VirtualPath, cancellationToken).ConfigureAwait(false))
            return Skipped(logger, agentRoot, "its commondir is not a git dir");

        var binds = new List<GitWorktreeBind>();
        if (Bind(fileSystem, commonGitDir, rootAccess, accessAt) is { } commonBind)
        {
            binds.Add(commonBind);
            if (commonBind.Path.Access == ConfinedAccess.ReadWrite)
                await AddReadOnlyAsync(binds, fileSystem, commonGitDir, ["config", "hooks"], cancellationToken).ConfigureAwait(false);
        }

        if (Bind(fileSystem, gitDir, rootAccess, accessAt) is { } worktreeBind)
        {
            binds.Add(worktreeBind);
            if (worktreeBind.Path.Access == ConfinedAccess.ReadWrite)
                await AddReadOnlyAsync(binds, fileSystem, gitDir, ["config.worktree"], cancellationToken).ConfigureAwait(false);
        }

        return binds;
    }

    /// <summary>The bind of <paramref name="dir"/> with the rights in force there, capped by the root's; null when it grants nothing.</summary>
    private static GitWorktreeBind? Bind(
        IFileSystemService fileSystem, GitDir dir, ConfinedAccess rootAccess, Func<string, ConfinedAccess?> accessAt)
    {
        if (accessAt(dir.VirtualPath) is not { } access)
            return null;
        var resolved = fileSystem.ResolveAndValidate(dir.VirtualPath, FileAccessRights.None);
        if (!resolved.IsAllowed || string.IsNullOrEmpty(resolved.ResolvedPath))
            return null;

        var capped = access == ConfinedAccess.ReadWrite && rootAccess == ConfinedAccess.ReadWrite
            ? ConfinedAccess.ReadWrite
            : ConfinedAccess.ReadOnly;
        return new GitWorktreeBind(dir.VirtualPath, new ConfinedPath(resolved.ResolvedPath, dir.Destination, capped));
    }

    /// <summary>Adds the entries of <paramref name="dir"/> named by <paramref name="names"/> that exist, read-only.</summary>
    private static async Task AddReadOnlyAsync(
        List<GitWorktreeBind> binds, IFileSystemService fileSystem, GitDir dir, string[] names, CancellationToken cancellationToken)
    {
        foreach (var name in names)
        {
            var virtualPath = $"{dir.VirtualPath}/{name}";
            if (await KindAsync(fileSystem, virtualPath, cancellationToken).ConfigureAwait(false) is not (VirtualEntryKind.File or VirtualEntryKind.Directory))
                continue;
            var resolved = fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.None);
            if (resolved.IsAllowed && !string.IsNullOrEmpty(resolved.ResolvedPath))
                binds.Add(new GitWorktreeBind(virtualPath, new ConfinedPath(resolved.ResolvedPath, $"{dir.Destination}/{name}", ConfinedAccess.ReadOnly)));
        }
    }

    /// <summary>Where the paths of one worktree are looked up: its root and the mount that holds it.</summary>
    private sealed class Context(IFileSystemService fileSystem, string agentRoot, string mountVirtualRoot, string mountPhysicalRoot)
    {
        /// <summary>
        /// Where <paramref name="target"/>, read in a file of <paramref name="from"/>, lies; null when it
        /// leaves the mount, is the mount itself, lies in the root (already bound) or holds the root.
        /// </summary>
        public GitDir? Locate(string target, GitDir from)
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

            if (located is not { } dir
                || !ShellCommandTool.MatchesMountPrefix(dir.VirtualPath, mountVirtualRoot)
                || string.Equals(dir.VirtualPath, mountVirtualRoot, StringComparison.Ordinal)
                || ShellCommandTool.MatchesMountPrefix(dir.VirtualPath, agentRoot)
                || ShellCommandTool.MatchesMountPrefix(agentRoot, dir.VirtualPath))
                return null;
            return dir;
        }

        /// <summary>A worktree's git dir: a directory holding its own <c>HEAD</c> and a <c>commondir</c> file.</summary>
        public async Task<bool> IsWorktreeGitDirAsync(string virtualPath, CancellationToken cancellationToken)
            => await KindAsync(fileSystem, virtualPath, cancellationToken).ConfigureAwait(false) == VirtualEntryKind.Directory
               && await KindAsync(fileSystem, $"{virtualPath}/HEAD", cancellationToken).ConfigureAwait(false) == VirtualEntryKind.File
               && await KindAsync(fileSystem, $"{virtualPath}/commondir", cancellationToken).ConfigureAwait(false) == VirtualEntryKind.File;

        /// <summary>A repository's git dir: a directory holding <c>HEAD</c> and an <c>objects</c> directory.</summary>
        public async Task<bool> IsCommonGitDirAsync(string virtualPath, CancellationToken cancellationToken)
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

    /// <summary>The kind of the entry at <paramref name="virtualPath"/>, null when it is absent or cannot be read.</summary>
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
