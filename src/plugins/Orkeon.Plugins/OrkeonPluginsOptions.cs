namespace Orkeon.Plugins;

/// <summary>
/// Options controlling plugin discovery and loading.
/// Bound from the <c>"Plugins"</c> configuration section by the
/// <c>AddOrkeonPlugins(IFileSystemService, IConfiguration)</c> overload, or configured
/// through the <c>Action&lt;OrkeonPluginsOptions&gt;</c> delegate overload.
/// </summary>
/// <remarks>
/// These options are consumed eagerly at registration time (plugins must contribute
/// their services before the container is built); they are intentionally not exposed
/// through <c>IOptions&lt;T&gt;</c>.
/// </remarks>
public sealed class OrkeonPluginsOptions
{
    /// <summary>
    /// Virtual path (VFS) of the directory scanned for plugin assemblies.
    /// Must start with <c>/</c> and resolve inside a configured mount.
    /// Default: <c>/plugins</c>.
    /// </summary>
    public string Directory { get; set; } = "/plugins";

    /// <summary>
    /// Simple glob pattern applied to candidate assembly file names
    /// (e.g. <c>*.dll</c>, <c>MyCompany.*.dll</c>). Candidates must additionally
    /// carry the <c>.dll</c> extension regardless of the pattern. Default: <c>*.dll</c>.
    /// </summary>
    public string SearchPattern { get; set; } = "*.dll";

    /// <summary>
    /// When <c>true</c>, a plugin assembly that fails to load or to configure its
    /// services is recorded in <see cref="IPluginRegistry.Failures"/> and the
    /// remaining candidates keep loading. When <c>false</c> (default), the first
    /// failure throws a <see cref="PluginLoadException"/> (fail fast).
    /// </summary>
    public bool ContinueOnError { get; set; }

    /// <summary>
    /// Assembly simple-name prefixes that are never loaded into the plugin's isolated
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>; resolution defers to the
    /// host (default) context instead, so contract types (e.g. <see cref="IOrkeonPlugin"/>,
    /// <c>IBaseTool</c>, <c>IServiceCollection</c>) keep a single identity shared between
    /// the host and the plugin. Defaults: <c>Orkeon.</c> and <c>Microsoft.Extensions.</c>.
    /// </summary>
    public IList<string> SharedAssemblyPrefixes { get; } = new List<string>
    {
        "Orkeon.",
        "Microsoft.Extensions.",
    };
}
