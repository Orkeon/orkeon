namespace Orkeon.Application.Interfaces.Monitoring;

/// <summary>
/// Aggregated metrics captured from the Orkeon meter instruments.
/// </summary>
public sealed record AggregatedMetrics
{
    /// <summary>Total number of LLM API calls recorded.</summary>
    public long TotalLlmCalls { get; init; }

    /// <summary>Total number of tool executions recorded.</summary>
    public long TotalToolExecutions { get; init; }

    /// <summary>Total number of task executions recorded.</summary>
    public long TotalTaskExecutions { get; init; }

    /// <summary>Total number of crew executions recorded.</summary>
    public long TotalCrewExecutions { get; init; }

    /// <summary>Number of currently active crews.</summary>
    public long ActiveCrews { get; init; }

    /// <summary>Number of currently active tasks.</summary>
    public long ActiveTasks { get; init; }

    /// <summary>Total estimated cost in USD.</summary>
    public double TotalCostUsd { get; init; }

    /// <summary>Timestamp when the metrics were captured.</summary>
    public DateTime CapturedAt { get; init; }
}

/// <summary>
/// A full snapshot of all metrics including raw counter and histogram data.
/// </summary>
public sealed record MetricsSnapshot
{
    /// <summary>The aggregated metrics at snapshot time.</summary>
    public AggregatedMetrics Metrics { get; init; } = new();

    /// <summary>Raw counter values indexed by instrument name.</summary>
    public IReadOnlyDictionary<string, long> CountersByName { get; init; } =
        new Dictionary<string, long>();

    /// <summary>Average values for histogram instruments indexed by instrument name.</summary>
    public IReadOnlyDictionary<string, double> HistogramAverages { get; init; } =
        new Dictionary<string, double>();

    /// <summary>Timestamp when the snapshot was taken.</summary>
    public DateTime SnapshotAt { get; init; }
}

/// <summary>
/// Summary information about a distributed trace.
/// </summary>
public sealed record TraceInfo
{
    /// <summary>Unique identifier for the trace.</summary>
    public string TraceId { get; init; } = "";

    /// <summary>Name of the root operation.</summary>
    public string OperationName { get; init; } = "";

    /// <summary>When the trace started.</summary>
    public DateTime StartTime { get; init; }

    /// <summary>Total duration of the trace.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Overall status of the trace (e.g., "OK", "Error").</summary>
    public string Status { get; init; } = "";

    /// <summary>Number of spans within the trace.</summary>
    public int SpanCount { get; init; }
}

/// <summary>
/// Detailed information about a trace including all its spans.
/// </summary>
public sealed record TraceDetail
{
    /// <summary>Unique identifier for the trace.</summary>
    public string TraceId { get; init; } = "";

    /// <summary>Name of the root operation.</summary>
    public string OperationName { get; init; } = "";

    /// <summary>When the trace started.</summary>
    public DateTime StartTime { get; init; }

    /// <summary>Total duration of the trace.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Overall status of the trace.</summary>
    public string Status { get; init; } = "";

    /// <summary>All spans belonging to this trace.</summary>
    public IReadOnlyList<SpanInfo> Spans { get; init; } = [];
}

/// <summary>
/// Information about a single span within a trace.
/// </summary>
public sealed record SpanInfo
{
    /// <summary>Unique identifier for the span.</summary>
    public string SpanId { get; init; } = "";

    /// <summary>Name of the operation this span represents.</summary>
    public string OperationName { get; init; } = "";

    /// <summary>When the span started.</summary>
    public DateTime StartTime { get; init; }

    /// <summary>Duration of the span.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Parent span ID, or null if this is a root span.</summary>
    public string? ParentSpanId { get; init; }

    /// <summary>Tags (attributes) associated with the span.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Criteria for searching traces.
/// </summary>
public sealed record TraceSearchCriteria
{
    /// <summary>Filter by operation name (exact match).</summary>
    public string? OperationName { get; init; }

    /// <summary>Filter traces starting from this time.</summary>
    public DateTime? From { get; init; }

    /// <summary>Filter traces up to this time.</summary>
    public DateTime? To { get; init; }

    /// <summary>Filter by status (e.g., "OK", "Error").</summary>
    public string? Status { get; init; }

    /// <summary>Maximum number of traces to return.</summary>
    public int Limit { get; init; } = 50;
}
