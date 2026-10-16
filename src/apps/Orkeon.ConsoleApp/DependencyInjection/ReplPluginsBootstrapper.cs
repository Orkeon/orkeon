using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Plugins;

namespace Orkeon.ConsoleApp.DependencyInjection;

/// <summary>
/// Loads the plugins of the <c>Plugins</c> section when the settings declare it: the assemblies of its
/// <c>Directory</c> (a virtual path, <c>/plugins</c> by default) contribute their services, their tools
/// among them, to the REPL's container, so a command's <c>tools</c> namespace and a crew's
/// <c>.tools([...])</c> reach them like a built-in tool.
/// </summary>
/// <remarks>
/// <para>
/// A plugin registers services, so it is loaded while the container is being composed, before the REPL's
/// file system exists. Discovery reads a file system of its own, built from the declared mounts
/// (<c>Orkeon:FileSystem:Mounts</c> and <c>:InternalMounts</c>, the latter for a plugin folder agents must
/// not see): the registry's checks hold — the mount, its rights, no path out of it — but not the agents'
/// path validator, which refuses every <c>.dll</c> whatever the mount. A plugin is an assembly the
/// operator put in a folder the settings name, not a file an agent reaches.
/// </para>
/// <para>
/// A plugin runs with the REPL's own rights: there is no sandbox (see <c>docs/architecture/plugins.md</c>).
/// A plugin tool named like a tool already registered refuses the start: the tool registry names both.
/// </para>
/// </remarks>
internal static class ReplPluginsBootstrapper
{
    /// <summary>Loads the plugins when the <c>Plugins</c> section is present; nothing otherwise.</summary>
    /// <param name="services">The REPL's services.</param>
    /// <param name="configuration">The REPL's configuration, mounts included.</param>
    public static IServiceCollection AddReplPlugins(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // Declared whether present or not, as the runners declare MCP: its keys are judged at the start (GAP-40).
        services.DeclareSettingsShape(OrkeonPluginsServiceCollectionExtensions.ConfigurationSection, typeof(OrkeonPluginsOptions));
        if (!configuration.GetSection(OrkeonPluginsServiceCollectionExtensions.ConfigurationSection).Exists())
            return services;

        return services.AddOrkeonPlugins(DiscoveryFileSystem(configuration), configuration);
    }

    private static FileSystemService DiscoveryFileSystem(IConfiguration configuration)
    {
        var mounts = Declared(configuration, "Mounts", MountVisibility.AgentFacing)
            .Concat(Declared(configuration, "InternalMounts", MountVisibility.Internal))
            .ToList();

        // The registry lives as long as the plugins: their load contexts keep the assemblies it resolved.
#pragma warning disable CA2000 // Owned by the FileSystemService returned, which lives for the process.
        var registry = new FileSystemRegistry(mounts);
#pragma warning restore CA2000
        return new FileSystemService(
            registry, new RegistryCheckedPaths(), NullLogger<FileSystemService>.Instance, scope: null, internalAccess: true);
    }

    private static IEnumerable<FileSystemMount> Declared(IConfiguration configuration, string key, MountVisibility visibility)
    {
        foreach (var entry in configuration.GetSection($"Orkeon:FileSystem:{key}").GetChildren())
        {
            // A blank entry is one the host withdrew for this run (VFS-90).
            if (string.IsNullOrWhiteSpace(entry.Value))
                continue;

            var mount = FileSystemMount.Parse(entry.Value);
            yield return visibility == MountVisibility.AgentFacing
                ? mount
                : new FileSystemMount(mount.BasePath, mount.VirtualPath, mount.DefaultRights, mount.Overrides, visibility);
        }
    }

    /// <summary>
    /// The second check of <see cref="FileSystemService.ResolveAndValidate"/>, for plugin discovery: the
    /// registry already refused a path outside its mounts or their rights, and nothing else applies.
    /// </summary>
    private sealed class RegistryCheckedPaths : IPathValidator
    {
        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null)
            => PathValidationResult.Allowed(requestedPath);
    }
}
