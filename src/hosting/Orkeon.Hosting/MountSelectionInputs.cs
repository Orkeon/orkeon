using Microsoft.Extensions.Configuration;

namespace Orkeon.Hosting;

/// <summary>The mount declarations a selection guard reads, and what its lines name them by.</summary>
/// <param name="Configuration">The configuration the host is built over, before its own mounts.</param>
/// <param name="Label">The settings file, or the files separated by a comma; null for the configuration alone.</param>
internal sealed record MountLayers(IConfiguration Configuration, string? Label);

/// <summary>The mounts a tool host is asked for on its command line.</summary>
/// <param name="CliMounts">The <c>--mount</c> values.</param>
/// <param name="MountIds">The <c>--mount-id</c> values, unparsed.</param>
/// <param name="AllowExternalMounts">Whether a mount outside the working directory is allowed.</param>
/// <param name="Offered">What the command offers to choose between the entries of a root.</param>
internal sealed record ToolHostMounts(
    IReadOnlyList<string> CliMounts,
    IEnumerable<string> MountIds,
    bool AllowExternalMounts,
    OfferedMountSelectors Offered);
