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
    /// Wraps a segment of a mount string so its content is taken literally. A path that
    /// contains a <c>:</c> or a <c>;</c>, or that ends with a backslash, is quoted rather than
    /// escaped — a backslash escape would collide with the Windows path separator, which is the
    /// very thing that has to survive here. A literal quote inside a quoted segment is doubled.
    /// </summary>
    public const char QuoteCharacter = '"';

    /// <summary>Parses a mount definition string into a <see cref="FileSystemMount"/> instance.</summary>
    public static FileSystemMount Parse(string mountString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountString);

        // Split into main part and override parts (separated by ';')
        var segments = SplitOutsideQuotes(mountString, ';');
        var mainPart = segments[0];

        // Split main part into basePath, virtualPath, rights
        var parts = SplitMainPart(mainPart);
        if (parts.Count != 3)
            throw new FormatException(
                $"Invalid mount format. Expected '<physical>:<virtual>:<rights>', got: '{mainPart}'. "
                + "A path that contains a ':' or a ';', or ends with a backslash, is quoted: "
                + "\"C:\\src\\\":/workspace:ro.");

        var basePath = Unquote(parts[0]);
        var virtualPath = Unquote(parts[1]);
        var defaultRights = ParseRights(parts[2]);

        if (!IsValidVirtualPath(virtualPath))
            throw new FormatException(
                $"Virtual path must start with '/': '{virtualPath}'. A physical path is never a virtual path — mount it under a name, e.g. '{Quote(basePath)}:/workspace:ro'.");

        // Parse overrides
        var overrides = new List<SubPathOverride>();
        for (var i = 1; i < segments.Count; i++)
        {
            var overrideParts = SplitOutsideQuotes(segments[i], ':');
            if (overrideParts.Count != 2)
                throw new FormatException(
                    $"Invalid override format. Expected '<subpath>:<rights>', got: '{segments[i]}'");

            overrides.Add(new SubPathOverride(Unquote(overrideParts[0]), ParseRights(overrideParts[1])));
        }

        return new FileSystemMount(basePath, virtualPath, defaultRights, overrides);
    }

    /// <summary>
    /// The physical base path a mount string declares, unquoted — or <see langword="null"/>
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

        var mainPart = SplitOutsideQuotes(mountString, ';')[0];
        var parts = SplitMainPart(mainPart);
        return parts.Count >= 2 ? Unquote(parts[0]) : null;
    }

    /// <summary>
    /// The same mount string with its physical segment replaced by <paramref name="basePath"/>,
    /// quoted if the grammar requires it. Used by the bootstrappers that resolve a user-supplied
    /// relative mount to an absolute one before handing it to <see cref="Parse"/>.
    /// </summary>
    /// <exception cref="FormatException">The string is not a well-formed mount.</exception>
    public static string WithBasePath(string mountString, string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountString);
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        var segments = SplitOutsideQuotes(mountString, ';');
        var parts = SplitMainPart(segments[0]);
        if (parts.Count < 2)
            throw new FormatException(
                $"Invalid mount format. Expected '<physical>:<virtual>:<rights>', got: '{segments[0]}'.");

        parts[0] = Quote(basePath);
        segments[0] = string.Join(':', parts);
        return string.Join(';', segments);
    }

    /// <summary>
    /// Quotes a path only when the grammar needs it: a bare <c>C:\src</c> is left exactly as it
    /// reads, because the parser recognises the drive letter on its own.
    /// </summary>
    public static string Quote(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (!NeedsQuoting(segment))
            return segment;

        var body = segment.Replace("\"", "\"\"", StringComparison.Ordinal);
        return QuoteCharacter + body + QuoteCharacter;
    }

    /// <summary>
    /// True when a segment cannot be written bare: it carries a separator, a quote, or a
    /// trailing backslash that would otherwise glue itself to the separator.
    /// </summary>
    private static bool NeedsQuoting(string segment)
    {
        if (segment.Length == 0)
            return false;
        if (segment.EndsWith('\\') || segment.Contains(QuoteCharacter, StringComparison.Ordinal))
            return true;
        if (segment.Contains(';', StringComparison.Ordinal))
            return true;

        // A drive-letter colon is read natively; any other colon is a separator.
        var from = HasDriveLetterPrefix(segment) ? 2 : 0;
        return segment.IndexOf(':', from) >= 0;
    }

    private static bool HasDriveLetterPrefix(string value) =>
        value.Length >= 3
        && char.IsLetter(value[0])
        && value[1] == ':'
        && (value[2] == '\\' || value[2] == '/');

    /// <summary>
    /// Splits on occurrences of <paramref name="separator"/> that sit outside a quoted segment.
    /// Quotes are carried through verbatim — unquoting happens once, per segment, after the split.
    /// </summary>
    private static List<string> SplitOutsideQuotes(string value, char separator)
    {
        var parts = new List<string>();
        var builder = new System.Text.StringBuilder(value.Length);
        var inQuotes = false;

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];

            if (current == QuoteCharacter)
            {
                // A doubled quote inside a quoted segment is a literal one, not a terminator.
                if (inQuotes && i + 1 < value.Length && value[i + 1] == QuoteCharacter)
                {
                    builder.Append(current).Append(current);
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                builder.Append(current);
                continue;
            }

            if (!inQuotes && current == separator)
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

    /// <summary>Strips the quotes of one already-split segment, if it carries any.</summary>
    private static string Unquote(string segment)
    {
        if (segment.Length < 2 || segment[0] != QuoteCharacter || segment[^1] != QuoteCharacter)
            return segment;

        return segment[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
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
        // Iteratively peel off path/rights segments. A quoted segment is taken whole, so a path
        // carrying a ':' or ending in a backslash survives verbatim. Outside quotes, a Windows
        // drive-letter prefix ("X:\" or "X:/") is recognised so its embedded ':' is not a
        // separator — that convenience is why "C:\src:/virtual:ro" needs no quoting at all.
        // The scan stays segment-agnostic: a drive letter on the virtual side splits cleanly and
        // is then rejected by IsValidVirtualPath with a precise message, rather than failing
        // here as a malformed mount string.
        var parts = new List<string>();
        var rest = mainPart;

        while (rest.Length > 0)
        {
            int nextColon;
            if (rest[0] == QuoteCharacter)
            {
                var close = IndexOfClosingQuote(rest);
                // An unterminated quote is a malformed spec: keep the remainder as one segment
                // so the caller reports the format error rather than splitting nonsense.
                if (close < 0)
                {
                    parts.Add(rest);
                    break;
                }

                nextColon = close + 1 < rest.Length && rest[close + 1] == ':' ? close + 1 : -1;
            }
            else
            {
                // For a Windows-path segment, start looking after the drive-letter colon.
                nextColon = rest.IndexOf(':', HasDriveLetterPrefix(rest) ? 2 : 0);
            }

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

    /// <summary>Index of the quote closing the one at position 0, honouring doubled quotes.</summary>
    private static int IndexOfClosingQuote(string value)
    {
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] != QuoteCharacter)
                continue;

            if (i + 1 < value.Length && value[i + 1] == QuoteCharacter)
            {
                i++;
                continue;
            }

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
