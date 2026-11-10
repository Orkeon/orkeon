namespace Orkeon.Domain.FileSystem;

/// <summary>
/// The real location of a physical path: every symbolic link (or Windows junction) on the
/// path is followed, component by component from the root, the way the operating system
/// follows them when the path is opened. A lexical <see cref="Path.GetFullPath(string)"/>
/// answers about spelling only: <c>/mount/a/link/sub/file</c> stays under <c>/mount</c> as
/// written while <c>link</c> may lead anywhere.
/// <para>
/// A component that does not exist ends the resolution and the rest of the path is appended
/// as written: nothing below a missing folder can be a link yet. A dangling link is still
/// followed to its target text, since writing through it creates the target. A component the
/// process cannot inspect is treated like a missing one, for the same reason: opening the
/// path would fail at that component too.
/// </para>
/// <para>
/// The answer holds when it is computed: a link created or replaced between this resolution
/// and the open that follows (a time-of-check to time-of-use race) is not seen.
/// </para>
/// </summary>
internal static class PhysicalRealPath
{
    /// <summary>The bound on links followed, as the Linux kernel's <c>MAXSYMLINKS</c>.</summary>
    private const int MaxLinkHops = 40;

    /// <summary>
    /// Resolves <paramref name="fullPath"/> (already normalized by
    /// <see cref="Path.GetFullPath(string)"/>) to its real path, or <see langword="null"/>
    /// when the links on it form a loop or a chain longer than <see cref="MaxLinkHops"/>.
    /// </summary>
    public static string? Resolve(string fullPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(fullPath);

        var hops = 0;
        var path = fullPath;
        while (true)
        {
            if (TryWalk(ref path, ref hops, out var result))
                return result;
        }
    }

    /// <summary>
    /// Walks <paramref name="path"/> once from its root. Returns true when the walk is over, with the real
    /// path (or null for a loop) in <paramref name="result"/>; false when a link was replaced by its target
    /// in <paramref name="path"/> and the walk must start again.
    /// </summary>
    private static bool TryWalk(ref string path, ref int hops, out string? result)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            result = path;
            return true;
        }

        var components = path[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var i = 0; i < components.Length; i++)
        {
            var candidate = Path.Combine(current, components[i]);
            var linkTarget = TryReadLink(candidate, out var exists);
            if (!exists)
            {
                result = AppendTail(candidate, components, i + 1);
                return true;
            }

            if (linkTarget is null)
            {
                current = candidate;
                continue;
            }

            if (++hops > MaxLinkHops)
            {
                result = null;
                return true;
            }

            // Replace the link by its target and walk the new path again from its root:
            // the target itself may run through other links.
            var target = Path.GetFullPath(linkTarget, current);
            path = AppendTail(target, components, i + 1);
            result = null;
            return false;
        }

        result = current;
        return true;
    }

    private static string AppendTail(string head, string[] components, int from)
    {
        var result = head;
        for (var i = from; i < components.Length; i++)
            result = Path.Combine(result, components[i]);
        return Path.GetFullPath(result);
    }

    /// <summary>
    /// The link text of <paramref name="path"/> when it is a link, else <see langword="null"/>;
    /// <paramref name="exists"/> says whether the entry itself (not its target) is there.
    /// </summary>
    private static string? TryReadLink(string path, out bool exists)
    {
        try
        {
            var info = new FileInfo(path);
            var linkTarget = info.LinkTarget;
            exists = linkTarget is not null || info.Exists || Directory.Exists(path);
            return linkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            exists = false;
            return null;
        }
    }
}
