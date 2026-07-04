namespace Orkeon.Infrastructure.Monitoring;

/// <summary>
/// Configuration options for the monitoring backend.
/// Bind to <c>Orkeon:Monitoring</c> in appsettings.json.
/// </summary>
public class MonitoringOptions
{
    /// <summary>
    /// Maximum number of completed traces kept in the circular buffer.
    /// Older traces are evicted when this limit is reached.
    /// </summary>
    public int MaxTraceHistory { get; set; } = 1000;

    /// <summary>
    /// Number of minutes to retain detailed metrics data.
    /// </summary>
    public int MetricsRetentionMinutes { get; set; } = 60;
}
