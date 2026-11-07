namespace Orkeon.Plugins;

/// <summary>
/// Finds plugin assembly candidates in the configured plugin directory.
/// </summary>
public interface IPluginAssemblyDiscovery
{
    /// <summary>
    /// Scans <see cref="OrkeonPluginsOptions.Directory"/> (a virtual path) and returns
    /// the plugin candidates, ordered by virtual path. The layout is one sub-folder per plugin:
    /// <c>&lt;dir&gt;/MyPlugin/plugin.json</c> plus the entry assembly the manifest names
    /// (<c>&lt;dir&gt;/MyPlugin/MyPlugin.dll</c>; private dependencies live alongside and are resolved
    /// through the plugin's <c>.deps.json</c>). A <c>.dll</c> dropped flat in the directory is not a plugin.
    /// A folder that holds a <c>.dll</c> and no manifest, or whose manifest is unusable, is
    /// returned as a candidate carrying <see cref="PluginAssemblyCandidate.ManifestFailure"/>, which the
    /// loader refuses. Returns an empty list when the directory does not exist.
    /// </summary>
    /// <param name="options">Discovery options (directory, search pattern).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<PluginAssemblyCandidate>> DiscoverAsync(
        OrkeonPluginsOptions options,
        CancellationToken ct = default);
}
