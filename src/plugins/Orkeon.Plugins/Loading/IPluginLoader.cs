namespace Orkeon.Plugins;

/// <summary>
/// Loads a plugin assembly candidate in an isolated, collectible
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> and instantiates the
/// <see cref="IOrkeonPlugin"/> implementations it contains.
/// </summary>
public interface IPluginLoader
{
    /// <summary>
    /// Loads <paramref name="candidate"/> in a dedicated load context and returns the
    /// loaded assembly with its plugin instances (possibly none — the caller decides
    /// whether to keep or unload a plugin-less assembly).
    /// </summary>
    /// <param name="candidate">The assembly candidate produced by discovery.</param>
    /// <exception cref="PluginLoadException">
    /// The assembly is not a valid managed assembly, its types cannot be inspected, a
    /// plugin type cannot be instantiated, or the assembly was compiled against a
    /// bundled copy of <c>Orkeon.Plugins</c> (type identity mismatch).
    /// </exception>
    LoadedPluginAssembly Load(PluginAssemblyCandidate candidate);
}
