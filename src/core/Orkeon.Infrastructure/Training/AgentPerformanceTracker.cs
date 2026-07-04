using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Training;

namespace Orkeon.Infrastructure.Training;

/// <summary>
/// In-memory implementation of <see cref="IAgentPerformanceTracker"/> using concurrent dictionaries.
/// </summary>
public sealed class AgentPerformanceTracker : IAgentPerformanceTracker
{
    private readonly ConcurrentDictionary<string, List<PerformanceDataPoint>> _data = new();

    /// <inheritdoc />
    public Task RecordPerformanceAsync(
        string agentId,
        string taskId,
        double score,
        IDictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        var readOnlyMetadata = metadata != null
            ? new Dictionary<string, object>(metadata).AsReadOnly()
            : null;

        var dataPoint = new PerformanceDataPoint(
            taskId,
            score,
            DateTime.UtcNow,
            readOnlyMetadata as IReadOnlyDictionary<string, object>);

        var points = _data.GetOrAdd(agentId, _ => []);
        lock (points)
        {
            points.Add(dataPoint);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PerformanceTrend> GetPerformanceTrendAsync(
        string agentId,
        int lastN = 10,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        if (!_data.TryGetValue(agentId, out var allPoints))
        {
            return Task.FromResult(new PerformanceTrend(
                agentId,
                Array.Empty<PerformanceDataPoint>(),
                AverageScore: 0.0,
                Improvement: 0.0));
        }

        List<PerformanceDataPoint> snapshot;
        lock (allPoints)
        {
            snapshot = allPoints
                .OrderBy(p => p.RecordedAt)
                .TakeLast(lastN)
                .ToList();
        }

        if (snapshot.Count == 0)
        {
            return Task.FromResult(new PerformanceTrend(
                agentId,
                Array.Empty<PerformanceDataPoint>(),
                AverageScore: 0.0,
                Improvement: 0.0));
        }

        var averageScore = snapshot.Average(p => p.Score);
        var improvement = snapshot.Count >= 2
            ? snapshot[^1].Score - snapshot[0].Score
            : 0.0;

        return Task.FromResult(new PerformanceTrend(
            agentId,
            snapshot.AsReadOnly(),
            Math.Round(averageScore, 4),
            Math.Round(improvement, 4)));
    }
}
