namespace Orkeon.Domain.FileSystem;

/// <summary>
/// The file-system scope of one agent run (#349): the virtual paths its tools may touch, each with the
/// rights the agent narrows the mount's to there. Built once at run start by <see cref="Resolve"/>, which
/// refuses an entry under no mount or asking more than the mount grants; held by the run's
/// <see cref="Execution.AgentRunScope"/> and enforced by the file system on every tool call of the run.
/// </summary>
/// <remarks>
/// <para>
/// A declared scope is exhaustive: a path no entry covers is refused, on every mount. It is an
/// intersection with the mounts, never an extension: the registry keeps checking the mount's rights, and
/// the scope removes what its entries do not grant. The most specific entry covering a path decides there.
/// </para>
/// <para>
/// A checked path is read as the host's file system reads it, and when that reading is ambiguous the
/// scope grants only what every reading grants: off Windows a backslash is part of a name (the registry
/// maps <c>/output/notes\x.md</c> to the file <c>notes\x.md</c> of <c>/output</c>), save right after a
/// mount root, where the registry takes it as a separator; and on a case-insensitive host
/// (<c>/output/REPORT</c> is <c>/output/report</c>, as is <c>/output/report.</c> on Windows) the most
/// specific entry is also sought without case and without trailing dots or spaces. The scope may so refuse
/// a path a case-sensitive host would allow, never the reverse.
/// </para>
/// <para>
/// A scope started under another (a child of <c>spawn_agent</c> or <c>ctx.spawnBackground</c>) keeps its
/// parent: what it grants is also bounded by the parent's, so a child only narrows.
/// </para>
/// </remarks>
public sealed class AgentFileSystemScope
{
    private AgentFileSystemScope(string agentName, IReadOnlyList<FileSystemScopeEntry> entries, AgentFileSystemScope? parent)
    {
        AgentName = agentName;
        Entries = entries;
        Parent = parent;
    }

    /// <summary>The agent the scope belongs to, as denials and the prompt name it.</summary>
    public string AgentName { get; }

    /// <summary>The entries, their paths normalised, sorted by virtual path.</summary>
    public IReadOnlyList<FileSystemScopeEntry> Entries { get; }

    /// <summary>The scope of the agent that started this one, which bounds it; <see langword="null"/> for a top-level agent.</summary>
    public AgentFileSystemScope? Parent { get; }

    /// <summary>
    /// Checks <paramref name="declared"/> against the mounts and builds the scope. Each path must be
    /// absolute, without a <c>..</c> segment, under one of <paramref name="mounts"/>, and appear once; an
    /// <c>access</c> must not ask a right the mount withholds there, nor, under a
    /// <paramref name="parent"/>, a right the parent's scope withholds; under a parent, every path must
    /// lie inside the parent's scope.
    /// </summary>
    /// <param name="agentName">The agent, named by the refusals.</param>
    /// <param name="declared">The declared entries; at least one.</param>
    /// <param name="mounts">The agent-facing mounts of the run (<see cref="IFileSystemService.GetAvailableMounts"/>).</param>
    /// <param name="parent">The scope of the agent that started this one, or <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">An entry is refused; the message names the agent and the entry.</exception>
    public static AgentFileSystemScope Resolve(
        string agentName, IReadOnlyList<FileSystemScopeEntry> declared, IReadOnlyList<MountInfo> mounts, AgentFileSystemScope? parent = null)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(mounts);
        if (declared.Count == 0)
            throw new InvalidOperationException($"The file-system scope of agent '{agentName}' declares no entry: declare at least one, or none at all.");

