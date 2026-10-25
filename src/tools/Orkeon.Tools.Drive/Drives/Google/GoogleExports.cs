namespace Orkeon.Tools.Drive.Drives.Google;

/// <summary>
/// The formats Google's own documents export to, by kind, as the Drive API documents them
/// (checked 2026-10-17). A Google document has no bytes of its own: reading or downloading
/// it means choosing one of these.
/// </summary>
internal static class GoogleExports
{
    private const string Document = "application/vnd.google-apps.document";
    private const string Spreadsheet = "application/vnd.google-apps.spreadsheet";
    private const string Presentation = "application/vnd.google-apps.presentation";
    private const string Drawing = "application/vnd.google-apps.drawing";

    private static readonly Dictionary<string, (string Extension, string MimeType)[]> Formats = new(StringComparer.Ordinal)
    {
        [Document] =
        [
            ("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            ("pdf", "application/pdf"),
            ("odt", "application/vnd.oasis.opendocument.text"),
            ("txt", "text/plain"),
            ("md", "text/markdown"),
            ("html", "text/html"),
        ],
        [Spreadsheet] =
        [
            ("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            ("pdf", "application/pdf"),
            ("ods", "application/vnd.oasis.opendocument.spreadsheet"),
            ("csv", "text/csv"),
        ],
        [Presentation] =
        [
            ("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
            ("pdf", "application/pdf"),
            ("odp", "application/vnd.oasis.opendocument.presentation"),
            ("txt", "text/plain"),
        ],
        [Drawing] =
        [
            ("png", "image/png"),
            ("pdf", "application/pdf"),
            ("svg", "image/svg+xml"),
        ],
    };

    /// <summary>
    /// The export of <paramref name="item"/> to <paramref name="format"/> (an extension), or to
    /// the kind's first format when <paramref name="format"/> is empty. A kind with no export
    /// (a form, a map) goes to PDF; a format the kind does not offer is refused with the list.
    /// </summary>
    public static (string Extension, string MimeType) Resolve(DriveItem item, string? format)
    {
        ArgumentNullException.ThrowIfNull(item);
        var offered = Offered(item);
        if (string.IsNullOrWhiteSpace(format))
            return offered[0];

        var wanted = format.Trim().TrimStart('.');
        var match = Array.FindIndex(offered, candidate => string.Equals(candidate.Extension, wanted, StringComparison.OrdinalIgnoreCase));
        if (match >= 0)
            return offered[match];

        throw new DriveToolException(
            DriveErrorCode.InvalidRequest,
            $"'{item.Name}' exports to {string.Join(", ", offered.Select(candidate => candidate.Extension))}; '{format}' is not one of them.");
    }

    /// <summary>The text export <c>drive_read</c> uses: plain text, or CSV for a spreadsheet; null when the kind has none.</summary>
    public static (string Extension, string MimeType)? TextExport(DriveItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var offered = Offered(item);
        var match = Array.FindIndex(offered, candidate => candidate.Extension is "txt" or "csv");
        return match < 0 ? null : offered[match];
    }

    /// <summary><paramref name="name"/> without an extension Google may have kept from an imported file.</summary>
    public static string StripExtension(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var dot = name.LastIndexOf('.');
        return dot > 0 && name.Length - dot <= 5 ? name[..dot] : name;
    }

    private static (string Extension, string MimeType)[] Offered(DriveItem item) =>
        item.MimeType is { } type && Formats.TryGetValue(type, out var formats) ? formats : [("pdf", "application/pdf")];
}
