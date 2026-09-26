using System.Globalization;
using System.Text;
using MimeKit;

namespace Orkeon.Tools.Email.Mime;

/// <summary>
/// Turns the file name a sender chose into one that is safe to write: no directory part, no
/// traversal, no control or reserved characters, no device name, bounded length. The sender
/// controls this string entirely, so nothing of it is trusted.
/// </summary>
internal static class AttachmentNames
{
    private const int MaxLength = 120;

    private static readonly char[] ForbiddenCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// A safe file name for the attachment at <paramref name="index"/> (zero-based), falling back
    /// to <c>attachment-N</c> plus an extension guessed from <paramref name="contentType"/>.
    /// </summary>
    public static string Sanitize(string? declaredName, int index, ContentType? contentType)
    {
        var name = LastSegment(declaredName ?? string.Empty);
        var cleaned = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsControl(c) || Array.IndexOf(ForbiddenCharacters, c) >= 0)
                cleaned.Append('_');
            else
                cleaned.Append(c);
        }

        var safe = cleaned.ToString().Trim().TrimStart('.').TrimEnd('.', ' ');
        if (safe.Length == 0)
            safe = Fallback(index, contentType);

        // Windows reserves a device name whatever follows its first dot: CON.tar.gz is CON too.
        var dot = safe.IndexOf('.', StringComparison.Ordinal);
        var stem = (dot < 0 ? safe : safe[..dot]).TrimEnd(' ');
        if (ReservedDeviceNames.Contains(stem))
            safe = "_" + safe;

        return Truncate(safe);
    }

    /// <summary>
    /// <paramref name="name"/>, or the first <c>name (n).ext</c> not in <paramref name="taken"/>;
    /// the chosen name is added to <paramref name="taken"/>.
    /// </summary>
    public static string Unique(string name, ISet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(taken);
        if (taken.Add(name))
            return name;

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var n = 1; ; n++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({n}){extension}");
            if (taken.Add(candidate))
                return candidate;
        }
    }

    private static string LastSegment(string name)
    {
        var cut = name.LastIndexOfAny(['/', '\\']);
        return cut >= 0 ? name[(cut + 1)..] : name;
    }

    private static string Fallback(int index, ContentType? contentType)
    {
        var extension = contentType is not null && MimeTypes.TryGetExtension(contentType.MimeType, out var known)
            ? known
            : ".bin";
        return string.Create(CultureInfo.InvariantCulture, $"attachment-{index + 1}{extension}");
    }

    private static string Truncate(string name)
    {
        if (name.Length <= MaxLength)
            return name;

        var extension = Path.GetExtension(name);
        if (extension.Length >= MaxLength / 2)
            extension = string.Empty;
        return string.Concat(name.AsSpan(0, MaxLength - extension.Length), extension);
    }
}
