using Microsoft.Extensions.DependencyInjection;
using Orkeon.Compliance.Vfs;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.ConsoleApp.DependencyInjection;

/// <summary>
/// Bootstraps arbitrary VFS mounts supplied at startup via <c>--mount
/// &lt;physical:virtual:rights&gt;</c>. This is how the coding-agent REPL is given a
/// <c>/workspace</c> (and e.g. <c>/output</c>) to read and edit, mirroring
/// <c>Scripting.Cli</c>'s <c>-m</c> flag. Sibling of <see cref="CliCommandMountBootstrapper"/>,
/// which handles the read-only <c>/cli-commands</c> source dirs.
/// </summary>
/// <remarks>
/// Each spec is appended to <see cref="FileSystemOptions.Mounts"/> via a PostConfigure callback
/// (so it stacks on top of any appsettings-declared mounts rather than replacing them). The
/// leading physical segment is resolved to an absolute path; the rest of the spec
/// (<c>:virtual:rights[;subpath:rights;...]</c>) is preserved verbatim for
/// <c>FileSystemMount.Parse</c>.
/// </remarks>
internal static class CliWorkspaceMountBootstrapper
{
    /// <summary>
    /// Registers a PostConfigure callback that appends each <paramref name="mountSpecs"/> entry to
    /// <see cref="FileSystemOptions.Mounts"/>. Safe to call with an empty list (no-op).
    /// </summary>
    [SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: the physical segment of a user-supplied --mount spec is resolved to absolute before being handed to the VFS mount parser. It is registered as a mount root, not used for direct framework I/O.")]
    public static IServiceCollection AddCliWorkspaceMounts(
        this IServiceCollection services,
        IReadOnlyList<string> mountSpecs)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(mountSpecs);

        if (mountSpecs.Count == 0)
            return services;

        var resolved = mountSpecs.Select(ResolvePhysicalSegment).ToArray();

        services.PostConfigure<FileSystemOptions>(opts =>
        {
            foreach (var entry in resolved)
            {
                if (!opts.Mounts.Contains(entry, StringComparer.Ordinal))
                    opts.Mounts.Add(entry);
            }
        });

        return services;
    }

    /// <summary>
    /// Resolves the physical path segment (everything before the first <c>:</c>) of a mount spec
    /// to an absolute path, leaving the <c>:virtual:rights[;...]</c> remainder untouched. A spec
    /// without a separating colon is returned verbatim so <c>FileSystemMount.Parse</c> can surface
    /// a precise format error.
    /// </summary>
    [SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: resolves a user-supplied --mount physical path to absolute for the VFS mount parser, before any VFS mount exists.")]
    private static string ResolvePhysicalSegment(string spec)
    {
        var firstColon = spec.IndexOf(':', StringComparison.Ordinal);
        if (firstColon <= 0)
            return spec;

        var physical = spec[..firstColon];
        var remainder = spec[firstColon..];
        return Path.GetFullPath(physical) + remainder;
    }
}