        var entries = new List<FileSystemScopeEntry>(declared.Count);
        foreach (var entry in declared)
        {
            ArgumentNullException.ThrowIfNull(entry);
            var path = NormalizeDeclared(agentName, entry.Path);
            if (entries.Any(e => e.Path.Equals(path, StringComparison.Ordinal)))
                throw Refused(agentName, entry.Path, "is declared twice");

            if (entry.Access == FileAccessRights.None)
                throw Refused(agentName, entry.Path, "grants no right; leave the path out of the scope instead");
            var mountRights = MountRightsAt(mounts, path)
                ?? throw Refused(agentName, entry.Path, "is not under any mount of the file system");
            if (entry.Access is { } access && (access & ~mountRights) != FileAccessRights.None)
                throw Refused(agentName, entry.Path, $"asks {FormatRights(access)} but the mount grants {FormatRights(mountRights)} there");

            if (parent is not null)
            {
                var parentRights = parent.RightsAt(path);
                if (parentRights == FileAccessRights.None)
                    throw Refused(agentName, entry.Path, $"lies outside the file-system scope of '{parent.AgentName}' that started it");
                if (entry.Access is { } narrowed && (narrowed & ~parentRights) != FileAccessRights.None)
                    throw Refused(agentName, entry.Path, $"asks {FormatRights(narrowed)} but the scope of '{parent.AgentName}' grants {FormatRights(parentRights)} there");
            }

            entries.Add(new FileSystemScopeEntry(path, entry.Access));
        }

