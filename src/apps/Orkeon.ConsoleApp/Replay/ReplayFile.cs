using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Abstractions.Security;

namespace Orkeon.ConsoleApp.Replay;

/// <summary>A replay file read, or why it could not be.</summary>
/// <param name="VirtualPath">The path read, resolved against the working directory.</param>
/// <param name="Lines">The lines to replay; null when the file could not be read.</param>
/// <param name="Error">Why it could not be read; null when it was.</param>
internal sealed record ReplayFileLoad(string VirtualPath, IReadOnlyList<ReplayLine>? Lines, string? Error);

/// <summary>
/// Reads a replay file through the VFS, as the start script is read (<c>ScriptHostStartScriptRunner</c>): a
/// relative path is resolved against the working directory (<see cref="WorkingDirectoryResolver"/>), so
/// <c>--replay demo.replay</c> reads <c>/workspace/demo.replay</c> on a project.
/// </summary>
internal static class ReplayFile
{
    /// <summary>Reads and parses <paramref name="path"/>.</summary>
    /// <param name="fileSystem">The VFS.</param>
    /// <param name="path">The path given, virtual, absolute or relative to the working directory.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<ReplayFileLoad> LoadAsync(IFileSystemService fileSystem, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(path);

        var virtualPath = WorkingDirectoryResolver.Absolutize(path.Trim(), fileSystem);
        if (string.IsNullOrWhiteSpace(virtualPath))
            return new ReplayFileLoad(virtualPath, null, "no replay file given");

        string? content;
        try
        {
            content = await fileSystem.TryReadAllTextAsync(virtualPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileAccessDeniedException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new ReplayFileLoad(virtualPath, null, $"cannot read the replay file {virtualPath}: {ex.Message}");
        }

        return content is null
            ? new ReplayFileLoad(virtualPath, null, $"replay file not found: {virtualPath}")
            : new ReplayFileLoad(virtualPath, ReplayScript.Parse(content), null);
    }
}
