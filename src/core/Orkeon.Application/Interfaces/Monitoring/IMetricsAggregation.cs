namespace Orkeon.Application.Interfaces.Monitoring;

/// <summary>
/// Provides aggregated metrics captured from Orkeon meter instruments.
/// Implementations listen to the <c>Orkeon</c> meter and expose
/// current counter values and histogram summaries.
/// </summary>
public interface IMetricsAggregation
{
    /// <summary>
    /// Returns the current aggregated metrics (counters, active gauges, cost).
    /// </summary>
    System.Threading.Tasks.Task<AggregatedMetrics> GetCurrentMetricsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a full snapshot that includes raw counter and histogram data.
    /// </summary>
    System.Threading.Tasks.Task<MetricsSnapshot> GetMetricsSnapshotAsync(CancellationToken ct = default);
}
