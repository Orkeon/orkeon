namespace Orkeon.Plugins;

/// <summary>
/// Finds plugin assembly candidates in the configured plugin directory.
/// </summary>
public interface IPluginAssemblyDiscovery
{
    /// <summary>
    /// Scans <see cref="OrkeonPluginsOptions.Directory"/> (a virtual path) and returns
    /// the plugin assembly candidates, ordered by virtual path. Two layouts are
    /// recognized:
    /// <list type="bullet">
    /// <item><description>flat — <c>&lt;dir&gt;/MyPlugin.dll</c>;</description></item>
    /// <item><description>folder-per-plugin — <c>&lt;dir&gt;/MyPlugin/MyPlugin.dll</c>
    /// (private dependencies live alongside and are resolved through the plugin's
    /// <c>.deps.json</c>).</description></item>
    /// </list>
    /// Returns an empty list when the directory does not exist.
    /// </summary>
    /// <param name="options">Discovery options (directory, search pattern).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<PluginAssemblyCandidate>> DiscoverAsync(
        OrkeonPluginsOptions options,
        CancellationToken ct = default);
}
