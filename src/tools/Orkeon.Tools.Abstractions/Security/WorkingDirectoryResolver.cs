using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Abstractions.Security;

/// <summary>
/// The working directory of a tool call, and the resolution of a relative path against it: the
/// one rule every file tool and <c>shell_command</c> share (#199).
/// <para>
/// A model writes a path as it reads it in the code (<c>src/stats.js</c>), and a shell command
/// already ran in a working directory without being told. The file tools sent that path straight
/// to the mount registry, which refused it, and every refusal cost the model a turn spent writing
/// the absolute path. They now resolve it where the shell runs: the agent's root under an agent
/// run (a worktree of a parallel batch), else the file system's working directory
/// (<c>/workspace</c> on a project).
/// </para>
/// </summary>
public static class WorkingDirectoryResolver
{
    /// <summary>The refusal of a file tool called without a path (#200).</summary>
    public const string PathRequiredMessage =
        "path is required: the absolute path of the file, e.g. /workspace/src/stats.js";

    /// <summary>
    /// The virtual working directory of the calling flow: the root of the current agent run when
    /// one is declared, else <see cref="IFileSystemService.WorkingDirectory"/>; <see langword="null"/>
    /// when there is none.
    /// </summary>
    /// <param name="fileSystemService">The virtual file system.</param>
    public static string? Current(IFileSystemService fileSystemService)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        return AgentRunScope.Current?.Root ?? fileSystemService.WorkingDirectory;
    }

    /// <summary>
    /// The absolute virtual spelling of <paramref name="path"/>: unchanged when it is already
    /// absolute (it starts with '/', or with a drive for a Windows-rooted mount); otherwise joined
    /// to <see cref="Current"/> with its <c>.</c> and <c>..</c> segments resolved. Unchanged too
    /// when there is no working directory, so the registry refuses it as before. Escaping the
    /// working directory with <c>..</c> is not refused here: the mounts and
    /// <see cref="AgentRootGuard"/> judge the resolved path.
    /// </summary>
    /// <param name="path">The path a caller wrote.</param>
    /// <param name="fileSystemService">The virtual file system.</param>
    public static string Absolutize(string path, IFileSystemService fileSystemService)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(fileSystemService);

        if (string.IsNullOrWhiteSpace(path) || IsAbsolute(path))
            return path;

        var workingDirectory = Current(fileSystemService);
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return path;

        var segments = new List<string>();
        foreach (var segment in $"{workingDirectory}/{path}".Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }

        return "/" + string.Join('/', segments);
    }

    private static bool IsAbsolute(string path) =>
        path[0] is '/' or '\\'
        || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\');
}
