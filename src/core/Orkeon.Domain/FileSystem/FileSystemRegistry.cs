namespace Orkeon.Domain.FileSystem;

/// <summary>Registry of virtual file system mounts with access control resolution.</summary>
public sealed class FileSystemRegistry : IDisposable
{
    private readonly List<FileSystemMount> _mounts;
    private readonly ReaderWriterLockSlim _lock = new();
    private string? _workingDirectory;
    private string? _writeDirectory;

    /// <summary>
    /// The virtual root a registry takes as its working directory when nobody designated one and
    /// an agent-facing mount claims it. Same spelling as <c>RunnerVirtualRoots.Workspace</c>,
    /// which the domain cannot reference (ADR-009); a test pins the two together.
    /// </summary>
    public const string DefaultWorkingDirectory = "/workspace";

    /// <summary>
    /// The virtual root a registry takes as its write directory when nobody designated one and an
    /// agent-facing mount claims it with the Write right. Same spelling as
    /// <c>RunnerVirtualRoots.Output</c>, which the domain cannot reference (ADR-009).
    /// </summary>
    public const string DefaultWriteDirectory = "/output";

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

        // An id names one entry (VFS-90): two mounts carrying the same one is a settings
        // file that copied a line, and every reference to that id would be ambiguous.
        var duplicateIds = mountList
            .Where(m => m.Id is not null)
            .GroupBy(m => m.Id!.ToString(), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate mount ids: {string.Join(", ", duplicateIds)}");

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
            if (mount.Id is not null && _mounts.Any(m => mount.Id.Equals(m.Id)))
                throw new InvalidOperationException($"Mount id '{mount.Id}' already registered");

            _mounts.Add(mount);
            _mounts.Sort((a, b) => b.VirtualPath.Length.CompareTo(a.VirtualPath.Length));
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// The virtual directory a relative path is resolved against, and the one a shell command
    /// runs in when it names none: the directory designated by <see cref="SetWorkingDirectory"/>
    /// while its mount is still registered, else <see cref="DefaultWorkingDirectory"/> when an
    /// agent-facing mount claims it, else <see langword="null"/> (no working directory: a relative
    /// path is refused).
    /// </summary>
    public string? WorkingDirectory
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                if (_workingDirectory is not null && FindAgentFacingMountUnsafe(_workingDirectory) is not null)
                    return _workingDirectory;
                return FindAgentFacingMountUnsafe(DefaultWorkingDirectory) is { } workspace
                       && string.Equals(workspace.VirtualPath, DefaultWorkingDirectory, StringComparison.Ordinal)
                    ? DefaultWorkingDirectory
                    : null;
            }
            finally { _lock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// Designates the working directory (see <see cref="WorkingDirectory"/>). A host calls it when
    /// the mount its agents work in is not <see cref="DefaultWorkingDirectory"/>.
    /// </summary>
    /// <param name="virtualPath">
    /// An absolute virtual path inside an agent-facing mount: a working directory an agent cannot
    /// address would hand it relative paths it could never spell back.
    /// </param>
    /// <exception cref="ArgumentException">The path is not absolute, holds a <c>.</c> or <c>..</c> segment, or no agent-facing mount holds it.</exception>
    public void SetWorkingDirectory(string virtualPath)
    {
        var normalized = NormalizeDesignatedDirectory(virtualPath, "working", DefaultWorkingDirectory);

        _lock.EnterWriteLock();
        try
        {
            if (FindAgentFacingMountUnsafe(normalized) is null)
                throw new ArgumentException(
                    $"No agent-facing mount holds the working directory '{virtualPath}'.",
                    nameof(virtualPath));
            _workingDirectory = normalized;
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// The virtual directory a writing shell command runs in when it names no directory, runs
    /// under no agent root, and the <see cref="WorkingDirectory"/> does not grant it the rights it
    /// needs (a read-only <c>/workspace</c> beside a writable <c>/output</c>): the directory
    /// designated by <see cref="SetWriteDirectory"/> while its mount is still registered and
    /// writable, else <see cref="DefaultWriteDirectory"/> when an agent-facing mount claims it with
    /// the Write right, else <see langword="null"/>. Never chosen from the order of the mounts.
    /// </summary>
    public string? WriteDirectory
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                if (_writeDirectory is not null && FindWritableAgentFacingMountUnsafe(_writeDirectory) is not null)
                    return _writeDirectory;
                return FindWritableAgentFacingMountUnsafe(DefaultWriteDirectory) is { } output
                       && string.Equals(output.VirtualPath, DefaultWriteDirectory, StringComparison.Ordinal)
                    ? DefaultWriteDirectory
                    : null;
            }
            finally { _lock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// Designates the write directory (see <see cref="WriteDirectory"/>). A host calls it when the
    /// writable mount its agents produce in is not <see cref="DefaultWriteDirectory"/>.
    /// </summary>
    /// <param name="virtualPath">
    /// An absolute virtual path, without a <c>.</c> or <c>..</c> segment, inside an agent-facing
    /// mount whose effective rights on that path (subtree overrides included) grant Write.
    /// </param>
    /// <exception cref="ArgumentException">The path is not absolute, holds a <c>.</c> or <c>..</c> segment, or no agent-facing mount grants Write on it.</exception>
    public void SetWriteDirectory(string virtualPath)
    {
        var normalized = NormalizeDesignatedDirectory(virtualPath, "write", DefaultWriteDirectory);

        _lock.EnterWriteLock();
        try
        {
            if (FindWritableAgentFacingMountUnsafe(normalized) is null)
                throw new ArgumentException(
                    $"No agent-facing mount grants Write on the write directory '{virtualPath}'.",
                    nameof(virtualPath));
            _writeDirectory = normalized;
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
                // The hint shows the shape of an address: a model that wrote a path relative to the
                // project (src/stats.js) learns how to spell it, not only where it could go.
                var example = visible.FirstOrDefault(m => m.VirtualPath == DefaultWorkingDirectory)?.VirtualPath
                              ?? visible.LastOrDefault()?.VirtualPath
                              ?? DefaultWorkingDirectory;
                throw new FileAccessDeniedException(
                    $"No mount found for virtual path '{virtualPath}': a path starts with a mount: {example.TrimEnd('/')}/src/stats.js. " +
                    $"Available mounts: {string.Join(", ", described)}",
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
                .Select(m => new MountInfo(m.VirtualPath, m.DefaultRights, m.Overrides, m.Visibility) { Id = m.Id })
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
                .Select(m => new MountInfo(m.VirtualPath, m.DefaultRights, m.Overrides, m.Visibility) { Id = m.Id })
                .ToList()
                .AsReadOnly();
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// The mounts themselves - base paths included - so a host composing a per-execution
    /// registry can carry the boot set forward.
    /// <para>
    /// <see cref="GetAllMountsInternal"/> answers a different question: it describes mounts for
    /// diagnostics and deliberately drops the base path, which makes it useless for rebuilding
    /// a registry. This is the accessor that is not.
    /// </para>
    /// <para>
    /// Entering a scope REPLACES the mount set, so an execution built from its own mounts alone
    /// loses every boot mount: the exchange log and the sandbox, the overlap check that stops
    /// them gaining a second agent-reachable address, and the agent-facing roots the runner
    /// needs to load the execution's own definition through the VFS.
    /// </para>
    /// </summary>
    /// <returns>Every mount, in registration order.</returns>
    public IReadOnlyList<FileSystemMount> GetMounts()
    {
        _lock.EnterReadLock();
        try
        {
            return _mounts.ToList().AsReadOnly();
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

    private FileSystemMount? FindAgentFacingMountUnsafe(string virtualPath) =>
        FindMountUnsafe(virtualPath) is { Visibility: MountVisibility.AgentFacing } mount ? mount : null;

    /// <summary>
    /// The agent-facing mount holding <paramref name="virtualPath"/> when the effective rights of
    /// that exact path grant Write: a subtree override that removes Write (<c>/output/ro</c>)
    /// counts, as it does when the path is resolved.
    /// </summary>
    private FileSystemMount? FindWritableAgentFacingMountUnsafe(string virtualPath) =>
        FindAgentFacingMountUnsafe(virtualPath) is { } mount
        && mount.ResolveRights(ExtractRelativePath(virtualPath, mount.VirtualPath)).HasFlag(FileAccessRights.Write)
            ? mount
            : null;

    /// <summary>
    /// The spelling a designated directory is kept under: absolute, without a trailing slash, and
    /// without a <c>.</c> or <c>..</c> segment. Such a segment is refused rather than resolved: the
    /// registry checks a designation against its mounts, and resolution would refuse
    /// <c>/output/../workspace</c> as a traversal at every command after accepting it here.
    /// </summary>
    private static string NormalizeDesignatedDirectory(string virtualPath, string kind, string example)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        var normalized = virtualPath.Length > 1 ? virtualPath.TrimEnd('/') : virtualPath;
        if (!normalized.StartsWith('/'))
            throw new ArgumentException(
                $"A {kind} directory is an absolute virtual path, e.g. {example}; '{virtualPath}' is not.",
                nameof(virtualPath));
        if (normalized.Replace('\\', '/').Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException(
                $"A {kind} directory names its directory without '.' or '..' segments, e.g. {example}; '{virtualPath}' does not.",
                nameof(virtualPath));
        return normalized;
    }

    private static string ExtractRelativePath(string virtualPath, string mountVirtualPath)
    {
        if (string.Equals(virtualPath, mountVirtualPath, StringComparison.Ordinal))
            return string.Empty;

        return virtualPath[(mountVirtualPath.Length + 1)..];
    }
}
