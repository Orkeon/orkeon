using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tools.Abstractions.Security;

/// <summary>
/// Confines a virtual path to the working root of the current agent run (RT-06): the root is an
/// intersection with the mounts, never an extension of them.
/// </summary>
public static class AgentRootGuard
{
    /// <summary>
    /// Returns a denial when <paramref name="virtualPath"/> lies on the same mount as the current run's
    /// root but outside it; <c>null</c> when the path is allowed, on another mount, or no root is declared.
    /// </summary>
    /// <param name="fileSystemService">The virtual file system, for its mounts.</param>
    /// <param name="virtualPath">The virtual path about to be accessed.</param>
    public static PathValidationResult? Check(IFileSystemService fileSystemService, string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        ArgumentNullException.ThrowIfNull(virtualPath);
        var rootPath = AgentRunScope.Current?.Root;
        if (rootPath is null)
            return null;

        var root = Normalize(rootPath);
        var path = Normalize(virtualPath);
        var mount = fileSystemService.GetAvailableMounts()
            .Select(m => Normalize(m.VirtualPath))
            .Where(m => Contains(m, root))
            .OrderByDescending(m => m.Length)
            .FirstOrDefault();

        if (mount is null || !Contains(mount, path) || Contains(root, path))
            return null;

        return PathValidationResult.Denied($"Access denied: '{virtualPath}' is outside the agent working root '{rootPath}'.");
    }

    private static bool Contains(string parent, string child) =>
        parent == "/"
        || child.Equals(parent, StringComparison.Ordinal)
        || child.StartsWith(parent + "/", StringComparison.Ordinal);

    private static string Normalize(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
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
}
