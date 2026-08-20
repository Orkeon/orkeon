using System.Collections.Concurrent;
using System.Diagnostics;
using Orkeon.Application.Interfaces.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Monitoring;

/// <summary>
/// Captures completed <see cref="Activity"/> instances from Orkeon activity sources
/// using an <see cref="ActivityListener"/> and stores them in a circular buffer.
/// </summary>
public sealed partial class TraceExplorerService : ITraceExplorer, IDisposable
{
    private readonly ILogger<TraceExplorerService> _logger;
    private readonly int _maxTraceHistory;
    private readonly ActivityListener _activityListener;

    // Circular buffer of completed activities, keyed by TraceId + SpanId for uniqueness.
    private readonly ConcurrentQueue<ActivityRecord> _completedActivities = new();
    private int _currentCount;

    /// <summary>
    /// Initializes a new instance and starts listening to all Orkeon activity sources.
    /// </summary>
    public TraceExplorerService(
        ILogger<TraceExplorerService> logger,
        IOptions<MonitoringOptions> options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _maxTraceHistory = options?.Value?.MaxTraceHistory ?? 1000;
        var prefix = options?.Value?.TraceSourcePrefix ?? "Orkeon";

        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith(prefix, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped
        };

        ActivitySource.AddActivityListener(_activityListener);

        LogTraceexplorerserviceStartedListeningToOrkeon(_maxTraceHistory);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TraceInfo>> GetRecentTracesAsync(int limit = 50, CancellationToken ct = default)
    {
        var traces = _completedActivities
            .GroupBy(a => a.TraceId)
            .OrderByDescending(g => g.Max(a => a.StartTimeUtc))
            .Take(limit)
            .Select(g => ToTraceInfo(g.Key, g.ToList()))
            .ToList();

        return Task.FromResult<IReadOnlyList<TraceInfo>>(traces);
    }

    /// <inheritdoc />
    public Task<TraceDetail?> GetTraceByIdAsync(string traceId, CancellationToken ct = default)
    {
        var activities = _completedActivities
            .Where(a => a.TraceId == traceId)
            .ToList();

        if (activities.Count == 0)
            return Task.FromResult<TraceDetail?>(null);

        var root = activities.OrderBy(a => a.StartTimeUtc).First();
        var lastEnd = activities.Max(a => a.StartTimeUtc + a.Duration);

        var detail = new TraceDetail
        {
            TraceId = traceId,
            OperationName = root.OperationName,
            StartTime = root.StartTimeUtc,
            Duration = lastEnd - root.StartTimeUtc,
            Status = root.Status,
            Spans = activities.Select(a => new SpanInfo
            {
                SpanId = a.SpanId,
                OperationName = a.OperationName,
                StartTime = a.StartTimeUtc,
                Duration = a.Duration,
                ParentSpanId = a.ParentSpanId,
                Tags = a.Tags
            }).ToList()
        };

        return Task.FromResult<TraceDetail?>(detail);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TraceInfo>> SearchTracesAsync(TraceSearchCriteria criteria, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var groups = _completedActivities
            .GroupBy(a => a.TraceId);

        if (criteria.OperationName is not null)
        {
            groups = groups.Where(g =>
                g.Any(a => a.OperationName == criteria.OperationName));
        }

        if (criteria.From.HasValue)
        {
            groups = groups.Where(g =>
                g.Max(a => a.StartTimeUtc) >= criteria.From.Value);
        }

        if (criteria.To.HasValue)
        {
            groups = groups.Where(g =>
                g.Min(a => a.StartTimeUtc) <= criteria.To.Value);
        }

        if (criteria.Status is not null)
        {
            groups = groups.Where(g =>
                g.Any(a => a.Status == criteria.Status));
        }

        var result = groups
            .OrderByDescending(g => g.Max(a => a.StartTimeUtc))
            .Take(criteria.Limit)
            .Select(g => ToTraceInfo(g.Key, g.ToList()))
            .ToList();

        return Task.FromResult<IReadOnlyList<TraceInfo>>(result);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _activityListener.Dispose();
        LogTraceexplorerserviceDisposed();
    }

    private void OnActivityStopped(Activity activity)
    {
        var record = new ActivityRecord
        {
            TraceId = activity.TraceId.ToString(),
            SpanId = activity.SpanId.ToString(),
            ParentSpanId = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToString(),
            OperationName = activity.OperationName,
            StartTimeUtc = activity.StartTimeUtc,
            Duration = activity.Duration,
            Status = activity.Status == ActivityStatusCode.Error ? "Error" : "OK",
            Tags = activity.Tags.ToDictionary(t => t.Key, t => t.Value ?? "")
        };

        _completedActivities.Enqueue(record);
        var count = Interlocked.Increment(ref _currentCount);

        // Evict oldest entries when buffer exceeds max size
        while (count > _maxTraceHistory && _completedActivities.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _currentCount);
        }
    }

    private static TraceInfo ToTraceInfo(string traceId, List<ActivityRecord> activities)
    {
        var root = activities.OrderBy(a => a.StartTimeUtc).First();
        var lastEnd = activities.Max(a => a.StartTimeUtc + a.Duration);

        return new TraceInfo
        {
            TraceId = traceId,
            OperationName = root.OperationName,
            StartTime = root.StartTimeUtc,
            Duration = lastEnd - root.StartTimeUtc,
            Status = root.Status,
            SpanCount = activities.Count
        };
    }

    /// <summary>
    /// Internal record for storing completed activity data.
    /// </summary>
    private sealed class ActivityRecord
    {
        public required string TraceId { get; init; }
        public required string SpanId { get; init; }
        public string? ParentSpanId { get; init; }
        public required string OperationName { get; init; }
        public DateTime StartTimeUtc { get; init; }
        public TimeSpan Duration { get; init; }
        public required string Status { get; init; }
        public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "TraceExplorerService started listening to Orkeon activity sources (max history: {Max})")]
    private partial void LogTraceexplorerserviceStartedListeningToOrkeon(int max);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "TraceExplorerService disposed")]
    private partial void LogTraceexplorerserviceDisposed();

}
