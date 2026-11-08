using Microsoft.Extensions.Configuration;
using Orkeon.Compliance.Vfs;
using Orkeon.Constants.FileSystem;

namespace Orkeon.Hosting;

/// <summary>
/// The folder of the durable checkpoint store: when <c>Orkeon:Checkpointing</c> is enabled with the
/// <c>json</c> or <c>sqlite</c> store, <c>./.orkeon/state</c> is mounted at
/// <see cref="RunnerVirtualRoots.State"/> as an internal mount, so the store reaches it through the VFS
/// and no agent can address it. Planned where every runner host plans its mounts
/// (<c>RunnerHost.ConfigureAppConfiguration</c>), so <c>orkeon run</c> on a crew or a script,
/// <c>orkeon-host</c> and <c>orkeon doctor</c> all get it.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: creates the state folder before the VFS mount that hosts it is provisioned.")]
internal static class RunnerState
{
    /// <summary>The folder the state is kept in, under the working directory.</summary>
    internal const string Directory = ".orkeon/state";

    /// <summary>
    /// The sentence to warn with when the settings name a durable store without enabling checkpointing
    /// (the store is then ignored and the sessions stay in memory), or null.
    /// </summary>
    internal static string? IgnoredStoreWarning(IConfigurationSection? section)
    {
        if (section is null || string.Equals(section["Enabled"], "true", StringComparison.OrdinalIgnoreCase))
            return null;

        var store = section["Store"];
        if (string.IsNullOrWhiteSpace(store) || string.Equals(store, "memory", StringComparison.OrdinalIgnoreCase))
            return null;

        return $"{section.Path}:Store is '{store}' but {section.Path}:Enabled is not true: checkpoint sessions stay in memory "
            + $"and nothing survives the process. Set {section.Path}:Enabled to true to use the {store} store.";
    }

    /// <summary>
    /// The physical state folder the checkpoint store needs, created, or null when it needs none: the
    /// section is absent or disabled, the store is not a file store, a Sqlite connection string names
    /// its own place, or the operator mounts <see cref="RunnerVirtualRoots.State"/> themselves.
    /// </summary>
    /// <param name="section">The <c>Orkeon:Checkpointing</c> section of the declared layers, or null.</param>
    /// <param name="cwd">The working directory; the process's when null.</param>
    /// <param name="mountRoots">The virtual roots already mounted by the operator.</param>
    internal static string? Plan(IConfigurationSection? section, string? cwd, IEnumerable<string> mountRoots)
    {
        ArgumentNullException.ThrowIfNull(mountRoots);
        if (section is null || !string.Equals(section["Enabled"], "true", StringComparison.OrdinalIgnoreCase))
            return null;

        var store = section["Store"];
        var json = string.Equals(store, "json", StringComparison.OrdinalIgnoreCase);
        var sqlite = string.Equals(store, "sqlite", StringComparison.OrdinalIgnoreCase);
        if (!json && !sqlite)
            return null;

        // A connection string names its own place; only the default and a virtual path need the mount.
        if (sqlite && !string.IsNullOrWhiteSpace(section["ConnectionString"]))
            return null;

        if (mountRoots.Contains(RunnerVirtualRoots.State, StringComparer.Ordinal))
            return null;

        var physical = System.IO.Path.GetFullPath(Directory, cwd ?? System.IO.Directory.GetCurrentDirectory());
        System.IO.Directory.CreateDirectory(physical);
        return physical;
    }
}
