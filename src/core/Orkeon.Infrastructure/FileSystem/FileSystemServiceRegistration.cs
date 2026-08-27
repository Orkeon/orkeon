using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
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

        // 1b. The sandbox mount travels with the file system, not with a hosted service: the
        //     runners build a host and never start it, so a hosted service provisioned
        //     /sandbox in the daemon alone while every CLI registered the code-execution
        //     subsystem over a virtual root that did not exist.
        services.Configure<SandboxFileSystemOptions>(configuration.GetSection("Orkeon:Sandbox"));
        services.TryAddSingleton<SandboxSession>();

        // 2. Register FileSystemRegistry as singleton (built from parsed mounts)
        services.AddSingleton<FileSystemRegistry>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FileSystemOptions>>().Value;

            if (options.Mounts.Count == 0 && options.InternalMounts.Count == 0)
                throw new InvalidOperationException(
                    "At least one file system mount must be configured in 'Orkeon:FileSystem:Mounts'.");

            var mounts = new List<FileSystemMount>();
            foreach (var mountStr in options.Mounts)
                mounts.Add(ParseAndCheck(mountStr, MountVisibility.AgentFacing));

            // Infrastructure mounts: resolvable, never listed to an agent (ADR-008).
            foreach (var mountStr in options.InternalMounts)
                mounts.Add(ParseAndCheck(mountStr, MountVisibility.Internal));

            // The per-process sandbox: also Internal, and provisioned by the session object
            // itself so the directory exists before the registry validates it.
            mounts.Add(sp.GetRequiredService<SandboxSession>().Mount);

            return new FileSystemRegistry(mounts);

            static FileSystemMount ParseAndCheck(string mountStr, MountVisibility visibility)
            {
                var parsed = FileSystemMount.Parse(mountStr);

                // Validate base path exists (Infrastructure responsibility)
                if (!Directory.Exists(parsed.BasePath))
                    throw new DirectoryNotFoundException(
                        $"Mount base path does not exist for virtual path '{parsed.VirtualPath}'.");

                return visibility == MountVisibility.AgentFacing
                    ? parsed
                    : new FileSystemMount(
                        parsed.BasePath, parsed.VirtualPath, parsed.DefaultRights, parsed.Overrides, visibility);
            }
        });

        // 3. Register the ambient per-execution mount scope (P2-O-05). Singleton so the host that
        //    enters a scoped registry and the singleton FileSystemService that reads it share the
        //    same AsyncLocal slot. A host that never enters a scope keeps the boot mounts unchanged.
        services.TryAddSingleton<IFileSystemScope, AsyncLocalFileSystemScope>();

        // 4. Register FileSystemService as the IFileSystemService implementation. This is the
        //    instance every tool and every agent reaches, so it does NOT resolve Internal
        //    mounts: /llm-logs and /sandbox are refused to it exactly as if they did not exist.
        services.AddSingleton<IFileSystemService>(sp => new FileSystemService(
            sp.GetRequiredService<FileSystemRegistry>(),
            sp.GetRequiredService<IPathValidator>(),
            sp.GetRequiredService<ILogger<FileSystemService>>(),
            sp.GetService<IFileSystemScope>()));

        // 4b. The privileged view, for the components that write to an internal root — the LLM
        //     exchange logger and the code sandboxes. Asked for by name, never injected as
        //     IFileSystemService, so nothing reaches it by accident.
        services.TryAddSingleton(sp => new PrivilegedFileSystemAccess(new FileSystemService(
            sp.GetRequiredService<FileSystemRegistry>(),
            sp.GetRequiredService<IPathValidator>(),
            sp.GetRequiredService<ILogger<FileSystemService>>(),
            sp.GetService<IFileSystemScope>(),
            internalAccess: true)));

        // 5. Register VirtualFileSystemWatcher (transient: each caller owns one watcher lifetime)
        services.AddTransient<IVirtualFileSystemWatcher, VirtualFileSystemWatcher>();

        return services;
    }

}
