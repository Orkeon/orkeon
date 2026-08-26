using Orkeon.Domain.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Compliance.Vfs;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.ConsoleApp.DependencyInjection;

/// <summary>
/// Bootstraps VFS mounts for crew directories supplied at startup (e.g. via <c>--crews-dir</c>)
/// and registers their virtual paths with <see cref="ScriptHostFacadeOptions.CrewDirectories"/>
/// so commands can resolve crews by name (e.g. <c>assistant</c> → <c>main-loop</c> →
/// <c>&lt;dir&gt;/main-loop/crew.ork.ts</c>). Sibling of <see cref="CliCommandMountBootstrapper"/>,
/// which does the same for <c>*.cmd.ts</c> command sources.
/// </summary>
/// <remarks>
/// Each physical path is mounted read-only at <c>/crews</c> (first dir) or <c>/crews-{N}</c>
/// (subsequent dirs). This realises the "host populates CrewDirectories from its mount bootstrap"
/// contract noted on <see cref="ScriptHostFacadeOptions"/>.
/// </remarks>
internal static class CliCrewMountBootstrapper
{
    /// <summary>Virtual path prefix used for the bootstrapped crew mounts.</summary>
    public const string VirtualPathPrefix = "/crews";

    /// <summary>
    /// Registers PostConfigure callbacks that append a read-only VFS mount per
    /// <paramref name="physicalPaths"/> entry AND extend
    /// <see cref="ScriptHostFacadeOptions.CrewDirectories"/> with the matching virtual paths.
    /// Safe to call with an empty list (no-op).
    /// </summary>
    [SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: user-supplied --crews-dir paths are resolved to absolute before being handed to the VFS mount parser. The path is registered as a mount root, not used for direct I/O.")]
    public static IServiceCollection AddScriptHostCrewMounts(
        this IServiceCollection services,
        IReadOnlyList<string> physicalPaths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(physicalPaths);

        if (physicalPaths.Count == 0)
            return services;

        var resolved = physicalPaths
            .Select((p, i) => new ResolvedMount(
                PhysicalPath: Path.GetFullPath(p),
                VirtualPath: i == 0 ? VirtualPathPrefix : $"{VirtualPathPrefix}-{i}"))
            .ToArray();

        // 1. Append read-only mounts to FileSystemOptions.Mounts before the registry is built.
        services.PostConfigure<FileSystemOptions>(opts =>
        {
            foreach (var m in resolved)
            {
                var entry = $"{FileSystemMount.Quote(m.PhysicalPath)}:{m.VirtualPath}:ro";
                if (!opts.Mounts.Contains(entry, StringComparer.Ordinal))
                    opts.Mounts.Add(entry);
            }
        });

        // 2. Register the virtual crew directories with the script-host facade so runCrew(name)
        //    resolves <dir>/<name>/crew.ork.ts.
        services.PostConfigure<ScriptHostFacadeOptions>(opts =>
        {
            var existing = opts.CrewDirectories.IsDefaultOrEmpty
                ? new List<string>()
                : opts.CrewDirectories.ToList();
            existing.AddRange(resolved
                .Select(m => m.VirtualPath)
                .Where(vp => !existing.Contains(vp, StringComparer.Ordinal)));
            opts.CrewDirectories = System.Collections.Immutable.ImmutableArray.CreateRange(existing);
        });

        return services;
    }

    private sealed record ResolvedMount(string PhysicalPath, string VirtualPath);
}
