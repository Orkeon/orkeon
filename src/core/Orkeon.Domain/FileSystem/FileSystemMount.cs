namespace Orkeon.Domain.FileSystem;

/// <summary>Maps a physical directory to a virtual path with access rights.</summary>
public sealed record FileSystemMount
{
    /// <summary>Physical base path on disk.</summary>
    public string BasePath { get; }
    /// <summary>Virtual path prefix used to reference this mount.</summary>
    public string VirtualPath { get; }
    /// <summary>Default access rights for the mount.</summary>
    public FileAccessRights DefaultRights { get; }
    /// <summary>Sub-path access right overrides, sorted by path length descending.</summary>
    public IReadOnlyList<SubPathOverride> Overrides { get; }
    /// <summary>Controls visibility in GetAvailableMounts(); Internal mounts are hidden from agents.</summary>
    public MountVisibility Visibility { get; }

    /// <summary>Creates a new file system mount with the specified parameters.</summary>
    public FileSystemMount(
        string basePath,
        string virtualPath,
        FileAccessRights defaultRights,
        IReadOnlyList<SubPathOverride>? overrides = null,
        MountVisibility visibility = MountVisibility.AgentFacing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        if (!IsValidVirtualPath(virtualPath))
            throw new FormatException(
                $"Virtual path must start with '/': '{virtualPath}'. A physical path is never a virtual path — mount it under a name, e.g. '{virtualPath}:/workspace:ro'.");

        BasePath = basePath;
        VirtualPath = virtualPath;
        DefaultRights = defaultRights;
        Overrides = overrides is { Count: > 0 }
            ? overrides
                .OrderByDescending(o => o.RelativePath.Length)
                .ToList()
                .AsReadOnly()
            : Array.Empty<SubPathOverride>();
        Visibility = visibility;
    }

    /// <summary>
    /// The escape character of the mount-string grammar. <c>\:</c> is a literal colon,
    /// <c>\;</c> a literal semicolon, <c>\\</c> a literal backslash; a backslash before
    /// anything else is itself literal, so ordinary Windows paths (<c>C:\src\sub</c>) need no
    /// escaping at all.
    /// </summary>
    public const char EscapeCharacter = '\\';

    /// <summary>Parses a mount definition string into a <see cref="FileSystemMount"/> instance.</summary>
    public static FileSystemMount Parse(string mountString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountString);

        // Split into main part and override parts (separated by ';')
        var segments = SplitUnescaped(mountString, ';');
        var mainPart = segments[0];

        // Split main part into basePath, virtualPath, rights
        var parts = SplitMainPart(mainPart);
        if (parts.Count != 3)
            throw new FormatException(
                $"Invalid mount format. Expected '<physical>:<virtual>:<rights>', got: '{mainPart}'. "
                + $"A path containing a ':' or a ';' escapes it as '{EscapeCharacter}:' / '{EscapeCharacter};'.");

        var basePath = Unescape(parts[0]);
        var virtualPath = Unescape(parts[1]);
        var defaultRights = ParseRights(parts[2]);

        if (!IsValidVirtualPath(virtualPath))
            throw new FormatException(
                $"Virtual path must start with '/': '{virtualPath}'. A physical path is never a virtual path — mount it under a name, e.g. '{Escape(basePath)}:/workspace:ro'.");

        // Parse overrides
        var overrides = new List<SubPathOverride>();
        for (var i = 1; i < segments.Count; i++)
        {
            var overrideParts = SplitUnescaped(segments[i], ':');
            if (overrideParts.Count != 2)
                throw new FormatException(
                    $"Invalid override format. Expected '<subpath>:<rights>', got: '{segments[i]}'");

            overrides.Add(new SubPathOverride(Unescape(overrideParts[0]), ParseRights(overrideParts[1])));
        }

