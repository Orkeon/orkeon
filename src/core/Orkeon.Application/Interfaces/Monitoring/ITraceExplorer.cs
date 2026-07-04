namespace Orkeon.Application.Interfaces.Monitoring;

/// <summary>
/// Provides access to distributed traces captured from Orkeon activity sources.
/// Implementations listen to <c>Orkeon.*</c> ActivitySources and store
/// completed activities in a circular buffer for inspection.
/// </summary>
public interface ITraceExplorer
{
    /// <summary>
    /// Returns the most recent traces, up to <paramref name="limit"/>.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<TraceInfo>> GetRecentTracesAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Returns the detailed trace for the given <paramref name="traceId"/>,
    /// or <c>null</c> if not found.
    /// </summary>
    System.Threading.Tasks.Task<TraceDetail?> GetTraceByIdAsync(string traceId, CancellationToken ct = default);

    /// <summary>
    /// Searches traces matching the given <paramref name="criteria"/>.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<TraceInfo>> SearchTracesAsync(TraceSearchCriteria criteria, CancellationToken ct = default);
}
