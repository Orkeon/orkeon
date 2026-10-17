using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Names a downloaded file safely: no path, no reserved character, never one already taken.</summary>
internal static class FileNames
{
    private const int MaxStemLength = 120;

    private static readonly char[] Forbidden = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary><paramref name="name"/> stripped of what a file name may not hold; <c>file</c> when nothing is left.</summary>
    public static string Sanitize(string? name)
    {
        var cleaned = new string((name ?? string.Empty).Select(c => char.IsControl(c) || Forbidden.Contains(c) ? '_' : c).ToArray()).Trim().Trim('.');
        if (cleaned.Length == 0)
            return "file";

        var extension = Path.GetExtension(cleaned);
        var stem = cleaned[..^extension.Length];
        if (stem.Length > MaxStemLength)
            stem = stem[..MaxStemLength];
        return stem.Length == 0 ? "file" + extension : stem + extension;
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
