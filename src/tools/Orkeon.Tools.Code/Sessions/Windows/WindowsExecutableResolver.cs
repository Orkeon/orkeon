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
/// <item>A batch file (<c>.bat</c>, <c>.cmd</c>) is refused unless the caller allows it (an
/// unconfined session): Windows starts it through cmd.exe, whose own parsing of the arguments the C
/// runtime quoting does not protect, so the caller runs it with <see cref="WindowsCommandLine.BatchFile"/>.
/// When allowed, a name without extension is then tried with <c>.bat</c> and <c>.cmd</c> too
/// (<c>npm</c> finds <c>npm.cmd</c>).</item>
/// </list>
/// </remarks>
internal static class WindowsExecutableResolver
{
    private static readonly string[] s_extensions = [".exe", ".com"];
    private static readonly string[] s_batchExtensions = [".bat", ".cmd"];

    /// <summary>The full path of <paramref name="name"/>, or null with the reason in <paramref name="error"/>.</summary>
    /// <param name="name">A bare command name, validated by the tool.</param>
    /// <param name="pathVariable">The <c>PATH</c> the session runs with.</param>
    /// <param name="fileExists">Whether a file exists on the host.</param>
    /// <param name="error">Why nothing was found.</param>
    /// <param name="allowBatchFiles">Whether a <c>.bat</c> or <c>.cmd</c> may be the result.</param>
    public static string? Resolve(string name, string? pathVariable, Func<string, bool> fileExists, out string? error, bool allowBatchFiles = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(fileExists);

        var extension = Path.GetExtension(name);
        if (IsBatchFile(name) && !allowBatchFiles)
        {
            error = $"'{name}' is a batch file, which a confined shell session does not run (cmd.exe would parse its arguments)";
            return null;
        }

        string[] extensions = allowBatchFiles ? [.. s_extensions, .. s_batchExtensions] : s_extensions;
        string[] candidates = extensions.Any(e => extension.Equals(e, StringComparison.OrdinalIgnoreCase))
            ? [name]
            : [.. extensions.Select(e => name + e)];

        var found = (pathVariable ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => directory.Trim('"'))
            .Where(IsAbsolute)
            .Select(folder => FirstExisting(folder, candidates, fileExists))
            .FirstOrDefault(full => full is not null);
        if (found is not null)
        {
            error = null;
            return found;
        }

        error = $"'{name}' was not found on PATH";
        return null;
    }

    /// <summary>The first of <paramref name="candidates"/> that exists in <paramref name="folder"/>, or null.</summary>
    private static string? FirstExisting(string folder, string[] candidates, Func<string, bool> fileExists)
    {
        var prefix = folder.EndsWith('\\') || folder.EndsWith('/') ? folder : folder + "\\";
        return candidates.Select(candidate => prefix + candidate).FirstOrDefault(fileExists);
    }

    /// <summary>Whether <paramref name="path"/> names a batch file, which only cmd.exe runs.</summary>
    public static bool IsBatchFile(string path)
        => s_batchExtensions.Any(e => Path.GetExtension(path).Equals(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>A drive path (<c>C:\…</c>) or a UNC path (<c>\\server\…</c>): a relative PATH entry is skipped.</summary>
    private static bool IsAbsolute(string folder)
        => (folder.Length >= 3 && char.IsAsciiLetter(folder[0]) && folder[1] == ':' && folder[2] is '\\' or '/')
           || folder.StartsWith(@"\\", StringComparison.Ordinal);
}
