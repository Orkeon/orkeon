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

    /// <summary>
    /// Prefix of the activity-source names whose traces are captured. Defaults to every Orkeon
    /// source; narrow it to watch one subsystem — or, in a test, to a name nothing else emits,
    /// since the listener is process-wide and would otherwise pick up unrelated activity.
    /// </summary>
    public string TraceSourcePrefix { get; set; } = "Orkeon";
}
