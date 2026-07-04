namespace Orkeon.Plugins;

/// <summary>
/// Read-only view over the plugins loaded by <c>AddOrkeonPlugins(...)</c>.
/// Registered as a singleton in the host container for introspection
/// (diagnostics, listing, controlled unload via the concrete <see cref="PluginRegistry"/>).
/// </summary>
public interface IPluginRegistry
{
    /// <summary>The plugin assemblies currently loaded, in load order.</summary>
    IReadOnlyList<LoadedPluginAssembly> Assemblies { get; }

    /// <summary>All plugin instances, flattened across assemblies.</summary>
    IReadOnlyList<IOrkeonPlugin> Plugins { get; }

    /// <summary>
    /// Assemblies that failed to load when
    /// <see cref="OrkeonPluginsOptions.ContinueOnError"/> was enabled.
    /// </summary>
    IReadOnlyList<PluginLoadFailure> Failures { get; }
}
