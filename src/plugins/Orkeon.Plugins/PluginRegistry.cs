namespace Orkeon.Plugins;

/// <summary>
/// Default <see cref="IPluginRegistry"/> implementation. Populated at registration time
/// by <c>AddOrkeonPlugins(...)</c>, then read-only for the host.
/// </summary>
/// <remarks>
/// The registry is registered as a pre-built singleton <em>instance</em>, so the DI
/// container does <b>not</b> dispose it: unloading plugins remains an explicit host
/// decision (<see cref="UnloadAll"/> / <see cref="Dispose"/>), to be taken only after
/// the service provider that consumes plugin services has been disposed.
/// </remarks>
public sealed class PluginRegistry : IPluginRegistry, IDisposable
{
    private readonly List<LoadedPluginAssembly> _assemblies = [];
    private readonly List<PluginLoadFailure> _failures = [];

    // Flattened view of all plugins across assemblies. Rebuilt eagerly whenever the set of
    // loaded assemblies changes (Add / UnloadAll) so the property never re-projects the
    // collection on read (S2365) — the getter just returns the pre-built list.
    private IReadOnlyList<IOrkeonPlugin> _plugins = [];

    /// <inheritdoc />
    public IReadOnlyList<LoadedPluginAssembly> Assemblies => _assemblies;

    /// <inheritdoc />
    public IReadOnlyList<IOrkeonPlugin> Plugins => _plugins;

    /// <inheritdoc />
    public IReadOnlyList<PluginLoadFailure> Failures => _failures;

    internal void Add(LoadedPluginAssembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assemblies.Add(assembly);
        _plugins = _assemblies.SelectMany(static a => a.Plugins).ToList();
    }

    internal void AddFailure(PluginLoadFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _failures.Add(failure);
    }

    /// <summary>
    /// Requests the unload of every loaded plugin assembly and empties the registry.
    /// Safe to call multiple times. See <see cref="LoadedPluginAssembly.Unload"/> for
    /// the cooperative-unload caveats.
    /// </summary>
    public void UnloadAll()
    {
        foreach (var assembly in _assemblies)
            assembly.Unload();

        _assemblies.Clear();
        _plugins = [];
    }

    /// <inheritdoc />
    public void Dispose() => UnloadAll();
}
