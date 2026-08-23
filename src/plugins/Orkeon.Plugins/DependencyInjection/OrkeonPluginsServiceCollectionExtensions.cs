using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Plugins;

/// <summary>
/// DI extension methods activating the Orkeon plugin system.
/// <para>
/// Opt-in subsystem (same pattern as R4.9 — <c>docs/reference/opt-in-subsystems.md</c>):
/// plugins are NOT loaded by <c>AddOrkeonApplication()</c> / <c>AddOrkeonInfrastructure()</c>.
/// Hosts that want directory-based plugins call <c>AddOrkeonPlugins(...)</c> explicitly,
/// once, during startup.
/// </para>
/// <para>
/// <b>Trust boundary</b>: every discovered assembly is loaded and executed with the
/// full privileges of the host process (no sandbox in v1). Only point
/// <see cref="OrkeonPluginsOptions.Directory"/> at a directory whose content you trust.
/// See <c>docs/architecture/plugins.md</c>.
/// </para>
/// </summary>
public static class OrkeonPluginsServiceCollectionExtensions
{
    /// <summary>Configuration section read by the <see cref="IConfiguration"/> overload.</summary>
    public const string ConfigurationSection = "Plugins";

    /// <summary>
    /// Discovers, loads, and activates plugins using options bound from the
    /// <c>"Plugins"</c> configuration section.
    /// </summary>
    /// <param name="services">The service collection plugins contribute to.</param>
    /// <param name="fileSystem">
    /// The virtual file system used for discovery and physical-path resolution. Passed
    /// explicitly because plugin loading happens at registration time, before the
    /// container is built (same bootstrap stage that provisions the VFS mounts).
    /// </param>
    /// <param name="configuration">Host configuration (section <c>"Plugins"</c>).</param>
    public static IServiceCollection AddOrkeonPlugins(
        this IServiceCollection services,
        IFileSystemService fileSystem,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new OrkeonPluginsOptions();
        configuration.GetSection(ConfigurationSection).Bind(options);
        return services.AddOrkeonPluginsCore(fileSystem, options);
    }

    /// <summary>
    /// Discovers, loads, and activates plugins using options configured through the
    /// optional delegate (defaults apply when omitted: directory <c>/plugins</c>,
    /// pattern <c>*.dll</c>, fail-fast).
    /// </summary>
    /// <param name="services">The service collection plugins contribute to.</param>
    /// <param name="fileSystem">
    /// The virtual file system used for discovery and physical-path resolution.
    /// </param>
    /// <param name="configure">Optional options configuration.</param>
    public static IServiceCollection AddOrkeonPlugins(
        this IServiceCollection services,
        IFileSystemService fileSystem,
        Action<OrkeonPluginsOptions>? configure = null)
    {
        var options = new OrkeonPluginsOptions();
        configure?.Invoke(options);
        return services.AddOrkeonPluginsCore(fileSystem, options);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "The PluginRegistry (IDisposable) is registered as an instance singleton and deliberately lives for the whole process: it keeps the plugin AssemblyLoadContexts loaded (MS.DI never disposes instances it did not create). Disposing it early would unload assemblies still referenced by registered services.")]
    private static IServiceCollection AddOrkeonPluginsCore(
        this IServiceCollection services,
        IFileSystemService fileSystem,
        OrkeonPluginsOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var discovery = new PluginAssemblyDiscovery(fileSystem);

        // Plugins contribute registrations to THIS IServiceCollection, so discovery and
        // loading are eager, at registration time (bootstrap, before the provider is
        // built). Blocking is safe here: hosts call AddOrkeonPlugins during startup,
        // outside any synchronization context.
        var candidates = discovery.DiscoverAsync(options, CancellationToken.None)
            .GetAwaiter().GetResult();

        var loader = new PluginLoader(options.SharedAssemblyPrefixes);
        var registry = new PluginRegistry();

        foreach (var candidate in candidates)
        {
            try
            {
                LoadCandidate(services, loader, registry, candidate);
            }
            catch (PluginLoadException ex) when (options.ContinueOnError)
            {
                registry.AddFailure(new PluginLoadFailure
                {
                    VirtualPath = candidate.VirtualPath,
                    Reason = ex.Message,
                });
            }
        }

        services.AddSingleton<IPluginRegistry>(registry);
        return services;
    }

    private static void LoadCandidate(
        IServiceCollection services,
        PluginLoader loader,
        PluginRegistry registry,
        PluginAssemblyCandidate candidate)
    {
        var loaded = loader.Load(candidate);

        if (loaded.Plugins.Count == 0)
        {
            // Not a plugin assembly (e.g. a dependency dropped flat) — release it.
            loaded.Unload();
            return;
        }

        foreach (var plugin in loaded.Plugins)
        {
            try
            {
                plugin.ConfigureServices(services);
            }
            catch (Exception ex)
            {
                // No rollback of registrations already contributed by this plugin —
                // with ContinueOnError the host accepts a partially configured set.
                loaded.Unload();
                throw new PluginLoadException(
                    $"Plugin '{plugin.Name}' ({candidate.VirtualPath}) failed in ConfigureServices. See inner exception for details.",
                    candidate.VirtualPath,
                    ex);
            }
        }

        registry.Add(loaded);
    }
}
