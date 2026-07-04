using System.Reflection;
using System.Runtime.Loader;

namespace Orkeon.Plugins;

/// <summary>
/// Collectible <see cref="AssemblyLoadContext"/> hosting one plugin assembly and its
/// private dependencies. Dependencies are resolved through an
/// <see cref="AssemblyDependencyResolver"/> (driven by the plugin's <c>.deps.json</c>
/// when present); assemblies whose simple name starts with one of the shared prefixes
/// are deliberately <em>not</em> resolved here so they unify with the host's (default)
/// context — this keeps a single type identity for contract types such as
/// <see cref="IOrkeonPlugin"/> and <c>IServiceCollection</c>.
/// </summary>
/// <remarks>
/// An <see cref="AssemblyLoadContext"/> provides <em>isolation</em> (independent
/// dependency versions, unloadability), <b>not</b> a security boundary: plugin code
/// runs with the full privileges of the host process. See
/// <c>docs/architecture/plugins.md</c>.
/// </remarks>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string[] _sharedAssemblyPrefixes;

    /// <summary>
    /// Creates a collectible load context for the plugin assembly at
    /// <paramref name="pluginAssemblyPhysicalPath"/>.
    /// </summary>
    /// <param name="pluginAssemblyPhysicalPath">
    /// Physical path of the plugin's root assembly, as resolved by the VFS
    /// (<c>IFileSystemService.ResolveAndValidate</c>).
    /// </param>
    /// <param name="sharedAssemblyPrefixes">
    /// Assembly simple-name prefixes resolved by the host context instead of this one.
    /// </param>
    public PluginLoadContext(string pluginAssemblyPhysicalPath, IEnumerable<string> sharedAssemblyPrefixes)
        : base(name: BuildContextName(pluginAssemblyPhysicalPath), isCollectible: true)
    {
        ArgumentNullException.ThrowIfNull(sharedAssemblyPrefixes);

        _resolver = new AssemblyDependencyResolver(pluginAssemblyPhysicalPath);
        _sharedAssemblyPrefixes = sharedAssemblyPrefixes.ToArray();
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        if (IsSharedAssembly(assemblyName))
            return null; // Defer to the default context — host/plugin type identities unify.

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <inheritdoc />
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }

    private bool IsSharedAssembly(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null)
            return false;

        return _sharedAssemblyPrefixes.Any(
            prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildContextName(string pluginAssemblyPhysicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginAssemblyPhysicalPath);
        return $"orkeon-plugin:{Path.GetFileNameWithoutExtension(pluginAssemblyPhysicalPath)}";
    }
}
