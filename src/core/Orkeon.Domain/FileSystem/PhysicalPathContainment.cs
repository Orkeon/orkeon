namespace Orkeon.Domain.FileSystem;

/// <summary>
/// Is a physical path inside a physical directory? One implementation, because the question
/// was answered in three places with three different rules and the disagreements were all
/// bugs.
/// <para>
/// The rule has two parts and both matter. <b>Boundary</b>: a directory only contains what
/// sits under it followed by a separator — <c>/home/u/proj-old</c> is not inside
/// <c>/home/u/proj</c>, though a bare <c>StartsWith</c> says it is. <b>Case</b>: Windows
/// paths compare case-insensitively, POSIX ones do not; a guard that hardcodes
/// <see cref="StringComparison.Ordinal"/> lets <c>C:\Proj</c> and <c>C:\proj</c> read as two
/// different directories on the one platform where they are the same one.
/// </para>
/// <para>
/// Both inputs are compared as given: callers pass paths they have already run through
/// <see cref="Path.GetFullPath(string)"/>, since a containment test on unresolved input
/// answers about spelling rather than about location.
/// </para>
/// </summary>
public static class PhysicalPathContainment
{
    /// <summary>
    /// <see langword="true"/> when <paramref name="path"/> is <paramref name="directory"/>
    /// itself or sits somewhere beneath it.
    /// </summary>
    public static bool IsUnder(string path, string directory)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(directory);

        var comparison = Comparison;
        var trimmed = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return path.Equals(trimmed, comparison)
            || path.StartsWith(trimmed + Path.DirectorySeparatorChar, comparison)
            || path.StartsWith(trimmed + Path.AltDirectorySeparatorChar, comparison);
    }

    /// <summary>
    /// The comparison physical paths are compared with on this platform: ordinal everywhere
    /// except Windows, where the filesystem itself is case-insensitive.
    /// </summary>
    public static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
