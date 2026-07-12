namespace Orkeon.Domain.FileSystem;

/// <summary>Registry of virtual file system mounts with access control resolution.</summary>
public sealed class FileSystemRegistry : IDisposable
{
    private readonly List<FileSystemMount> _mounts;
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>Initializes the registry with the specified mounts.</summary>
    public FileSystemRegistry(IEnumerable<FileSystemMount> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);

        var mountList = mounts.ToList();

        // Check for duplicate virtual paths
        var duplicates = mountList
            .GroupBy(m => m.VirtualPath, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate virtual paths: {string.Join(", ", duplicates)}");

        // Sort by VirtualPath length descending for longest prefix match
        mountList.Sort((a, b) => b.VirtualPath.Length.CompareTo(a.VirtualPath.Length));
        _mounts = mountList;
    }

    /// <summary>Adds a mount at runtime (e.g. from a hosted service bootstrapper).</summary>
    public void AddMount(FileSystemMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        _lock.EnterWriteLock();
        try
        {
            if (_mounts.Any(m => string.Equals(m.VirtualPath, mount.VirtualPath, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Mount '{mount.VirtualPath}' already registered");

            _mounts.Add(mount);
            _mounts.Sort((a, b) => b.VirtualPath.Length.CompareTo(a.VirtualPath.Length));
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Resolves a virtual path to a physical path after verifying access rights.</summary>
    public string ResolveAndCheckRights(string virtualPath, FileAccessRights requiredRight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        _lock.EnterReadLock();
        try
        {
            // 1. Find matching mount (longest virtual path prefix with boundary check) — all mounts
            var mount = FindMountUnsafe(virtualPath);

            if (mount is null)
            {
                var availableVirtualPaths = _mounts.Select(m => m.VirtualPath).ToList();
                // Annotate rights so a tool-calling agent can self-correct on the next
                // iteration (e.g. retry a write under the mount marked writable).
                var described = _mounts.Select(m => m.DefaultRights.HasFlag(FileAccessRights.Write)
                    ? $"{m.VirtualPath} (writable)"
                    : $"{m.VirtualPath} (read-only)");
                throw new FileAccessDeniedException(
                    $"No mount found for virtual path '{virtualPath}'. Available mounts: {string.Join(", ", described)}",
                    virtualPath,
                    requiredRight,
                    availableVirtualPaths);
            }

            // 2. Extract relative path
            var relativePath = ExtractRelativePath(virtualPath, mount.VirtualPath);

            // 3. Check rights
            var effectiveRights = mount.ResolveRights(relativePath);
            if (!effectiveRights.HasFlag(requiredRight))
            {
                var granting = _mounts
                    .Where(m => m.DefaultRights.HasFlag(requiredRight))
                    .Select(m => m.VirtualPath)
                    .ToList();
                throw new FileAccessDeniedException(
                    $"Access denied for '{virtualPath}': required {requiredRight}, effective {effectiveRights}. " +
                    $"Mounts granting {requiredRight}: {(granting.Count > 0 ? string.Join(", ", granting) : "(none)")}.",
                    virtualPath,
                    requiredRight);
            }

            // 4. Build physical path
            var physicalPath = Path.GetFullPath(Path.Combine(mount.BasePath, relativePath));

            // 5. Containment check
            var normalizedBase = Path.GetFullPath(mount.BasePath);
            if (!string.Equals(physicalPath, normalizedBase, StringComparison.Ordinal) &&
                !physicalPath.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new FileAccessDeniedException(
                    $"Access denied for '{virtualPath}': path traversal detected.",
                    virtualPath,
                    requiredRight);
            }

            return physicalPath;
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Converts a physical path back to its virtual path, or null if unmapped.</summary>
    public string? ToVirtualPath(string physicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);

        var normalized = Path.GetFullPath(physicalPath);

        _lock.EnterReadLock();
        try
        {
            foreach (var mount in _mounts)
            {
                var normalizedBase = Path.GetFullPath(mount.BasePath);
                var sep = Path.DirectorySeparatorChar;

                if (string.Equals(normalized, normalizedBase, StringComparison.Ordinal))
                    return mount.VirtualPath;

                if (normalized.StartsWith(normalizedBase + sep, StringComparison.Ordinal))
                {
                    var relative = normalized[(normalizedBase.Length + 1)..];
                    var virtualRelative = relative.Replace(sep, '/');
                    var vp = mount.VirtualPath.TrimEnd('/');
                    return $"{vp}/{virtualRelative}";
                }
            }
        }
        finally { _lock.ExitReadLock(); }

        return null;
    }

    /// <summary>Returns AgentFacing mounts only — Internal mounts are hidden from agents.</summary>
    public IReadOnlyList<MountInfo> GetAvailableMounts()
    {
        _lock.EnterReadLock();
        try
        {
            return _mounts
                .Where(m => m.Visibility == MountVisibility.AgentFacing)
                .Select(m => new MountInfo(m.VirtualPath, m.DefaultRights, m.Overrides, m.Visibility))
                .ToList()
                .AsReadOnly();
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Returns all mounts regardless of visibility — for internal/diagnostic use.</summary>
    public IReadOnlyList<MountInfo> GetAllMountsInternal()
    {
        _lock.EnterReadLock();
        try
        {
            return _mounts
                .Select(m => new MountInfo(m.VirtualPath, m.DefaultRights, m.Overrides, m.Visibility))
                .ToList()
                .AsReadOnly();
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    private FileSystemMount? FindMountUnsafe(string virtualPath)
    {
        foreach (var mount in _mounts)
        {
            // Exact match
            if (string.Equals(virtualPath, mount.VirtualPath, StringComparison.Ordinal))
                return mount;

            // Sub-path match: accept BOTH separators so Windows-style virtual
            // paths (C:\foo\bar.txt) match a Windows-rooted mount (C:\foo).
            if (virtualPath.StartsWith(mount.VirtualPath + "/", StringComparison.Ordinal) ||
                virtualPath.StartsWith(mount.VirtualPath + "\\", StringComparison.Ordinal))
            {
                return mount;
            }
        }
        return null;
    }

    private static string ExtractRelativePath(string virtualPath, string mountVirtualPath)
    {
        if (string.Equals(virtualPath, mountVirtualPath, StringComparison.Ordinal))
            return string.Empty;

        return virtualPath[(mountVirtualPath.Length + 1)..];
    }
}
