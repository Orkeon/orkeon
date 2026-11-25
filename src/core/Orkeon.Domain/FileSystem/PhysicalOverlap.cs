namespace Orkeon.Domain.FileSystem;

/// <summary>
/// Whether two physical folders share a part of the tree: one is the other, holds it or is under it. Unlike
/// <see cref="PhysicalPathContainment"/>, which grants, this refuses: where a file system usually ignores the case
/// (Windows, macOS), two spellings are taken for one folder.
/// </summary>
internal static class PhysicalOverlap
{
    private static readonly char[] s_separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>Compares two full paths as this platform's file systems usually do.</summary>
    public static bool Between(string first, string second) =>
        Between(first, second, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

    /// <summary>Compares two full paths.</summary>
    public static bool Between(string first, string second, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return IsUnder(first, second, comparison) || IsUnder(second, first, comparison);
    }

    /// <summary>Whether a full path is a folder or under it, as this platform's file systems usually compare.</summary>
    public static bool IsUnder(string path, string directory) =>
        IsUnder(path, directory, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

    /// <summary>Whether a full path is a folder or under it.</summary>
    public static bool IsUnder(string path, string directory, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(directory);
        return IsUnder(path, directory, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>Compares two folders as written and as their links lead now; a loop of links overlaps nothing, it opens nothing.</summary>
    public static bool BetweenFolders(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        string[] firsts = Spellings(first);
        string[] seconds = Spellings(second);
        return firsts.Any(a => seconds.Any(b => Between(a, b)));
    }

    private static string[] Spellings(string folder)
    {
        var full = Path.GetFullPath(folder);
        return [full, PhysicalRealPath.Resolve(full) ?? full];
    }

    private static bool IsUnder(string path, string directory, StringComparison comparison)
    {
        var trimmed = directory.TrimEnd(s_separators);
        return path.TrimEnd(s_separators).Equals(trimmed, comparison)
            || s_separators.Any(separator => path.StartsWith(trimmed + separator, comparison));
    }
}