        return new FileSystemMount(basePath, virtualPath, defaultRights, overrides);
    }

    /// <summary>
    /// The physical base path a mount string declares, unescaped — or <see langword="null"/>
    /// when the string is not a well-formed mount.
    /// <para>
    /// The one place that answers "which folder does this spec mount?". Splitting a spec on its
    /// first <c>':'</c> is wrong on Windows (<c>C:\src:/workspace:ro</c> yields <c>C</c>), and
    /// three call sites used to re-invent the disambiguation with three different heuristics —
    /// one of which was simply broken. Ask this instead.
    /// </para>
    /// </summary>
    public static string? TryGetBasePath(string mountString)
    {
        if (string.IsNullOrWhiteSpace(mountString))
            return null;

        var mainPart = SplitUnescaped(mountString, ';')[0];
        var parts = SplitMainPart(mainPart);
        return parts.Count >= 2 ? Unescape(parts[0]) : null;
    }

    /// <summary>
    /// The same mount string with its physical segment replaced by <paramref name="basePath"/>,
    /// escaped as the grammar requires. Used by the bootstrappers that resolve a user-supplied
    /// relative mount to an absolute one before handing it to <see cref="Parse"/>.
    /// </summary>
    /// <exception cref="FormatException">The string is not a well-formed mount.</exception>
    public static string WithBasePath(string mountString, string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountString);
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        var segments = SplitUnescaped(mountString, ';');
        var parts = SplitMainPart(segments[0]);
        if (parts.Count < 2)
            throw new FormatException(
                $"Invalid mount format. Expected '<physical>:<virtual>:<rights>', got: '{segments[0]}'.");

        parts[0] = Escape(basePath);
        segments[0] = string.Join(':', parts);
        return string.Join(';', segments);
    }

    /// <summary>
    /// Escapes the separators of the mount-string grammar in a path so it survives a round trip
    /// through <see cref="Parse"/>. A backslash is only escaped when it would otherwise turn the
    /// following separator into a literal one.
    /// </summary>
    public static string Escape(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var builder = new System.Text.StringBuilder(segment.Length);

        // A leading drive letter is left as it reads: the parser recognises "X:\" and "X:/"
        // natively, so escaping it would only make the common Windows spec harder to read.
        var start = HasDriveLetterPrefix(segment) ? 2 : 0;
        if (start == 2)
            builder.Append(segment[0]).Append(':');

        for (var i = start; i < segment.Length; i++)
        {
            var current = segment[i];
            var needsEscape = current is ':' or ';'
                || (current == EscapeCharacter && i + 1 < segment.Length && IsEscapable(segment[i + 1]));

            if (needsEscape)
                builder.Append(EscapeCharacter);
            builder.Append(current);
        }

        return builder.ToString();
    }

    private static bool HasDriveLetterPrefix(string value) =>
        value.Length >= 3
        && char.IsLetter(value[0])
        && value[1] == ':'
        && (value[2] == '\\' || value[2] == '/');

    private static bool IsEscapable(char c) => c is ':' or ';' or EscapeCharacter;

    /// <summary>
    /// Splits on unescaped occurrences of <paramref name="separator"/>. Escape sequences are
    /// carried through verbatim — unescaping happens once, per segment, after every split.
    /// </summary>
    private static List<string> SplitUnescaped(string value, char separator)
    {
        var parts = new List<string>();
        var builder = new System.Text.StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current == EscapeCharacter && i + 1 < value.Length && IsEscapable(value[i + 1]))
            {
                builder.Append(current).Append(value[i + 1]);
                i++;
                continue;
            }

            if (current == separator)
            {
                parts.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        parts.Add(builder.ToString());
        return parts;
    }

    /// <summary>Resolves the escape sequences of one already-split segment.</summary>
    private static string Unescape(string segment)
    {
        if (!segment.Contains(EscapeCharacter, StringComparison.Ordinal))
            return segment;

        var builder = new System.Text.StringBuilder(segment.Length);
        for (var i = 0; i < segment.Length; i++)
        {
            if (segment[i] == EscapeCharacter && i + 1 < segment.Length && IsEscapable(segment[i + 1]))
            {
                builder.Append(segment[i + 1]);
                i++;
                continue;
            }

            builder.Append(segment[i]);
        }

        return builder.ToString();
    }

    /// <summary>Resolves the effective access rights for a relative path within this mount.</summary>
    public FileAccessRights ResolveRights(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        // Normalize: replace backslash with forward slash, trim trailing slash
        var normalized = relativePath.Replace('\\', '/').TrimEnd('/');

        foreach (var ov in Overrides) // Already sorted by length descending
        {
            // Boundary check: exact match or starts with override + "/"
            if (string.Equals(normalized, ov.RelativePath, StringComparison.Ordinal) ||
                normalized.StartsWith(ov.RelativePath + "/", StringComparison.Ordinal))
            {
                return ov.Rights;
            }
        }

        return DefaultRights;
    }

    /// <summary>
    /// True only for Unix-style virtual paths (<c>/foo</c>). A physical path is never a
    /// virtual path (ADR-008): the drive-letter form used to be accepted so runners could
    /// identity-map their own directories, and that is exactly how absolute disk paths
    /// reached agents through <c>list_mounts</c> and access-denied messages. Runners now
    /// mount under a name (<c>/crew</c>, <c>/crews</c>, <c>/script</c>).
    /// </summary>
    private static bool IsValidVirtualPath(string virtualPath)
        => virtualPath.StartsWith('/');

    private static List<string> SplitMainPart(string mainPart)
    {
        // Iteratively peel off path/rights segments, skipping escaped separators. At each step,
        // a Windows drive-letter prefix ("X:\" or "X:/") is also recognised so its embedded ':'
        // is not treated as a separator — that convenience is why "C:\src:/virtual:ro" needs no
        // escaping. Anything else ambiguous is the author's to disambiguate with '\:'.
        // The scan stays segment-agnostic: a drive letter on the virtual side splits cleanly and
        // is then rejected by IsValidVirtualPath with a precise message, rather than failing
        // here as a malformed mount string.
        var parts = new List<string>();
        var rest = mainPart;

        while (rest.Length > 0)
        {
            // For a Windows-path segment, start looking after the drive-letter colon.
            var nextColon = IndexOfUnescaped(rest, ':', HasDriveLetterPrefix(rest) ? 2 : 0);

            if (nextColon < 0)
            {
                parts.Add(rest);
                break;
            }

            parts.Add(rest[..nextColon]);
            rest = rest[(nextColon + 1)..];
        }

        return parts;
    }

    /// <summary>Index of the first unescaped <paramref name="separator"/> at or after <paramref name="startIndex"/>.</summary>
    private static int IndexOfUnescaped(string value, char separator, int startIndex)
    {
        for (var i = startIndex; i < value.Length; i++)
        {
            if (value[i] == EscapeCharacter && i + 1 < value.Length && IsEscapable(value[i + 1]))
            {
                i++;
                continue;
            }

            if (value[i] == separator)
                return i;
        }

        return -1;
    }

    private static FileAccessRights ParseRights(string rights)
    {
#pragma warning disable CA1308 // lowercase is the normalized form matched by the switch arms (rights tokens), not a comparison normalization
        return rights.Trim().ToLowerInvariant() switch
        {
#pragma warning restore CA1308
            "ro" => FileAccessRights.ReadOnly,
            "rw" => FileAccessRights.ReadWrite,
            "rwnd" => FileAccessRights.ReadWriteNoDelete,
            _ => throw new FormatException($"Unknown rights value: '{rights}'. Valid values: ro, rw, rwnd")
        };
    }
}
