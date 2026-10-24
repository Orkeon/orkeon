using Orkeon.Compliance.Vfs;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting;

/// <summary>
/// The folder of the LLM exchange log (<c>--llm-log</c>, <c>--llm-log-path</c>), one rule for every
/// host that writes it: <c>orkeon run</c> for a YAML crew or a procedural script, and <c>orkeon-repl</c>.
/// <c>--llm-log</c> alone writes under <c>./llm-logs</c>; <c>--llm-log-path</c> names the folder and
/// implies <c>--llm-log</c>; a folder outside the working directory needs <c>--allow-external-mounts</c>.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: resolves and creates the user-supplied --llm-log-path folder before the VFS mount that hosts it is provisioned.")]
internal static class RunnerLlmLog
{
    /// <summary>The folder <c>--llm-log</c> writes under when no <c>--llm-log-path</c> names one.</summary>
    internal const string DefaultDirectory = "llm-logs";

    /// <summary>The full physical path of the log folder, or null when logging is off.</summary>
    /// <param name="enabled"><c>--llm-log</c>.</param>
    /// <param name="path"><c>--llm-log-path</c>, or null.</param>
    internal static string? ResolvePath(bool enabled, string? path)
    {
        if (!enabled && string.IsNullOrWhiteSpace(path))
            return null;

        return Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultDirectory : path);
    }

    /// <summary>
    /// The internal mount carrying the log, or nothing when logging is off. The log is
    /// infrastructure: the VFS must reach it, no agent has any business addressing it — hence the
    /// internal-mount list rather than <c>--mount</c>. The folder is created here: a mount base path
    /// must exist before the registry is built (FileSystemServiceRegistration throws
    /// DirectoryNotFoundException otherwise).
    /// </summary>
    internal static IReadOnlyList<string> CreateMounts(string? physicalPath)
    {
        if (physicalPath is null)
            return [];

        Directory.CreateDirectory(physicalPath);
        return [$"{FileSystemMount.Quote(physicalPath)}:{RunnerVirtualRoots.LlmLogs}:rw"];
    }

    /// <summary>
    /// Refuses a log folder outside the working directory without the opt-in, writing the refusal to
    /// <paramref name="error"/>. "Outside" is asked of <see cref="PhysicalPathContainment"/>, the
    /// predicate <c>PathValidator</c> enforces the rule with.
    /// </summary>
    /// <param name="physicalPath">The resolved log folder, or null when logging is off.</param>
    /// <param name="allowExternalMounts"><c>--allow-external-mounts</c> or its variable.</param>
    /// <param name="error">Where the refusal is written.</param>
    /// <param name="cwd">The working directory; the process's when null.</param>
    /// <returns>True when the folder may be written.</returns>
    internal static bool EnsureAllowed(string? physicalPath, bool allowExternalMounts, TextWriter error, string? cwd = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        cwd ??= Directory.GetCurrentDirectory();
        if (physicalPath is null || allowExternalMounts || PhysicalPathContainment.IsUnder(physicalPath, cwd))
            return true;

        error.WriteLine(
            "ERROR: --allow-external-mounts is required when --llm-log-path "
            + "points outside the current working directory "
            + "(or set ORKEON_ALLOW_EXTERNAL_MOUNTS=1).");
        error.WriteLine($"       llmLogPath  : {physicalPath}");
        error.WriteLine($"       cwd         : {cwd}");
        return false;
    }
}
