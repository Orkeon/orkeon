namespace Orkeon.Plugins;

/// <summary>
/// Records a plugin assembly that failed to load (or to configure its services) when
/// <see cref="OrkeonPluginsOptions.ContinueOnError"/> is enabled.
/// Exposed through <see cref="IPluginRegistry.Failures"/>.
/// </summary>
public sealed record PluginLoadFailure
{
    /// <summary>Virtual path of the assembly that failed.</summary>
    public required string VirtualPath { get; init; }

    /// <summary>Human-readable failure reason (virtual paths only, no physical paths).</summary>
    public required string Reason { get; init; }
}
