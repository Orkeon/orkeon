namespace Orkeon.Plugins;

/// <summary>
/// A plugin assembly found by <see cref="IPluginAssemblyDiscovery"/>: the virtual (VFS)
/// path of the file and the physical path the loader feeds to
/// <see cref="System.Runtime.Loader.AssemblyLoadContext.LoadFromAssemblyPath(string)"/>.
/// </summary>
/// <remarks>
/// The physical path is obtained through the VFS resolution mechanism
/// (<c>IFileSystemService.ResolveAndValidate</c>), so it is always mount-contained and
/// rights-checked. Error messages raised by the plugin system reference the virtual
/// path only.
/// </remarks>
public sealed record PluginAssemblyCandidate
{
    /// <summary>Virtual path of the assembly (e.g. <c>/plugins/Weather/Weather.dll</c>).</summary>
    public required string VirtualPath { get; init; }

    /// <summary>
    /// Mount-resolved physical path of the assembly, as produced by
    /// <c>IFileSystemService.ResolveAndValidate</c>. Required by the
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> loading APIs.
    /// </summary>
    public required string PhysicalPath { get; init; }
}
