using System.Globalization;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Names a downloaded file safely: no path, no reserved character, never one already taken.</summary>
internal static class FileNames
{
    private const int MaxStemLength = 120;

    private static readonly char[] Forbidden = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary><paramref name="name"/> stripped of what a file name may not hold; <c>file</c> when nothing is left.</summary>
    /// <remarks>
    /// A drive item's name is chosen by whoever shared it: a format character (U+202E) would show
    /// "invoice\u202Efdp.exe" as "invoiceexe.pdf", and a device name (CON) cannot be written on Windows.
    /// </remarks>
    public static string Sanitize(string? name)
    {
        var cleaned = new string((name ?? string.Empty)
            .Select(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format || Forbidden.Contains(c) ? '_' : c)
            .ToArray()).Trim().Trim('.');
        if (cleaned.Length == 0)
            return "file";

        var extension = Path.GetExtension(cleaned);
        var stem = cleaned[..^extension.Length];
        if (stem.Length > MaxStemLength)
            stem = stem[..MaxStemLength];
        if (stem.Length == 0)
            return "file" + extension;

        // Windows reserves a device name whatever follows its first dot: CON.tar.gz is CON too.
        var dot = stem.IndexOf('.', StringComparison.Ordinal);
        if (ReservedDeviceNames.Contains((dot < 0 ? stem : stem[..dot]).TrimEnd(' ')))
            stem = "_" + stem;
        return stem + extension;
    }

    /// <summary>The first free path for <paramref name="name"/> in <paramref name="directory"/>: <c>name.ext</c>, then <c>name (2).ext</c>…</summary>
    public static async Task<string> FreePathAsync(IFileSystemService fileSystem, string directory, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var safe = Sanitize(name);
        var extension = Path.GetExtension(safe);
        var stem = safe[..^extension.Length];
        for (var attempt = 1; ; attempt++)
        {
            var candidate = attempt == 1 ? safe : $"{stem} ({attempt}){extension}";
            var path = $"{directory}/{candidate}";
            if (!await fileSystem.ExistsAsync(path, cancellationToken).ConfigureAwait(false))
                return path;
        }
    }
}
