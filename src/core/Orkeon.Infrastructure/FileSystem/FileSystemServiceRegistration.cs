using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Sandbox;

namespace Orkeon.Infrastructure.FileSystem;

/// <summary>
/// Extension methods for registering the virtual file system services.
/// </summary>
public static class FileSystemServiceRegistration
{
    /// <summary>
    /// Registers the virtual file system services (registry, service, options) into the DI container.
    /// Reads mount definitions from the "Orkeon:FileSystem" configuration section.
    /// </summary>
    public static IServiceCollection AddOrkeonFileSystem(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 1. Bind FileSystemOptions from configuration section "Orkeon:FileSystem"
        services.Configure<FileSystemOptions>(configuration.GetSection("Orkeon:FileSystem"));

        // 2. Register FileSystemRegistry as singleton (built from parsed mounts)
        services.AddSingleton<FileSystemRegistry>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FileSystemOptions>>().Value;

            if (options.Mounts.Count == 0)
                throw new InvalidOperationException(
                    "At least one file system mount must be configured in 'Orkeon:FileSystem:Mounts'.");

            var mounts = new List<FileSystemMount>();
            foreach (var mountStr in options.Mounts)
            {
                var mount = FileSystemMount.Parse(mountStr);

                // Validate base path exists (Infrastructure responsibility)
                if (!Directory.Exists(mount.BasePath))
                    throw new DirectoryNotFoundException(
                        $"Mount base path does not exist for virtual path '{mount.VirtualPath}'.");

                mounts.Add(mount);
            }

            return new FileSystemRegistry(mounts);
        });

        // 3. Register the ambient per-execution mount scope (P2-O-05). Singleton so the host that
        //    enters a scoped registry and the singleton FileSystemService that reads it share the
        //    same AsyncLocal slot. A host that never enters a scope keeps the boot mounts unchanged.
        services.TryAddSingleton<IFileSystemScope, AsyncLocalFileSystemScope>();

        // 4. Register FileSystemService as the IFileSystemService implementation
        services.AddSingleton<IFileSystemService, FileSystemService>();

        // 5. Register VirtualFileSystemWatcher (transient: each caller owns one watcher lifetime)
        services.AddTransient<IVirtualFileSystemWatcher, VirtualFileSystemWatcher>();

        return services;
    }

    /// <summary>
    /// Registers the sandbox mount bootstrapper, which provisions a per-session
    /// <c>/sandbox</c> directory with <see cref="MountVisibility.Internal"/> visibility
    /// and runs a janitor sweep on startup.
    /// Reads sandbox options from the "Orkeon:Sandbox" configuration section.
    /// </summary>
    /// <remarks>
    /// Call this after <see cref="AddOrkeonFileSystem"/> so that <see cref="FileSystemRegistry"/>
    /// is already registered before the bootstrapper resolves it.
    /// </remarks>
    public static IServiceCollection AddSandboxMount(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind SandboxFileSystemOptions from "Orkeon:Sandbox"
        services.Configure<SandboxFileSystemOptions>(configuration.GetSection("Orkeon:Sandbox"));

        // Register the hosted service that provisions the sandbox mount at startup
        services.AddHostedService<SandboxMountBootstrapper>();

        return services;
    }
}
