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

    // Flattened view of all plugins across assemblies. Cached so the property does not
    // re-project the collection on every read (S2365); invalidated whenever the set of
    // loaded assemblies changes (Add / UnloadAll).
    private IReadOnlyList<IOrkeonPlugin>? _pluginsCache;

    /// <inheritdoc />
    public IReadOnlyList<LoadedPluginAssembly> Assemblies => _assemblies;

    /// <inheritdoc />
    public IReadOnlyList<IOrkeonPlugin> Plugins =>
        _pluginsCache ??= _assemblies.SelectMany(static a => a.Plugins).ToList();

    /// <inheritdoc />
    public IReadOnlyList<PluginLoadFailure> Failures => _failures;

    internal void Add(LoadedPluginAssembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assemblies.Add(assembly);
        _pluginsCache = null;
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
        _pluginsCache = null;
    }

    /// <inheritdoc />
    public void Dispose() => UnloadAll();
}
