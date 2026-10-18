namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// Finds the program a bare command name stands for on a Windows <c>PATH</c>. Pure: the file
/// system is a function the caller passes.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only the <c>PATH</c> directories are searched, in order: never the working directory, which
/// is a mount the agent writes to, so a <c>git.exe</c> it dropped there is never what <c>git</c>
/// runs.</item>
/// <item>A name without extension is tried with <c>.exe</c> then <c>.com</c>, as
/// <see cref="System.Diagnostics.Process"/> does.</item>
/// <item>A batch file (<c>.bat</c>, <c>.cmd</c>) is never run: Windows would start it through
/// cmd.exe, whose own parsing of the arguments the C runtime quoting does not protect.</item>
/// </list>
/// </remarks>
internal static class WindowsExecutableResolver
{
    private static readonly string[] s_extensions = [".exe", ".com"];

    /// <summary>The full path of <paramref name="name"/>, or null with the reason in <paramref name="error"/>.</summary>
    /// <param name="name">A bare command name, validated by the tool.</param>
    /// <param name="pathVariable">The <c>PATH</c> the session runs with.</param>
    /// <param name="fileExists">Whether a file exists on the host.</param>
    /// <param name="error">Why nothing was found.</param>
    public static string? Resolve(string name, string? pathVariable, Func<string, bool> fileExists, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(fileExists);

        var extension = Path.GetExtension(name);
        if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            error = $"'{name}' is a batch file, which a shell session does not run (cmd.exe would parse its arguments)";
            return null;
        }

        string[] candidates = s_extensions.Any(e => extension.Equals(e, StringComparison.OrdinalIgnoreCase))
            ? [name]
            : [.. s_extensions.Select(e => name + e)];

        foreach (var directory in (pathVariable ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var folder = directory.Trim('"');
            if (!IsAbsolute(folder))
                continue;
            foreach (var candidate in candidates)
            {
                var full = folder.EndsWith('\\') || folder.EndsWith('/') ? folder + candidate : folder + "\\" + candidate;
                if (fileExists(full))
                {
                    error = null;
                    return full;
                }
            }
        }

        error = $"'{name}' was not found on PATH";
        return null;
    }

    /// <summary>A drive path (<c>C:\…</c>) or a UNC path (<c>\\server\…</c>): a relative PATH entry is skipped.</summary>
    private static bool IsAbsolute(string folder)
        => (folder.Length >= 3 && char.IsAsciiLetter(folder[0]) && folder[1] == ':' && folder[2] is '\\' or '/')
           || folder.StartsWith(@"\\", StringComparison.Ordinal);
}
