using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Orkeon.Host.Gateway;

/// <summary>
/// Which paths a file of Teams may be at. The token sent there is the application's, which may reach more than the file a
/// person shared, and the address comes from an activity: a path that is one of SharePoint's own pages or interfaces, or
/// that has two readings, is no file. Microsoft documents no path; what is named here is the least that tells a file apart.
/// </summary>
internal static partial class TeamsFilePaths
{
    private const string Layouts = "_layouts";
    private const string Interface = "_api";

    private static readonly FrozenSet<string> s_system = new[]
    {
        Layouts, Interface, "_admin", "_catalogs", "_windows", "_trust", "_login", "_forms", "_controltemplates",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a file may be read there: at its own path, by the download page, or as the content of a drive's item.</summary>
    public static bool IsDownload(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (Segments(address) is not { } path)
            return false;

        var first = Array.FindIndex(path, IsSystem);
        return first < 0
            || (Same(path[first], Layouts) && path.Length == first + 3 && path[first + 1] == "15" && Same(path[first + 2], "download.aspx"))
            || IsOfDrive(path, first, "content");
    }

    /// <summary>Whether a file may be put there: the upload session of a drive's item, which Teams makes for the file it was asked for.</summary>
    public static bool IsUpload(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Segments(address) is { } path && IsOfDrive(path, Array.FindIndex(path, IsSystem), "uploadSession");
    }

    /// <summary>Whether a picture may be read there: an attachment of the Bot Connector.</summary>
    public static bool IsPicture(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Segments(address) is { } path
            && Array.FindIndex(path, s => Same(s, "v3")) is var at and >= 0
            && path.Length > at + 2
            && Same(path[at + 1], "attachments");
    }

    /// <summary>Whether the path is <c>_api/v2.N/drive/…/end</c> from <paramref name="at"/>, with no other interface in it.</summary>
    private static bool IsOfDrive(string[] path, int at, string end) =>
        at >= 0
        && Same(path[at], Interface)
        && path.Length > at + 3
        && DriveVersion().IsMatch(path[at + 1])
        && (Same(path[at + 2], "drive") || Same(path[at + 2], "drives"))
        && Same(path[^1], end)
        && !path.Skip(at + 1).Any(IsSystem);

    /// <summary>The segments of the path, read once; null when one of them has another reading, or is empty.</summary>
    private static string[]? Segments(Uri address)
    {
        var path = address.AbsolutePath;
        if (path.Length < 2 || path[0] != '/')
            return null;

        var segments = path[1..].Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var read = Uri.UnescapeDataString(segments[i]);
            if (read.Any(c => c is '/' or '\\' || char.IsControl(c)) || Encoded().IsMatch(read))
                return null;

            // As a Windows server reads it: without the dots and the spaces at its end. Nothing left is a dot segment.
            segments[i] = read.TrimEnd('.', ' ');
            if (segments[i].Length == 0)
                return null;
        }

        return segments;
    }

    private static bool IsSystem(string segment) => s_system.Contains(segment) || segment.StartsWith("_vti_", StringComparison.OrdinalIgnoreCase);

    private static bool Same(string segment, string name) => string.Equals(segment, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>An encoding left once read: one made twice, or of bytes that are no text.</summary>
    [GeneratedRegex("%[0-9A-Fa-f]{2}", RegexOptions.CultureInvariant)]
    private static partial Regex Encoded();

    [GeneratedRegex(@"\Av2\.[0-9]\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DriveVersion();
}
