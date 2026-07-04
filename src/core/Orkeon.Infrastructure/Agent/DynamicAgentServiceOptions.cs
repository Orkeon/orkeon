namespace Orkeon.Infrastructure.Agent;

/// <summary>
/// Configuration for <see cref="DynamicAgentService"/> controlling whether crews may spawn
/// dynamic agents and how many concurrent dynamic agents they may hold.
/// Resolution priority for a given crew: per-crew override &gt; global default.
/// </summary>
public sealed class DynamicAgentServiceOptions
{
    /// <summary>
    /// Whether crews allow dynamic agent creation by default when no per-crew override is set.
    /// Defaults to <see langword="true"/> to preserve existing behaviour.
    /// </summary>
    public bool AllowDynamicAgentsByDefault { get; set; } = true;

    /// <summary>
    /// Default maximum number of concurrent dynamic agents per crew when no per-crew override is set.
    /// <see langword="null"/> means unlimited.
    /// </summary>
    public int? DefaultMaxConcurrentDynamicAgents { get; set; }

    /// <summary>
    /// Per-crew overrides keyed by the crew identifier string (<c>CrewId.Value.ToString()</c>).
    /// When present, an entry fully supersedes the global defaults for that crew.
    /// </summary>
    public Dictionary<string, DynamicAgentCrewPolicy> CrewOverrides { get; } = [];
}

/// <summary>
/// Per-crew dynamic-agent policy: whether the crew may spawn dynamic agents and an optional
/// concurrency cap.
/// </summary>
public sealed class DynamicAgentCrewPolicy
{
    /// <summary>Whether this crew is allowed to spawn dynamic agents.</summary>
    public bool AllowDynamicAgents { get; set; } = true;

    /// <summary>
    /// Maximum number of concurrent dynamic agents for this crew, or <see langword="null"/> for unlimited.
    /// </summary>
    public int? MaxConcurrentDynamicAgents { get; set; }
}
