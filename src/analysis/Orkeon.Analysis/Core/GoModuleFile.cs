using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Reads the module path a Go package's <c>go.mod</c> declares and tags the package node with it
/// (<see cref="DependencyGraphBuilder.GoModuleTag"/>), so an import under that path resolves to a directory.
/// </summary>
internal static class GoModuleFile
{
    public static async Task TagPackagesAsync(IEnumerable<RaggableNode> packages, IFileSystemService fileSystem, CancellationToken ct)
    {
        foreach (var package in packages)
        {
            if (!package.Tags.TryGetValue("marker", out var marker) || marker != "go") continue;
            var path = $"{package.VirtualFilePath.Replace('\\', '/').TrimEnd('/')}/go.mod";
            string? content;
            try { content = await fileSystem.TryReadAllTextAsync(path, ct).ConfigureAwait(false); }
            catch (FileAccessDeniedException) { content = null; }
            var modulePath = content is null ? null : ModulePathOf(content);
            if (!string.IsNullOrEmpty(modulePath)) package.TagsMutable[DependencyGraphBuilder.GoModuleTag] = modulePath;
        }
    }

    /// <summary>The path of the <c>module</c> directive, or null when the file declares none.</summary>
    public static string? ModulePathOf(string goMod)
    {
        foreach (var raw in goMod.Split('\n'))
        {
            var line = raw.Trim();
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment].TrimEnd();
            if (!line.StartsWith("module", StringComparison.Ordinal) || line.Length == 6 || !char.IsWhiteSpace(line[6])) continue;
            var path = line[6..].Trim().Trim('"', '`');
            return path.Length == 0 ? null : path;
        }
        return null;
    }
}
