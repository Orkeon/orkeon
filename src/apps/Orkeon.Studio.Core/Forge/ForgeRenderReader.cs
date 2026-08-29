using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Forge;

/// <summary>
/// Reads the rendered crew definition of a forge session as one displayable YAML
/// document (v3 W-06: the generated-definition card shows the YAML itself, not the
/// session path). Files are concatenated in render order — config, then agents, then
/// tasks — separated by a mono comment naming each part.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the forge session directory is " +
    "engine-owned storage on the physical disk, addressed before any VFS mount exists.")]
public static class ForgeRenderReader
{
    /// <summary>Name of the rendered crew folder inside a session directory.</summary>
    public const string CrewDirectoryName = "crew";

    /// <summary>
    /// Concatenates the session's rendered YAML, or an empty string when nothing is
    /// rendered yet. Tolerant of a missing or unreadable folder — the card simply has
    /// nothing to show, the render itself is the engine's truth.
    /// </summary>
    public static string ReadDefinition(string sessionDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory))
            return "";

        var crewDirectory = Path.Combine(sessionDirectory, CrewDirectoryName);

        try
        {
            if (!Directory.Exists(crewDirectory))
                return "";

            var parts = new List<string>();
            foreach (var file in OrderedYamlFiles(crewDirectory))
            {
                var relative = Path.GetRelativePath(sessionDirectory, file)
                    .Replace(Path.DirectorySeparatorChar, '/');
                parts.Add($"# --- {relative}\n{File.ReadAllText(file).TrimEnd()}\n");
            }

            return string.Join("\n", parts);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Comfort, not truth: a locked file must never take the wizard down.
            return "";
        }
    }

    private static IEnumerable<string> OrderedYamlFiles(string crewDirectory)
    {
        var rootFiles = Directory.EnumerateFiles(crewDirectory, "*.yaml")
            .OrderBy(f => f, StringComparer.Ordinal);
        foreach (var file in rootFiles)
            yield return file;

        foreach (var subDirectory in new[] { "agents", "tasks" })
        {
            var directory = Path.Combine(crewDirectory, subDirectory);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.yaml")
                         .OrderBy(f => f, StringComparer.Ordinal))
                yield return file;
        }
    }
}
