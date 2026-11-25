using System.Globalization;
using System.Text;

namespace Orkeon.Tools.Email.Mime;

/// <summary>
/// Turns the file name a sender chose into one that is safe to write: no directory part, no
/// traversal, no control or reserved characters, no device name, bounded length. The sender
/// controls this string entirely, so nothing of it is trusted.
/// <para>
/// Linked into <c>Orkeon.Host</c> as source as well, for the files of a chat message: nothing here may
/// depend on a package or on another type of this assembly.
/// </para>
/// </summary>
internal static class SafeFileNames
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
    /// A safe file name for the file at <paramref name="index"/> (zero-based), falling back to
    /// <c>attachment-N</c> plus <paramref name="fallbackExtension"/>.
    /// </summary>
    public static string Sanitize(string? declaredName, int index, string fallbackExtension = ".bin")
    {
        var name = LastSegment(declaredName ?? string.Empty);
        var cleaned = new StringBuilder(name.Length);
        foreach (var rune in name.EnumerateRunes())
        {
            // Format characters are invisible and reorder or hide text: "invoice\u202Efdp.exe"
            // reads "invoiceexe.pdf" to a human, and to an agent that shows it one. By rune: the invisible
            // tag characters (U+E0020 and up) are pairs of chars. A line or paragraph separator ends a line.
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
                    or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                || (rune.IsBmp && Array.IndexOf(ForbiddenCharacters, (char)rune.Value) >= 0))
                cleaned.Append('_');
            else
                cleaned.Append(rune.ToString());
        }

        var safe = cleaned.ToString().Trim().TrimStart('.').TrimEnd('.', ' ');
        if (safe.Length == 0)
            safe = string.Create(CultureInfo.InvariantCulture, $"attachment-{index + 1}{fallbackExtension}");

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

    // In UTF-8 bytes, what most file systems bound a name by, and never in the middle of a character.
    private static string Truncate(string name)
    {
        if (Encoding.UTF8.GetByteCount(name) <= MaxLength)
            return name;

        var extension = Path.GetExtension(name);
        if (Encoding.UTF8.GetByteCount(extension) >= MaxLength / 2)
            extension = string.Empty;
        var left = MaxLength - Encoding.UTF8.GetByteCount(extension);
        var stem = new StringBuilder(MaxLength);
        foreach (var rune in name.EnumerateRunes())
        {
            left -= rune.Utf8SequenceLength;
            if (left < 0)
                break;
            stem.Append(rune.ToString());
        }

        return stem.Append(extension).ToString();
    }
}
