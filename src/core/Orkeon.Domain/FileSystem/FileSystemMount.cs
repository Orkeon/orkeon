using Orkeon.Domain.Common;

namespace Orkeon.Domain.FileSystem;

/// <summary>Maps a physical directory to a virtual path with access rights.</summary>
public sealed record FileSystemMount
{
    /// <summary>
    /// The identity of the settings entry this mount came from (VFS-90), or null for a mount
    /// declared without one — a hand-written settings line, a <c>--mount</c> argument, a
    /// runner's own root. Written as the <c>&lt;ulid&gt;|</c> prefix of the mount string; what a
    /// crew's <c>mounts:</c> block and Studio's team sidecar name an entry by, so two entries
    /// may share a virtual root and still be told apart.
    /// </summary>
    public MountId? Id { get; }
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
        MountVisibility visibility = MountVisibility.AgentFacing,
        MountId? id = null)
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
        Id = id;
    }

    /// <summary>
    /// The character between an entry's id and its mount string: <c>01J…|C:\data:/data:ro</c>.
    /// The prefix is read only when what precedes the first unquoted <c>|</c> is a bare
    /// alphanumeric token — a physical path that really contains a <c>|</c> is quoted, as any
    /// path the grammar cannot read bare.
    /// </summary>
    public const char IdSeparator = '|';

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

        var (idToken, spec) = SplitIdPrefix(mountString);
        MountId? id = null;
        if (idToken is not null && !MountId.TryParse(idToken, out id))
        {
            throw new FormatException(
                $"Invalid mount id '{idToken}': expected 26 Crockford base32 characters (0-9, A-Z without I, L, O, U). "
                + "Example: '01J9Z3K4M5N6P7Q8R9S0T1V2W3|C:\\data:/data:ro'. "
                + "A physical path that really contains '|' is quoted: \"a|b\":/x:ro.");
        }

        // Split into main part and override parts (separated by ';')
        var segments = SplitOutsideQuotes(spec, ';');
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

        return new FileSystemMount(basePath, virtualPath, defaultRights, overrides, id: id);
    }

    /// <summary>
    /// The id an entry carries before its <see cref="IdSeparator"/>, read without parsing the
    /// rest — the cheap question the hosts and Studio ask of every declared entry. Null when
    /// the string carries no prefix, or one that is not a well-formed id (the parser reports
    /// that one with its remedy).
    /// </summary>
    /// <param name="mountString">The entry as declared.</param>
    public static MountId? TryGetId(string? mountString)
    {
        if (string.IsNullOrWhiteSpace(mountString))
            return null;

        var (idToken, _) = SplitIdPrefix(mountString);
        return idToken is not null && MountId.TryParse(idToken, out var id) ? id : null;
    }

    /// <summary>
    /// The mount as a mount string — the canonical spelling <see cref="Parse"/> reads back,
    /// id prefix included when the mount has one, every path segment quoted only when the
    /// grammar needs it.
    /// </summary>
    public string ToMountString()
    {
        var builder = new System.Text.StringBuilder();
        if (Id is not null)
            builder.Append(Id.ToString()).Append(IdSeparator);

        builder.Append(Quote(BasePath)).Append(':').Append(Quote(VirtualPath)).Append(':').Append(FormatRights(DefaultRights));
        foreach (var item in Overrides)
            builder.Append(';').Append(Quote(item.RelativePath)).Append(':').Append(FormatRights(item.Rights));

        return builder.ToString();
    }

    /// <summary>The rights token of the mount string grammar: <c>ro</c>, <c>rw</c> or <c>rwnd</c>.</summary>
    /// <param name="rights">The rights to spell.</param>
    /// <exception cref="ArgumentOutOfRangeException">The rights have no token in the grammar.</exception>
    public static string FormatRights(FileAccessRights rights) => rights switch
    {
        FileAccessRights.ReadOnly => "ro",
        FileAccessRights.ReadWrite => "rw",
        FileAccessRights.ReadWriteNoDelete => "rwnd",
        _ => throw new ArgumentOutOfRangeException(nameof(rights), rights, "Only ro, rw and rwnd can be written in a mount string."),
    };

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

        var (_, spec) = SplitIdPrefix(mountString);
        var mainPart = SplitOutsideQuotes(spec, ';')[0];
        var parts = SplitMainPart(mainPart);
        if (parts.Count < 2)
            return null;

        // A blank physical segment (":/workspace:ro") is not a folder this can name: callers
        // resolve what comes back against the working directory, and Path.GetFullPath("")
        // throws. Null sends them down their "the parser will explain it" path instead.
        var basePath = Unquote(parts[0]);
        return string.IsNullOrWhiteSpace(basePath) ? null : basePath;
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

        // The id prefix is kept verbatim: rebasing a folder does not change which entry it is.
        var (idToken, spec) = SplitIdPrefix(mountString);
        var segments = SplitOutsideQuotes(spec, ';');
        var parts = SplitMainPart(segments[0]);
        if (parts.Count < 2)
            throw new FormatException(
                $"Invalid mount format. Expected '<physical>:<virtual>:<rights>', got: '{segments[0]}'.");

        parts[0] = Quote(basePath);
        segments[0] = string.Join(':', parts);
        var rebased = string.Join(';', segments);
        return idToken is null ? rebased : $"{idToken}{IdSeparator}{rebased}";
    }

    /// <summary>
    /// Splits an optional id prefix off a mount string. The prefix exists only when the text
    /// before the first <c>|</c> is a bare alphanumeric token: a quoted segment, a Unix or
    /// Windows path, a <c>./</c> folder all start otherwise, so a <c>|</c> inside them keeps
    /// its literal meaning. The token is returned unvalidated — <see cref="Parse"/> refuses a
    /// bad one with its remedy, <see cref="TryGetId"/> answers null.
    /// </summary>
    private static (string? IdToken, string Spec) SplitIdPrefix(string mountString)
    {
        var separator = mountString.IndexOf(IdSeparator, StringComparison.Ordinal);
        if (separator <= 0)
            return (null, mountString);

        var token = mountString[..separator];
        return IsBareToken(token) ? (token, mountString[(separator + 1)..]) : (null, mountString);
    }

    /// <summary>True for a run of ASCII letters and digits — the only thing an id prefix can be.</summary>
    private static bool IsBareToken(string text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c))
                return false;
        }

        return text.Length > 0;
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

        // A bare token followed by '|' would read as an id prefix (VFS-90); quoting keeps the
        // '|' literal. Any other '|' (after a '/', a drive letter, a '.') already reads literally.
        var pipe = segment.IndexOf(IdSeparator, StringComparison.Ordinal);
        if (pipe > 0 && IsBareToken(segment[..pipe]))
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
            var nextColon = IndexOfSegmentSeparator(rest);
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

    /// <summary>
    /// Index of the ':' that closes the segment starting <paramref name="rest"/>, or -1 when
    /// the whole remainder is the last segment. A quoted segment is measured from its closing
    /// quote. An unterminated quote is a malformed spec, so it reports -1 too: the caller then
    /// keeps the remainder as one segment and the format error is reported there rather than
    /// splitting nonsense here.
    /// </summary>
    private static int IndexOfSegmentSeparator(string rest)
    {
        if (rest[0] != QuoteCharacter)
        {
            // For a Windows-path segment, start looking after the drive-letter colon.
            return rest.IndexOf(':', HasDriveLetterPrefix(rest) ? 2 : 0);
        }

        var close = IndexOfClosingQuote(rest);
        if (close < 0)
            return -1;

        return close + 1 < rest.Length && rest[close + 1] == ':' ? close + 1 : -1;
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
