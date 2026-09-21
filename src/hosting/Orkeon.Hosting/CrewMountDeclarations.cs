using System.Collections.ObjectModel;
using Orkeon.Compliance.Vfs;
using Orkeon.Constants.FileSystem;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Hosting;

/// <summary>
/// The <c>mounts:</c> block of a crew definition, read from the physical disk before the host
/// exists (VFS-90, D-02). The crew loader reads the same block through the VFS — but the VFS
/// is built over the mounts this block helps select, so a runner needs the block first, and
/// needs it without the host. Two readers of one file, on purpose: this one is a probe that
/// answers "what does the crew ask for?", the loader remains the authority and the safety net
/// — nothing this probe misses can lead to a silent acceptance, only to the loader's own
/// refusal a moment later.
/// </summary>
/// <param name="SourceFile">The file the items were read from, or null when there is none.</param>
/// <param name="Items">The block's items as written (<c>/root</c> or <c>&lt;ulid&gt;|/root</c>),
/// unparsed: the guard parses them so a malformed one is refused with the file named.</param>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: pre-reads the crew's own mounts: block from the physical disk, before the host (and thus IFileSystemService) is built, so a mount selection that cannot be resolved is refused in one line rather than out of a DI factory.")]
public sealed record CrewMountDeclarations(string? SourceFile, IReadOnlyList<string> Items)
{
    /// <summary>No block: a scripting entry point, or a target that has no readable settings file.</summary>
    public static CrewMountDeclarations None { get; } = new(null, []);

    private static readonly string[] YamlExtensions = [".yaml", ".yml"];

    /// <summary>
    /// Reads the block of the target <c>--config</c> points at. A crew directory is read from
    /// its <see cref="ConventionalNames.CrewSettingsFile"/>, else from
    /// <see cref="ConventionalNames.CrewSettingsFallbackFile"/> — the loader's own order; a
    /// YAML file is read from itself; anything else (a <c>.ork.ts</c> script) has no block.
    /// A file that cannot be read or parsed yields no items: the loader reports it properly.
    /// </summary>
    /// <param name="configPath">The resolved crew target.</param>
    /// <param name="isCrewDirectory">Whether the target is a multi-file crew directory.</param>
    public static CrewMountDeclarations Read(string configPath, bool isCrewDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);

        var file = isCrewDirectory
            ? FirstExisting(configPath, ConventionalNames.CrewSettingsFile, ConventionalNames.CrewSettingsFallbackFile)
            : ExistingYamlFile(configPath);
        if (file is null)
            return None;

        try
        {
            var probe = new YamlDotNetSerializer().Deserialize<MountsProbe>(File.ReadAllText(file));
            var items = probe?.Mounts?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToList() ?? [];
            return new CrewMountDeclarations(file, items);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
        {
            return new CrewMountDeclarations(file, []);
        }
    }

    private static string? FirstExisting(string directory, params string[] names)
    {
        foreach (var name in names)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? ExistingYamlFile(string path) =>
        IsYaml(path) && File.Exists(path) ? path : null;

    private static bool IsYaml(string path) =>
        YamlExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The one key the probe reads; every other key of the file is ignored.</summary>
    // YamlDotNet populates the property by reflection, so the analyzer reports it as
    // unassigned (S3459) and its setter as unused (S1144). Both are false positives.
#pragma warning disable S3459, S1144 // Populated by YamlDotNet reflection, not by code
    private sealed class MountsProbe
    {
        public Collection<string>? Mounts { get; set; }
    }
#pragma warning restore S3459, S1144
}
