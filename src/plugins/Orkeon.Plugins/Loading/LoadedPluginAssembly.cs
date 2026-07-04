using System.Reflection;
using System.Runtime.Loader;

namespace Orkeon.Plugins;

/// <summary>
/// A plugin assembly loaded in its own isolated, collectible
/// <see cref="AssemblyLoadContext"/>, together with the <see cref="IOrkeonPlugin"/>
/// instances it exposes.
/// </summary>
public sealed class LoadedPluginAssembly
{
    private readonly AssemblyLoadContext _context;
    private bool _unloadRequested;

    internal LoadedPluginAssembly(
        string virtualPath,
        Assembly assembly,
        AssemblyLoadContext context,
        IReadOnlyList<IOrkeonPlugin> plugins)
    {
        VirtualPath = virtualPath;
        Assembly = assembly;
        _context = context;
        Plugins = plugins;
    }

    /// <summary>Virtual path the assembly was loaded from.</summary>
    public string VirtualPath { get; }

    /// <summary>The loaded assembly (lives in an isolated, collectible context).</summary>
    public Assembly Assembly { get; }

    /// <summary>The plugin instances found in the assembly.</summary>
    public IReadOnlyList<IOrkeonPlugin> Plugins { get; }

    /// <summary>True once <see cref="Unload"/> has been requested.</summary>
    public bool IsUnloadRequested => _unloadRequested;

    /// <summary>
    /// Initiates the unload of the plugin's load context. Idempotent.
    /// </summary>
    /// <remarks>
    /// Unloading is cooperative and completes only when no plugin-provided object
    /// (service instance, delegate, type) is reachable anymore — typically after the
    /// host's service provider has been disposed. Unloading while plugin services are
    /// still in use leaves the context alive until they are released.
    /// </remarks>
    public void Unload()
    {
        if (_unloadRequested)
            return;

        _unloadRequested = true;
        _context.Unload();
    }
}
