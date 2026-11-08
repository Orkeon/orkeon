namespace Orkeon.Plugins;

/// <summary>Checks a <see cref="PluginManifest"/> against the Orkeon release hosting it.</summary>
public static class PluginManifestValidator
{
    /// <summary>
    /// Throws <see cref="PluginManifestException"/> when a required field is missing or malformed
    /// or when <paramref name="orkeonVersion"/> falls outside the manifest's range.
    /// </summary>
    /// <param name="manifest">The manifest to check.</param>
    /// <param name="orkeonVersion">The version of the Orkeon release hosting the plugin.</param>
    public static void Validate(PluginManifest manifest, string orkeonVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(orkeonVersion);

        var range = ValidateShape(manifest);
        var host = PluginSemVer.TryParse(orkeonVersion)
            ?? throw new PluginManifestException($"The Orkeon version '{orkeonVersion}' is not a version.");
        if (!range.Contains(host))
        {
            throw new PluginManifestException(
                $"Plugin '{manifest.Name}' targets Orkeon '{manifest.OrkeonVersion}' (orkeonVersion); this is Orkeon {orkeonVersion}.");
        }
    }

    /// <summary>
    /// Checks everything the manifest alone says: the required fields, the entry assembly name, the
    /// tool names and the syntax of the range. Returns the parsed range.
    /// </summary>
    internal static PluginVersionRange ValidateShape(PluginManifest manifest)
    {
        RequireText(manifest.Name, "name");
        RequireText(manifest.Version, "version");
        RequireText(manifest.OrkeonVersion, "orkeonVersion");
        RequireText(manifest.EntryAssembly, "entryAssembly");

        var entry = manifest.EntryAssembly!;
        if (entry.IndexOfAny(['/', '\\']) >= 0
            || entry is "." or ".."
            || !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginManifestException(
                $"Manifest field 'entryAssembly' must be the file name of a .dll inside the plugin folder, not '{entry}'.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in manifest.Tools)
        {
            if (string.IsNullOrWhiteSpace(tool))
                throw new PluginManifestException("Manifest field 'tools' holds a blank tool name.");
            if (!seen.Add(tool))
                throw new PluginManifestException($"Manifest field 'tools' lists '{tool}' twice.");
        }

        return PluginVersionRange.TryParse(manifest.OrkeonVersion)
            ?? throw new PluginManifestException(
                $"Manifest field 'orkeonVersion' is not a version range: '{manifest.OrkeonVersion}'.");
    }

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new PluginManifestException($"Manifest field '{field}' is required.");
    }
}
