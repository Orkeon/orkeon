using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core.Structural;

internal static class YamlRuleLoader
{
    public static async Task<StructuralRule> LoadAsync(IFileSystemService fileSystem, string virtualPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var yaml = await fileSystem.TryReadAllTextAsync(virtualPath, ct).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Rule file '{virtualPath}' was not found.", virtualPath);
        return StructuralRule.FromYaml(yaml);
    }
}