        return new AgentFileSystemScope(agentName, [.. entries.OrderBy(e => e.Path, StringComparer.Ordinal)], parent);
    }

    /// <summary>
    /// The rights the scope leaves at <paramref name="virtualPath"/>: those of the most specific entry
    /// covering it (every right when the entry inherits the mount's), bounded by the parent's;
    /// <see cref="FileAccessRights.None"/> when no entry covers it. The mount's own rights are not part
    /// of it: the registry applies them.
    /// </summary>
    /// <param name="virtualPath">An absolute virtual path.</param>
    public FileAccessRights RightsAt(string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(virtualPath);
        var readings = HostReadings(virtualPath);
        var own = readings.Count == 0 ? FileAccessRights.None : FileAccessRights.ReadWrite;
        foreach (var path in readings)
            own &= MostSpecificRights(path, folded: false) & MostSpecificRights(path, folded: true);
        return Parent is null ? own : own & Parent.RightsAt(virtualPath);
    }

    /// <summary>
    /// The rights of the most specific entry covering <paramref name="path"/> (normalised);
    /// <see cref="FileAccessRights.None"/> when none does. <paramref name="folded"/> compares without case
    /// and without trailing dots or spaces in a segment, and intersects the entries that then tie.
    /// </summary>
    private FileAccessRights MostSpecificRights(string path, bool folded)
    {
        var key = folded ? Fold(path) : path;
        var comparison = folded ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var covering = Entries
            .Select(e => (Key: folded ? Fold(e.Path) : e.Path, Rights: e.Access ?? FileAccessRights.ReadWrite))
            .Where(e => Contains(e.Key, key, comparison))
            .ToList();
        if (covering.Count == 0)
            return FileAccessRights.None;
        var longest = covering.Max(e => e.Key.Length);
        return covering.Where(e => e.Key.Length == longest)
            .Aggregate(FileAccessRights.ReadWrite, (rights, e) => rights & e.Rights);
    }

    /// <summary>The most backslashes a checked path may hold off Windows; past it, the path is refused.</summary>
    private const int MaxBackslashReadings = 32;

    /// <summary>
    /// The normalised paths the host may read <paramref name="virtualPath"/> as. On Windows a backslash is
    /// a separator: one reading. Elsewhere it is part of a name, save the one the registry takes as a
    /// separator right after a mount root: the literal reading, plus one per backslash read as the only
    /// separator. Empty (the path is refused) past <see cref="MaxBackslashReadings"/> backslashes.
    /// </summary>
    private static List<string> HostReadings(string virtualPath)
    {
        if (OperatingSystem.IsWindows())
            return [Normalize(virtualPath)];

        var readings = new List<string> { NormalizeLiteral(virtualPath) };
        var backslashes = 0;
        for (var i = virtualPath.IndexOf('\\', StringComparison.Ordinal); i >= 0; i = virtualPath.IndexOf('\\', i + 1))
        {
            if (++backslashes > MaxBackslashReadings)
                return [];
            readings.Add(NormalizeLiteral(string.Concat(virtualPath.AsSpan(0, i), "/", virtualPath.AsSpan(i + 1))));
        }
        return readings;
    }

    /// <summary>A normalised path as a case-insensitive host compares it: each segment without its trailing dots or spaces.</summary>
    private static string Fold(string path)
        => path == "/" ? path : "/" + string.Join('/', path[1..].Split('/').Select(segment =>
        {
            var trimmed = segment.TrimEnd('.', ' ');
            return trimmed.Length == 0 ? segment : trimmed;
        }));

    /// <summary>Whether the scope grants every right of <paramref name="requiredRight"/> at <paramref name="virtualPath"/>.</summary>
    /// <param name="virtualPath">An absolute virtual path.</param>
    /// <param name="requiredRight">The rights the operation needs.</param>
    public bool Allows(string virtualPath, FileAccessRights requiredRight)
        => (RightsAt(virtualPath) & requiredRight) == requiredRight;

    /// <summary>
    /// The refusal of a path the scope does not grant, in the shape of the file system's denials: it
    /// names the agent's scope, never the run's mounts.
    /// </summary>
    /// <param name="virtualPath">The refused path, as the caller wrote it.</param>
    /// <param name="requiredRight">The rights the operation needed.</param>
    public string DescribeDenial(string virtualPath, FileAccessRights requiredRight)
    {
        ArgumentNullException.ThrowIfNull(virtualPath);
        var granted = RightsAt(virtualPath);
        var why = granted == FileAccessRights.None
            ? "is outside the file-system scope"
            : $"needs {requiredRight} but the file-system scope grants {FormatRights(granted)} there";
        return $"Access denied for '{virtualPath}': the path {why} of agent '{AgentName}'. Its scope: {Describe()}.";
    }

    /// <summary>
    /// The refusal of a path the scope grants but the mounts refuse, in the same shape as
    /// <see cref="DescribeDenial"/>: it names the agent's scope and never the run's other mounts.
    /// </summary>
    /// <param name="virtualPath">The refused path, as the caller wrote it.</param>
    /// <param name="requiredRight">The rights the operation needed.</param>
    internal string DescribeMountDenial(string virtualPath, FileAccessRights requiredRight)
        => $"Access denied for '{virtualPath}': the mounts do not grant {requiredRight} there, inside the file-system scope of agent '{AgentName}'. Its scope: {Describe()}.";

    /// <summary>
    /// The scope a run started under <paramref name="parent"/> holds when it declares none (#349): the
    /// parent's entries and rights, under the run's own agent name, so its denials and its prompt name it.
    /// </summary>
    /// <param name="agentName">The agent of the child run.</param>
    /// <param name="parent">The scope of the run that started it.</param>
    internal static AgentFileSystemScope InheritedBy(string agentName, AgentFileSystemScope parent)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        ArgumentNullException.ThrowIfNull(parent);
        return new AgentFileSystemScope(agentName, parent.Entries, parent);
    }

    /// <summary>
    /// The mounts as the agent sees them under this scope: one per entry, at the entry's path, with the
    /// rights in force there (the mount's, narrowed by the scope) and the mount's sub-path overrides below
    /// it, relative to the entry and narrowed the same way. Sorted by virtual path; an entry whose mount
    /// left the run, or that grants nothing, is left out.
    /// </summary>
    /// <param name="mounts">The agent-facing mounts of the run.</param>
    public IReadOnlyList<MountInfo> Narrow(IReadOnlyList<MountInfo> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        var visible = new List<MountInfo>(Entries.Count);
        foreach (var entry in Entries)
        {
            if (MountRightsAt(mounts, entry.Path) is not { } mountRights)
                continue;
            var rights = mountRights & RightsAt(entry.Path);
            if (rights == FileAccessRights.None)
                continue;

            var mount = mounts.Where(m => Contains(Normalize(m.VirtualPath), entry.Path, StringComparison.Ordinal)).MaxBy(m => Normalize(m.VirtualPath).Length)!;
            var mountRoot = Normalize(mount.VirtualPath);
            var overrides = mount.Overrides
                .Select(o => mountRoot == "/" ? Normalize("/" + o.RelativePath) : Normalize(mountRoot + "/" + o.RelativePath))
                .Where(path => path.StartsWith(entry.Path == "/" ? "/" : entry.Path + "/", StringComparison.Ordinal) && path != entry.Path)
                .Distinct(StringComparer.Ordinal)
                .Select(path => new SubPathOverride(
                    path[(entry.Path == "/" ? 1 : entry.Path.Length + 1)..],
                    (MountRightsAt(mounts, path) ?? FileAccessRights.None) & RightsAt(path)))
                .OrderBy(o => o.RelativePath, StringComparer.Ordinal)
                .ToList();
            visible.Add(new MountInfo(entry.Path, rights, overrides));
        }
        return visible;
    }

    /// <summary>The entries as one line: each path, with its <c>access</c> when it narrows the mount's.</summary>
    public string Describe()
        => string.Join(", ", Entries.Select(e => e.Access is { } access ? $"{e.Path} ({FormatRights(access)})" : e.Path));

    /// <summary>
    /// The rights the mounts grant at <paramref name="virtualPath"/>: those of the most specific mount
    /// holding it, its most specific sub-path override included; <see langword="null"/> when no mount holds it.
    /// </summary>
    /// <param name="mounts">The mounts.</param>
    /// <param name="virtualPath">An absolute virtual path.</param>
    public static FileAccessRights? MountRightsAt(IReadOnlyList<MountInfo> mounts, string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        ArgumentNullException.ThrowIfNull(virtualPath);
        var path = Normalize(virtualPath);
        var mount = mounts.Where(m => Contains(Normalize(m.VirtualPath), path, StringComparison.Ordinal)).MaxBy(m => Normalize(m.VirtualPath).Length);
        if (mount is null)
            return null;

        var root = Normalize(mount.VirtualPath);
        var relative = root == "/" ? path.TrimStart('/') : path.Length == root.Length ? "" : path[(root.Length + 1)..];
        var matching = mount.Overrides
            .Where(o => OverrideHolds(o, relative))
            .MaxBy(o => o.RelativePath.Replace('\\', '/').Trim('/').Length);
        return matching?.Rights ?? mount.DefaultRights;
    }

    private static bool OverrideHolds(SubPathOverride subPath, string relative)
    {
        var own = subPath.RelativePath.Replace('\\', '/').Trim('/');
        return own.Length > 0
               && (relative.Equals(own, StringComparison.Ordinal) || relative.StartsWith(own + "/", StringComparison.Ordinal));
    }

    /// <summary>The token of a mount string for the three usual combinations (<c>ro</c>, <c>rw</c>, <c>rwnd</c>), else the enum's spelling.</summary>
    /// <param name="rights">The rights.</param>
    public static string FormatRights(FileAccessRights rights) =>
        rights is FileAccessRights.ReadOnly or FileAccessRights.ReadWrite or FileAccessRights.ReadWriteNoDelete
            ? FileSystemMount.FormatRights(rights)
            : rights.ToString();

    /// <summary>Whether <paramref name="parent"/> holds <paramref name="child"/> (itself included); both normalised.</summary>
    private static bool Contains(string parent, string child, StringComparison comparison) =>
        parent == "/"
        || child.Equals(parent, comparison)
        || child.StartsWith(parent + "/", comparison);

    /// <summary>
    /// A virtual path without <c>//</c>, <c>.</c>, <c>..</c> or trailing <c>/</c>, always starting with <c>/</c>, a
    /// backslash read as a separator: the form of a declared entry and of a mount.
    /// </summary>
    internal static string Normalize(string path) => NormalizeLiteral(path.Replace('\\', '/'));

    /// <summary>As <see cref="Normalize"/>, a backslash kept as part of a name.</summary>
    private static string NormalizeLiteral(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return "/" + string.Join('/', segments);
    }

    private static string NormalizeDeclared(string agentName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"The file-system scope of agent '{agentName}' has an entry without a path.");
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!path.TrimStart().StartsWith('/') || segments.Contains(".."))
            throw Refused(agentName, path, "is not an absolute virtual path without '..'");
        return Normalize(path.Trim());
    }

    private static InvalidOperationException Refused(string agentName, string path, string why)
        => new($"The file-system scope of agent '{agentName}': '{path}' {why}.");
}
