namespace Orkeon.Domain.Constants.Orchestration;

/// <summary>
/// Default values for orchestration, state management, and monitoring intervals.
/// Centralises orchestration-related magic values used across the Orkeon platform.
/// </summary>
public static class OrchestrationDefaults
{
    /// <summary>Default interval between execution-state cleanup passes (5 min).</summary>
    public static readonly TimeSpan DefaultCleanupInterval = TimeSpan.FromMinutes(5);

    /// <summary>Default research task maximum duration (30 min).</summary>
    public static readonly TimeSpan DefaultResearchMaxDuration = TimeSpan.FromMinutes(30);

    /// <summary>Default measurement period for performance metrics (1 h).</summary>
    public static readonly TimeSpan DefaultMeasurementPeriod = TimeSpan.FromHours(1);
}
