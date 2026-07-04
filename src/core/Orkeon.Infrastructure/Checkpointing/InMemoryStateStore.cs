using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IStateStore"/>.
/// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> for storage.
/// Supports time-travel via an append-only version history.
/// </summary>
public sealed class InMemoryStateStore : IStateStore
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();
    private readonly ConcurrentDictionary<string, List<VersionedState>> _history = new();
    private readonly object _historyLock = new();

    /// <inheritdoc />
    public Task SaveAsync(SessionState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        _sessions.AddOrUpdate(state.SessionId, state, (_, _) => state);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SessionState?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        _sessions.TryGetValue(sessionId, out var state);
        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public Task<SessionState?> GetLatestForCrewAsync(string crewId, CancellationToken ct = default)
    {
        var latest = _sessions.Values
            .Where(s => s.CrewId == crewId)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefault();

        return Task.FromResult(latest);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default)
    {
        var summaries = _sessions.Values
            .Select(s => new SessionSummary
            {
                SessionId = s.SessionId,
                CrewId = s.CrewId,
                Phase = s.Phase,
                CompletedTaskCount = s.TaskCheckpoints.Count(t => t.Value.Status == CheckpointStatus.Completed),
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<SessionSummary>>(summaries);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    // ── Time-travel methods ──

    /// <inheritdoc />
    public Task<VersionedState> SaveVersionedAsync(SessionState state, string? stepId = null, string? label = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_historyLock)
        {
            var versions = _history.GetOrAdd(state.SessionId, _ => []);
            var nextVersion = versions.Count + 1;
            var versionId = Guid.NewGuid().ToString();

            var versioned = new VersionedState
            {
                VersionId = versionId,
                SessionId = state.SessionId,
                Version = nextVersion,
                State = state,
                StepId = stepId,
                Label = label,
                CreatedAt = DateTime.UtcNow
            };

            versions.Add(versioned);

            // Also update the current session state
            _sessions.AddOrUpdate(state.SessionId, state, (_, _) => state);

            return Task.FromResult(versioned);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VersionSummary>> GetHistoryAsync(string sessionId, int? limit = null, CancellationToken ct = default)
    {
        lock (_historyLock)
        {
            if (!_history.TryGetValue(sessionId, out var versions))
                return Task.FromResult<IReadOnlyList<VersionSummary>>(Array.Empty<VersionSummary>());

            var query = versions
                .OrderByDescending(v => v.Version)
                .Select(v => new VersionSummary
                {
                    VersionId = v.VersionId,
                    SessionId = v.SessionId,
                    Version = v.Version,
                    Phase = v.State.Phase,
                    CompletedTaskCount = v.State.TaskCheckpoints.Count(t => t.Value.Status == CheckpointStatus.Completed),
                    StepId = v.StepId,
                    Label = v.Label,
                    CreatedAt = v.CreatedAt
                });

            if (limit.HasValue)
                query = query.Take(limit.Value);

            return Task.FromResult<IReadOnlyList<VersionSummary>>(query.ToList());
        }
    }

    /// <inheritdoc />
    public Task<VersionedState?> GetVersionAsync(string sessionId, string versionId, CancellationToken ct = default)
    {
        lock (_historyLock)
        {
            if (!_history.TryGetValue(sessionId, out var versions))
                return Task.FromResult<VersionedState?>(null);

            var found = versions.FirstOrDefault(v => v.VersionId == versionId);
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<VersionedState?> GetAtAsync(string sessionId, DateTime timestamp, CancellationToken ct = default)
    {
        lock (_historyLock)
        {
            if (!_history.TryGetValue(sessionId, out var versions))
                return Task.FromResult<VersionedState?>(null);

            var found = versions
                .Where(v => v.CreatedAt <= timestamp)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefault();

            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<VersionedState?> GetByStepAsync(string sessionId, string stepId, CancellationToken ct = default)
    {
        lock (_historyLock)
        {
            if (!_history.TryGetValue(sessionId, out var versions))
                return Task.FromResult<VersionedState?>(null);

            var found = versions.FirstOrDefault(v => v.StepId == stepId);
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<VersionedState> ForkAsync(string sourceSessionId, string sourceVersionId, string? label = null, CancellationToken ct = default)
    {
        lock (_historyLock)
        {
            if (!_history.TryGetValue(sourceSessionId, out var sourceVersions))
                throw new InvalidOperationException($"Session '{sourceSessionId}' not found");

            var sourceVersion = sourceVersions.FirstOrDefault(v => v.VersionId == sourceVersionId)
                ?? throw new InvalidOperationException($"Version '{sourceVersionId}' not found in session '{sourceSessionId}'");

            var newSessionId = Guid.NewGuid().ToString();
            var newVersionId = Guid.NewGuid().ToString();

            var forkedState = sourceVersion.State with
            {
                SessionId = newSessionId,
                UpdatedAt = DateTime.UtcNow
            };

            var forkedVersion = new VersionedState
            {
                VersionId = newVersionId,
                SessionId = newSessionId,
                Version = 1,
                State = forkedState,
                StepId = null,
                Label = label ?? $"Fork from {sourceSessionId}@v{sourceVersion.Version}",
                ForkedFromSession = sourceSessionId,
                ForkedFromVersion = sourceVersionId,
                CreatedAt = DateTime.UtcNow
            };

            var newVersions = new List<VersionedState> { forkedVersion };
            _history[newSessionId] = newVersions;
            _sessions[newSessionId] = forkedState;

            return Task.FromResult(forkedVersion);
        }
    }

    /// <inheritdoc />
    public Task<StateDiff> DiffAsync(string sessionId, string fromVersionId, string toVersionId, CancellationToken ct = default)
    {
        lock (_historyLock)
        {
            if (!_history.TryGetValue(sessionId, out var versions))
                throw new InvalidOperationException($"Session '{sessionId}' not found");

            var fromVersion = versions.FirstOrDefault(v => v.VersionId == fromVersionId)
                ?? throw new InvalidOperationException($"Version '{fromVersionId}' not found");
            var toVersion = versions.FirstOrDefault(v => v.VersionId == toVersionId)
                ?? throw new InvalidOperationException($"Version '{toVersionId}' not found");

            var diff = ComputeDiff(fromVersion, toVersion);
            return Task.FromResult(diff);
        }
    }

    private static StateDiff ComputeDiff(VersionedState from, VersionedState to)
    {
        var fromTasks = from.State.TaskCheckpoints;
        var toTasks = to.State.TaskCheckpoints;

        var allTaskIds = fromTasks.Keys.Union(toTasks.Keys).ToList();
        var addedIds = toTasks.Keys.Except(fromTasks.Keys).ToList();
        var removedIds = fromTasks.Keys.Except(toTasks.Keys).ToList();

        var taskDiffs = new List<TaskDiff>();
        foreach (var taskId in allTaskIds)
        {
            var hasFrom = fromTasks.TryGetValue(taskId, out var fromTask);
            var hasTo = toTasks.TryGetValue(taskId, out var toTask);

            if (hasFrom && hasTo
                && (fromTask!.Status != toTask!.Status || fromTask.Output != toTask.Output))
            {
                taskDiffs.Add(new TaskDiff
                {
                    TaskId = taskId,
                    FromStatus = fromTask.Status,
                    ToStatus = toTask.Status,
                    OutputChanged = fromTask.Output != toTask.Output
                });
            }
        }

        return new StateDiff
        {
            FromVersionId = from.VersionId,
            ToVersionId = to.VersionId,
            PhaseChanged = from.State.Phase != to.State.Phase,
            FromPhase = from.State.Phase,
            ToPhase = to.State.Phase,
            TaskDiffs = taskDiffs,
            AddedTaskIds = addedIds,
            RemovedTaskIds = removedIds
        };
    }
}
