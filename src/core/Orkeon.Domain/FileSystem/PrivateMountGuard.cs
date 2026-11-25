namespace Orkeon.Domain.FileSystem;

/// <summary>
/// Keeps the content of a private mount (<see cref="FileSystemMount.IsPrivate"/>) out of what outlives its execution or is
/// shared with others: a code index, a document store, a journal. A host that has no private mount refuses nothing here.
/// </summary>
public static class PrivateMountGuard
{
    /// <summary>
    /// Refuses a source of a shared store that is a private mount, is under one, or holds one.
    /// </summary>
    /// <param name="fileSystem">The file system, in the mount set of the execution.</param>
    /// <param name="virtualPath">The file or the folder about to be read.</param>
    /// <exception cref="FileAccessDeniedException">The source reaches a private mount.</exception>
    public static void ThrowIfReached(IFileSystemService fileSystem, string virtualPath)
    {
        if (Reached(fileSystem, virtualPath) is { } mount)
        {
            throw new FileAccessDeniedException(
                $"Access denied for '{virtualPath}': the files of '{mount}' belong to this conversation and are not kept in an index or a store other conversations read. Read them with a file tool.",
                virtualPath,
                FileAccessRights.Read);
        }
    }

    /// <summary>The private mount <paramref name="virtualPath"/> is, is under or holds; null when there is none.</summary>
    /// <param name="fileSystem">The file system, in the mount set of the execution.</param>
    /// <param name="virtualPath">The file or the folder.</param>
    public static string? Reached(IFileSystemService fileSystem, string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(virtualPath);

        var mounts = fileSystem.GetAvailableMounts();
        if (!mounts.Any(m => m.IsPrivate))
            return null;

        var path = Normalize(virtualPath, fileSystem.WorkingDirectory);
        return mounts
            .Where(m => m.IsPrivate)
            .Select(m => m.VirtualPath)
            .FirstOrDefault(mount => Holds(mount, path) || Holds(path, mount));
    }

    // The case is ignored: this refuses, and a file system may ignore it.
    private static bool Holds(string folder, string path)
    {
        var root = folder.TrimEnd('/');
        return path.Equals(root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string virtualPath, string? workingDirectory)
    {
        var path = virtualPath.Replace('\\', '/');
        if (!path.StartsWith('/'))
            path = $"{workingDirectory}/{path}";

        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        return "/" + string.Join('/', segments);
    }
}
