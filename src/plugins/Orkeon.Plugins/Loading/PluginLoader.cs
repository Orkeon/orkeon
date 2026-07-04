using System.Reflection;

namespace Orkeon.Plugins;

/// <summary>
/// Default <see cref="IPluginLoader"/>: one collectible <see cref="PluginLoadContext"/>
/// per plugin assembly, dependency resolution via <c>AssemblyDependencyResolver</c>,
/// shared-prefix assemblies unified with the host context.
/// </summary>
public sealed class PluginLoader : IPluginLoader
{
    private readonly string[] _sharedAssemblyPrefixes;

    /// <summary>
    /// Creates a loader.
    /// </summary>
    /// <param name="sharedAssemblyPrefixes">
    /// Assembly simple-name prefixes resolved by the host context instead of the
    /// plugin context (see <see cref="OrkeonPluginsOptions.SharedAssemblyPrefixes"/>).
    /// </param>
    public PluginLoader(IEnumerable<string> sharedAssemblyPrefixes)
    {
        ArgumentNullException.ThrowIfNull(sharedAssemblyPrefixes);
        _sharedAssemblyPrefixes = sharedAssemblyPrefixes.ToArray();
    }

    /// <inheritdoc />
    public LoadedPluginAssembly Load(PluginAssemblyCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        PluginLoadContext? context = null;
        try
        {
            context = new PluginLoadContext(candidate.PhysicalPath, _sharedAssemblyPrefixes);
            var assembly = context.LoadFromAssemblyPath(candidate.PhysicalPath);
            var plugins = InstantiatePlugins(assembly, candidate.VirtualPath);
            return new LoadedPluginAssembly(candidate.VirtualPath, assembly, context, plugins);
        }
        catch (PluginLoadException)
        {
            context?.Unload();
            throw;
        }
        catch (Exception ex)
        {
            context?.Unload();
            // Keep the message virtual-path only; runtime messages embedding physical
            // paths stay confined to the inner exception.
            throw new PluginLoadException(
                $"Failed to load plugin assembly '{candidate.VirtualPath}' ({ex.GetType().Name}). See inner exception for details.",
                candidate.VirtualPath,
                ex);
        }
    }

    private static List<IOrkeonPlugin> InstantiatePlugins(Assembly assembly, string virtualPath)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var reasons = ex.LoaderExceptions
                .Where(static e => e is not null)
                .Select(static e => e!.GetType().Name)
                .Distinct()
                .ToList();
            throw new PluginLoadException(
                $"Failed to inspect types of plugin assembly '{virtualPath}' ({string.Join(", ", reasons)}). See inner exception for details.",
                virtualPath,
                ex);
        }

        var plugins = new List<IOrkeonPlugin>();
        foreach (var type in types)
        {
            if (!type.IsClass || type.IsAbstract)
                continue;

            if (typeof(IOrkeonPlugin).IsAssignableFrom(type))
            {
                plugins.Add(Instantiate(type, virtualPath));
            }
            else if (ImplementsForeignPluginContract(type))
            {
                throw new PluginLoadException(
                    $"Type '{type.FullName}' in '{virtualPath}' implements an interface named " +
                    $"'{typeof(IOrkeonPlugin).FullName}' whose identity differs from the host's. " +
                    "The plugin probably ships its own copy of Orkeon.Plugins.dll — reference " +
                    "Orkeon.Plugins with ExcludeAssets=\"runtime\" (or <Private>false</Private>), " +
                    "or keep 'Orkeon.' in SharedAssemblyPrefixes.",
                    virtualPath);
            }
        }

        return plugins;
    }

    private static IOrkeonPlugin Instantiate(Type type, string virtualPath)
    {
        try
        {
            return (IOrkeonPlugin)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Activator returned null."));
        }
        catch (Exception ex)
        {
            throw new PluginLoadException(
                $"Failed to instantiate plugin type '{type.FullName}' from '{virtualPath}' — " +
                $"plugin types need a public parameterless constructor. See inner exception for details.",
                virtualPath,
                ex);
        }
    }

    /// <summary>
    /// Detects the classic type-identity trap: the type implements an interface whose
    /// <em>full name</em> matches <see cref="IOrkeonPlugin"/> but whose identity comes
    /// from a duplicate Orkeon.Plugins assembly loaded inside the plugin context.
    /// </summary>
    private static bool ImplementsForeignPluginContract(Type type)
    {
        var contractFullName = typeof(IOrkeonPlugin).FullName;
        foreach (var itf in type.GetInterfaces())
        {
            if (itf.FullName == contractFullName)
                return true;
        }

        return false;
    }
}
