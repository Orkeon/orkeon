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
    /// <param name="virtualPath">The virtual path to resolve.</param>
    /// <param name="requiredRight">The right the caller needs.</param>
    /// <param name="includeInternal">
    /// Whether <see cref="MountVisibility.Internal"/> mounts may be resolved. <b>Default
    /// <see langword="false"/>: Internal is a boundary, not a hiding place.</b>
    /// <para>
    /// It used to be a hiding place. An Internal mount was absent from
    /// <see cref="GetAvailableMounts"/> and resolved for anyone who typed its name — and the
    /// names are documented: <c>/llm-logs</c> holds every prompt and every API response of the
    /// run, so one <c>file_read /llm-logs/llm-exchanges-….jsonl</c> handed an agent the whole
    /// exchange history. Infrastructure that legitimately writes there asks for it explicitly
    /// (<c>PrivilegedFileSystemAccess</c>); everything an agent can reach goes through the
    /// default.
    /// </para>
    /// <para>
    /// A hidden mount that is refused is reported exactly like one that does not exist: saying
    /// "this exists but is not yours" gives away the name the visibility exists to withhold.
    /// </para>
    /// </param>
    public string ResolveAndCheckRights(
        string virtualPath,
        FileAccessRights requiredRight,
        bool includeInternal = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        _lock.EnterReadLock();
        try
        {
            // 1. Find matching mount (longest virtual path prefix with boundary check) — all mounts
            var mount = FindMountUnsafe(virtualPath);

            // An Internal mount is invisible to an unprivileged caller in both directions: it is
            // not listed, and it does not resolve. Falling back to a parent mount here would
            // punch the hole straight back open for a nested Internal mount.
            if (mount is not null && mount.Visibility == MountVisibility.Internal && !includeInternal)
                mount = null;

            if (mount is null)
            {
                // Agent-facing only: a denial message is read by the LLM, so it names exactly
                // what GetAvailableMounts() names (ADR-008). Listing an Internal mount here
                // would hand an agent the one thing its visibility exists to withhold — the
                // exchange-log directory, which holds every prompt and API payload, was
                // advertised as "writable" in this very sentence.
                var visible = _mounts.Where(m => m.Visibility == MountVisibility.AgentFacing).ToList();
                var availableVirtualPaths = visible.Select(m => m.VirtualPath).ToList();
                // Annotate rights so a tool-calling agent can self-correct on the next
                // iteration (e.g. retry a write under the mount marked writable).
                var described = visible.Select(m => m.DefaultRights.HasFlag(FileAccessRights.Write)
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
                    .Where(m => m.Visibility == MountVisibility.AgentFacing
                                && m.DefaultRights.HasFlag(requiredRight))
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
            if (!PhysicalPathContainment.IsUnder(physicalPath, Path.GetFullPath(mount.BasePath)))
            {
                throw new FileAccessDeniedException(
                    $"Access denied for '{virtualPath}': path traversal detected.",
                    virtualPath,
                    requiredRight);
            }

            // 6. An Internal mount is a boundary in PHYSICAL space, not only in the virtual
            // namespace. Refusing the name `/llm-logs` is worth nothing while the same bytes
            // keep a second address through whatever agent-facing mount contains them — and
            // that arrangement is the ordinary one, not a corner case: `--llm-log ./logs`
            // needs no `--allow-external-mounts` precisely because it stays under the working
            // directory, and the working directory is what gets mounted for the agents. The
            // boundary has to be enforced where the physical path is produced, since the
            // physical path is what the caller then opens.
            if (!includeInternal && IsUnderInternalMountUnsafe(physicalPath))
            {
                throw new FileAccessDeniedException(
                    $"Access denied for '{virtualPath}': this path is reserved by the runtime.",
                    virtualPath,
                    requiredRight);
            }

            return physicalPath;
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Converts a physical path back to its virtual path, or null if unmapped.
    /// <paramref name="includeInternal"/> follows <see cref="ResolveAndCheckRights"/>: an
    /// unprivileged caller must not learn an Internal mount's name from a path rewrite either
    /// — the shell tool rewrites physical→virtual in everything it hands back to the model.
    /// </summary>
    public string? ToVirtualPath(string physicalPath, bool includeInternal = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);

        var normalized = Path.GetFullPath(physicalPath);

        _lock.EnterReadLock();
        try
        {
            // _mounts is ordered by VIRTUAL path length, which is what FindMountUnsafe needs
            // going the other way. Coming back, the specificity that decides is the PHYSICAL
            // one, and the two orderings are unrelated: mount /host at /workspace and
            // /host/data at /data, and taking the first list hit hands /host/data/x.md back
            // as "/workspace/data/x.md" — a spelling that re-resolves under a different
            // mount's rights. Pick the longest matching base path, so a nested mount always
            // wins over the mount it sits inside.
            FileSystemMount? best = null;
            var bestBase = string.Empty;

            foreach (var mount in _mounts)
            {
                if (mount.Visibility == MountVisibility.Internal && !includeInternal)
                    continue;

                var normalizedBase = Path.GetFullPath(mount.BasePath);
                if (normalizedBase.Length <= bestBase.Length)
                    continue;

                if (PhysicalPathContainment.IsUnder(normalized, normalizedBase))
                {
                    best = mount;
                    bestBase = normalizedBase;
                }
            }

            if (best is null)
                return null;

            // The same boundary, going the other way. Skipping Internal mounts in the loop
            // above only stops this from ANSWERING "/llm-logs/x"; the parent mount still
            // matches, so it would hand back "/workspace/logs/x" — a spelling that used to
            // resolve. An unprivileged caller gets no address at all for those bytes.
            if (!includeInternal && IsUnderInternalMountUnsafe(normalized))
                return null;

            var trimmedBase = bestBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalized.Length <= trimmedBase.Length)
                return best.VirtualPath;

            var relative = normalized[(trimmedBase.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
            return $"{best.VirtualPath.TrimEnd('/')}/{relative}";
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Is this normalized physical path inside any <see cref="MountVisibility.Internal"/>
    /// mount? Call under the read lock.
    /// </summary>
    private bool IsUnderInternalMountUnsafe(string normalizedPhysicalPath)
    {
        foreach (var mount in _mounts)
        {
            if (mount.Visibility != MountVisibility.Internal)
                continue;

            if (PhysicalPathContainment.IsUnder(normalizedPhysicalPath, Path.GetFullPath(mount.BasePath)))
                return true;
        }

        return false;
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

    /// <summary>
    /// The Internal mounts themselves — base paths included — so a host composing a
    /// per-execution registry can carry them forward.
    /// <para>
    /// <see cref="GetAllMountsInternal"/> answers a different question: it describes mounts for
    /// diagnostics and deliberately drops the base path. Entering a scope REPLACES the mount
    /// set, so an execution built from its own mounts alone loses the exchange log and the
    /// sandbox — and, worse, loses the overlap check that stops them gaining a second address
    /// through one of its own mounts. This is the accessor that lets a caller not do that.
    /// </para>
    /// </summary>
    /// <returns>The Internal mounts, in registration order.</returns>
    public IReadOnlyList<FileSystemMount> GetInternalMounts()
    {
        _lock.EnterReadLock();
        try
        {
            return _mounts
                .Where(m => m.Visibility == MountVisibility.Internal)
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
