using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Domain.Configuration;

/// <summary>
/// A folder a crew opens to the people who talk to it (<c>files:</c>): what they may take from it, what they may leave in
/// it. The crew chooses within what the host mounts and never widens it.
/// </summary>
public sealed record CrewFileFolder
{
    /// <summary>Gets the virtual path of the folder, as <see cref="TryNormalizePath"/> writes it.</summary>
    public required string Path { get; init; }

    /// <summary>Gets whether the files of the folder, and of the folders under it, may be downloaded.</summary>
    public bool Download { get; init; }

    /// <summary>Gets whether files may be left in the folder, and in the folders under it.</summary>
    public bool Upload { get; init; }

    /// <summary>
    /// Writes a declared path in its one form: absolute, separated by <c>/</c>, without a trailing one. A segment that
    /// would name another folder than it reads (<c>..</c>, <c>.</c>, an empty one, one a file system trims) is refused,
    /// never resolved.
    /// </summary>
    /// <param name="value">The path as declared.</param>
    /// <param name="path">The path in its one form; empty when refused.</param>
    /// <param name="refusal">Why the path is refused; null when it is not.</param>
    public static bool TryNormalizePath(string? value, out string path, [NotNullWhen(false)] out string? refusal)
    {
        path = string.Empty;
        refusal = RefusalOf(value);
        if (refusal is not null)
            return false;

        path = value!.TrimEnd('/');
        return true;
    }

    private static string? RefusalOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "the path is missing";
        if (value[0] != '/')
            return "a virtual path starts with '/'";
        if (value.Any(char.IsControl))
            return "a path holds no control character";
        if (value.Contains('\\', StringComparison.Ordinal) || value.Contains(':', StringComparison.Ordinal))
            return "a virtual path holds no '\\' and no ':'";
        if (value.Trim('/').Length == 0)
            return "'/' is no folder: name one under a mount";

        var segments = value.TrimEnd('/')[1..].Split('/');
        if (Array.Exists(segments, s => s.Length == 0 || s[^1] is '.' or ' ' || s[0] == ' '))
            return "a segment is empty, is '.' or '..', or starts or ends where a file system trims it";

        return null;
    }
}
