using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Scripting.Configuration;
using Orkeon.Compliance.Vfs;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.ConsoleApp.DependencyInjection;

/// <summary>
/// Bootstraps VFS mounts for scripted-command directories supplied at startup (e.g. via
/// <c>--commands-dir</c>). Each physical path is mounted read-only at
/// <c>/cli-commands</c> (first dir) or <c>/cli-commands-{N}</c> (subsequent dirs), and
/// the corresponding virtual paths are appended to
/// <see cref="ScriptCommandsConfiguration.Directories"/>.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the <c>Orkeon.ConsoleApp</c> composition root rather than the
/// <c>Orkeon.Cli.Scripting</c> package because it depends on
/// <see cref="FileSystemOptions"/> from <c>Orkeon.Infrastructure</c> — the package
/// itself must not know about the infrastructure layer (Clean Architecture).
/// </para>
/// <para>
/// Option <em>(a)</em> of plan §Q7: bootstrap mounts at startup via
/// <see cref="OptionsServiceCollectionExtensions.PostConfigure{TOptions}(IServiceCollection, Action{TOptions})"/>
/// callbacks. The previous in-memory configuration overlay approach (option c) was
/// replaced by this so the dependency graph is explicit.
/// </para>
/// </remarks>
internal static class CliCommandMountBootstrapper
{
    /// <summary>Virtual path prefix used for the bootstrapped mounts.</summary>
    public const string VirtualPathPrefix = "/cli-commands";

    /// <summary>
    /// Registers PostConfigure callbacks that append a VFS mount per <paramref name="physicalPaths"/>
    /// entry AND extend <see cref="ScriptCommandsConfiguration.Directories"/> with the
    /// matching virtual paths. Safe to call with an empty list (no-op).
    /// </summary>
    [SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: user-supplied --commands-dir paths are resolved to absolute before being handed to the VFS mount parser. The path is registered as a mount root, not used for direct I/O.")]
    public static IServiceCollection AddScriptCommandMounts(
        this IServiceCollection services,
        IReadOnlyList<string> physicalPaths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(physicalPaths);

        if (physicalPaths.Count == 0)
            return services;

        var resolved = physicalPaths
            .Select((p, i) => new ResolvedMount(
                Index: i,
                PhysicalPath: Path.GetFullPath(p),
                VirtualPath: i == 0 ? VirtualPathPrefix : $"{VirtualPathPrefix}-{i}"))
            .ToArray();

        // 1. Append the mount strings to FileSystemOptions.Mounts BEFORE the registry is built.
        services.PostConfigure<FileSystemOptions>(opts =>
        {
            foreach (var m in resolved)
            {
                var entry = $"{m.PhysicalPath}:{m.VirtualPath}:ro";
                if (!opts.Mounts.Contains(entry, StringComparer.Ordinal))
                    opts.Mounts.Add(entry);
            }
        });

        // 2. Append the virtual paths to ScriptCommandsConfiguration.Directories so the
        //    loader scans them. Use PostConfigure so any appsettings entries are kept.
        services.PostConfigure<ScriptCommandsConfiguration>(opts =>
        {
            var existing = opts.Directories.IsDefault ? new List<string>() : opts.Directories.ToList();
            existing.AddRange(resolved
                .Select(m => m.VirtualPath)
                .Where(vp => !existing.Contains(vp, StringComparer.Ordinal)));
            opts.Directories = System.Collections.Immutable.ImmutableArray.CreateRange(existing);
        });

        return services;
    }

    private sealed record ResolvedMount(int Index, string PhysicalPath, string VirtualPath);
}
