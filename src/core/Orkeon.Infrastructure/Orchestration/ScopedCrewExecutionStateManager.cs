using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Constants.Orchestration;
using Orkeon.Domain.Constants.Crew;

namespace Orkeon.Infrastructure.Orchestration;

/// <summary>
/// Scoped implementation of crew execution state management.
/// Provides proper isolation and cleanup of execution states.
/// </summary>
/// <remarks>
/// Durable persistence (crash recovery) is opt-in: when
/// <see cref="CrewExecutionStatePersistenceOptions.Enabled"/> is set and an
/// <see cref="IStateStore"/> is registered (checkpointing stores: in-memory, JSON file,
/// SQLite, PostgreSQL), every state transition is persisted and states are reloaded
/// transparently after a restart. Without configuration the manager behaves exactly as
/// before: in-memory only.
/// </remarks>
public partial class ScopedCrewExecutionStateManager : ICrewExecutionStateManager, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScopedCrewExecutionStateManager> _logger;
    private readonly ConcurrentDictionary<ExecutionId, CrewExecutionState> _states;
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _cleanupInterval = OrchestrationDefaults.CleanupInterval;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly CrewExecutionStatePersistenceOptions _persistence;
    private int _missingStoreWarned;

    /// <summary>
    /// Initializes a new instance of <see cref="ScopedCrewExecutionStateManager"/>.
    /// </summary>
    /// <param name="scopeFactory">Scope factory used to resolve scoped persistence services.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="persistenceOptions">
    /// Optional durable-persistence options. When omitted (or <c>Enabled = false</c>),
    /// states are kept in memory only — the historical, backward-compatible behavior.
    /// </param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fire-and-forget timer callback: a failure during periodic cleanup is logged and swallowed so it cannot fault the timer thread or crash the host.")]
    public ScopedCrewExecutionStateManager(
        IServiceScopeFactory scopeFactory,
        ILogger<ScopedCrewExecutionStateManager> logger,
        IOptions<CrewExecutionStatePersistenceOptions>? persistenceOptions = null)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _persistence = persistenceOptions?.Value ?? new CrewExecutionStatePersistenceOptions();
        _states = new ConcurrentDictionary<ExecutionId, CrewExecutionState>();

        // Periodic cleanup — fire-and-forget with exception logging
        _cleanupTimer = new Timer(
            async _ =>
            {
                try
                {
                    await CleanupExpiredExecutionsAsync(OrchestrationDefaults.ExecutionExpiryHours).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogPeriodicCleanupFailed(ex);
                }
            },
            null,
            _cleanupInterval,
            _cleanupInterval);
    }

    /// <summary>
    /// Create State Async.
    /// </summary>
    public async Task<CrewExecutionState> CreateStateAsync(
        CrewId crewId,
        CancellationToken cancellationToken = default)
    {
        var executionId = ExecutionId.New();
        var input = CrewInput.Empty();

        return await CreateStateAsync(crewId, executionId, input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an execution state, or resumes the persisted one when durable persistence
    /// is enabled and a state already exists in the store for <paramref name="executionId"/>
    /// (crash recovery).
    /// </summary>
    public Task<CrewExecutionState> CreateStateAsync(
        CrewId crewId,
        ExecutionId executionId,
        CrewInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        return CreateStateCoreAsync();

        async Task<CrewExecutionState> CreateStateCoreAsync()
        {
            await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var scope = _scopeFactory.CreateScope();

                // Resume path: a state persisted by a previous process (e.g., before a crash)
                // is reloaded instead of being recreated, so the execution continues from its
                // last persisted status/progress. Live in-memory duplicates still throw below.
                if (!_states.ContainsKey(executionId))
                {
                    var resumed = await LoadStateAsync(executionId, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
                    if (resumed != null)
                    {
                        _states.TryAdd(executionId, resumed);
                        LogExecutionStateResumed(executionId, resumed.Status);
                        return resumed;
                    }
                }

                var state = new CrewExecutionState(crewId, executionId, input);

                // Store in concurrent dictionary
                if (!_states.TryAdd(executionId, state))
                {
                    throw new InvalidOperationException(
                        $"Execution state with ID {executionId} already exists");
                }

                if (_logger.IsEnabled(LogLevel.Information))
                    LogExecutionStateCreated(crewId, executionId);

                // Optionally persist to external storage
                await PersistStateAsync(state, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);

                return state;
            }
            finally
            {
                _stateLock.Release();
            }
        }
    }

    /// <summary>
    /// Get State Async.
    /// </summary>
    public Task<CrewExecutionState?> GetStateAsync(
        ExecutionId executionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        return GetStateCoreAsync();

        async Task<CrewExecutionState?> GetStateCoreAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Try memory first
            if (_states.TryGetValue(executionId, out var state))
            {
                return state;
            }

            // Try loading from persistence
            using var scope = _scopeFactory.CreateScope();
            state = await LoadStateAsync(executionId, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);

            if (state != null)
            {
                // Cache in memory
                _states.TryAdd(executionId, state);
            }

            return state;
        }
    }

    /// <summary>
    /// Update State Async.
    /// </summary>
    public Task UpdateStateAsync(
        ExecutionId executionId,
        Action<CrewExecutionState> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ArgumentNullException.ThrowIfNull(update);
        return UpdateStateCoreAsync();

        async Task UpdateStateCoreAsync()
        {
            var state = await GetStateAsync(executionId, cancellationToken).ConfigureAwait(false);
            if (state == null)
            {
                throw new InvalidOperationException(
                    $"Execution state with ID {executionId} not found");
            }

            // Apply update
            update(state);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                LogExecutionStateUpdated(executionId, state.Status, state.Progress);
            }

            // Persist changes
            using var scope = _scopeFactory.CreateScope();
            await PersistStateAsync(state, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Complete Execution Async.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fire-and-forget archival continuation: a failure archiving the completed state is logged and swallowed because archival is best-effort cleanup and must not fail the completion flow.")]
    public Task CompleteExecutionAsync(
        ExecutionId executionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        return CompleteExecutionCoreAsync();

        async Task CompleteExecutionCoreAsync()
        {
            await UpdateStateAsync(executionId, state =>
            {
                state.Status = ExecutionState.Completed;
                state.Progress = 1.0;
            }, cancellationToken).ConfigureAwait(false);

            if (_logger.IsEnabled(LogLevel.Information))
                LogExecutionCompleted(executionId);

            // Fire-and-forget: archive after a delay. Exceptions are intentionally swallowed
            // because archival is best-effort cleanup and should not fail the completion flow.
            _ = System.Threading.Tasks.Task.Delay(CrewDefaults.DefaultExecutionTimeout, cancellationToken)
                .ContinueWith(async _ =>
                {
                    try
                    {
                        await ArchiveStateAsync(executionId, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogArchiveFailed(ex, executionId.AsString());
                    }
                }, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Get Active Executions Async.
    /// </summary>
    public async Task<IReadOnlyList<CrewExecutionState>> GetActiveExecutionsAsync(
        CrewId crewId,
        CancellationToken cancellationToken = default)
    {
        var activeStates = _states.Values
            .Where(s => s.CrewId == crewId &&
                       (s.Status == ExecutionState.Running ||
                        s.Status == ExecutionState.Pending))
            .ToList();

        // Also check persistence for any we might have missed
        using var scope = _scopeFactory.CreateScope();
        var persistedStates = await LoadActiveStatesAsync(crewId, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);

        // Merge and deduplicate (in-memory entries win over persisted copies)
        var allStates = activeStates
            .Concat(persistedStates)
            .DistinctBy(s => s.Id)
            .ToList();

        // Cache rehydrated states so subsequent updates target the same instance.
        foreach (var state in allStates)
        {
            _states.TryAdd(state.Id, state);
        }

        return allStates;
    }

    /// <summary>
    /// Cleanup Expired Executions Async.
    /// </summary>
    public async Task CleanupExpiredExecutionsAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTime.UtcNow - maxAge;
        var expiredIds = new List<ExecutionId>();

        foreach (var kvp in _states)
        {
            var state = kvp.Value;
            var isExpired = state.EndTime.HasValue && state.EndTime.Value < cutoffTime;
            var isAbandoned = !state.EndTime.HasValue &&
                            state.StartTime < cutoffTime &&
                            state.Status != ExecutionState.Running;

            if (isExpired || isAbandoned)
            {
                expiredIds.Add(kvp.Key);
            }
        }

        LogCleaningUpExpired(expiredIds.Count);

        foreach (var id in expiredIds)
        {
            await ArchiveStateAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    // Private helper methods — durable persistence (R3.8).
    // The IStateStore is resolved per call from the scoped provider so that the
    // singleton manager can use scoped/disposable stores safely. When persistence is
    // disabled or no store is registered, these helpers degrade to the historical
    // in-memory-only behavior.

    /// <summary>
    /// Resolves the state store from the scope when persistence is enabled.
    /// Logs a single warning when persistence is enabled but no store is registered.
    /// </summary>
    private IStateStore? ResolveStore(IServiceProvider serviceProvider)
    {
        if (!_persistence.Enabled)
            return null;

        var store = serviceProvider.GetService<IStateStore>();
        if (store == null && Interlocked.Exchange(ref _missingStoreWarned, 1) == 0)
        {
            LogPersistenceEnabledButNoStore();
        }

        return store;
    }

    /// <summary>Persists the current snapshot of <paramref name="state"/> to the configured store.</summary>
    private async Task PersistStateAsync(
        CrewExecutionState state,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var store = ResolveStore(serviceProvider);
        if (store == null)
            return;

        var session = CrewExecutionStateMapper.ToSessionState(state);
        await store.SaveAsync(session, cancellationToken).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Debug))
            LogExecutionStatePersisted(state.Id);
    }

    /// <summary>Loads and rehydrates a persisted state. Returns null on cache miss or when persistence is off.</summary>
    private async Task<CrewExecutionState?> LoadStateAsync(
        ExecutionId executionId,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var store = ResolveStore(serviceProvider);
        if (store == null)
            return null;

        var session = await store.GetAsync(
            CrewExecutionStateMapper.SessionIdFor(executionId), cancellationToken).ConfigureAwait(false);
        if (session == null)
            return null;

        var state = CrewExecutionStateMapper.FromSessionState(session);
        if (state != null && _logger.IsEnabled(LogLevel.Debug))
            LogExecutionStateLoaded(executionId);

        return state;
    }

    /// <summary>Loads all persisted, non-terminal states of a crew. Empty when persistence is off.</summary>
    private async Task<IEnumerable<CrewExecutionState>> LoadActiveStatesAsync(
        CrewId crewId,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var store = ResolveStore(serviceProvider);
        if (store == null)
            return [];

        var crewScope = CrewExecutionStateMapper.CrewScopeFor(crewId);
        var summaries = await store.ListAsync(cancellationToken).ConfigureAwait(false);

        var states = new List<CrewExecutionState>();
        foreach (var summary in summaries)
        {
            if (!string.Equals(summary.CrewId, crewScope, StringComparison.Ordinal))
                continue;
            if (summary.Phase != SessionPhase.Pending && summary.Phase != SessionPhase.Running)
                continue;

            var session = await store.GetAsync(summary.SessionId, cancellationToken).ConfigureAwait(false);
            if (session == null)
                continue;

            var state = CrewExecutionStateMapper.FromSessionState(session);
            if (state != null)
                states.Add(state);
        }

        return states;
    }

    /// <summary>
    /// Archives an execution: persists its final snapshot (unless configured to delete),
    /// then evicts it from the in-memory dictionary.
    /// </summary>
    private async Task ArchiveStateAsync(
        ExecutionId executionId,
        CancellationToken cancellationToken)
    {
        if (_persistence.Enabled)
        {
            using var scope = _scopeFactory.CreateScope();
            var store = ResolveStore(scope.ServiceProvider);

            if (store != null)
            {
                if (_persistence.DeleteFromStoreOnArchive)
                {
                    await store.DeleteAsync(
                        CrewExecutionStateMapper.SessionIdFor(executionId), cancellationToken).ConfigureAwait(false);
                }
                else if (_states.TryGetValue(executionId, out var state))
                {
                    // Keep the final snapshot so status queries survive memory eviction.
                    await store.SaveAsync(
                        CrewExecutionStateMapper.ToSessionState(state), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        _states.TryRemove(executionId, out _);
    }

    /// <summary>
    /// Dispose.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Dispose(bool).</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cleanupTimer?.Dispose();
            _stateLock?.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Periodic cleanup of expired executions failed")]
    private partial void LogPeriodicCleanupFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created execution state for crew {CrewId} with execution ID {ExecutionId}")]
    private partial void LogExecutionStateCreated(CrewId crewId, ExecutionId executionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updated execution state {ExecutionId}: Status={Status}, Progress={Progress}")]
    private partial void LogExecutionStateUpdated(ExecutionId executionId, ExecutionState status, double progress);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed execution {ExecutionId}")]
    private partial void LogExecutionCompleted(ExecutionId executionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to archive execution state {ExecutionId}")]
    private partial void LogArchiveFailed(Exception ex, string executionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaning up {Count} expired executions")]
    private partial void LogCleaningUpExpired(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resumed persisted execution state {ExecutionId} (status {Status})")]
    private partial void LogExecutionStateResumed(ExecutionId executionId, ExecutionState status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Execution state persistence is enabled but no IStateStore is registered — states remain in-memory only. Register a checkpointing store (e.g., AddOrkeonCheckpointing / AddOrkeonSqliteCheckpointing).")]
    private partial void LogPersistenceEnabledButNoStore();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Persisted execution state {ExecutionId}")]
    private partial void LogExecutionStatePersisted(ExecutionId executionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded persisted execution state {ExecutionId}")]
    private partial void LogExecutionStateLoaded(ExecutionId executionId);
}

/// <summary>
/// Extension methods for registering state management.
/// </summary>
public static class StateManagementExtensions
{
    /// <summary>
    /// Add Crew Execution State Management.
    /// </summary>
    public static IServiceCollection AddCrewExecutionStateManagement(
        this IServiceCollection services)
    {
        services.AddSingleton<ICrewExecutionStateManager, ScopedCrewExecutionStateManager>();

        return services;
    }

    /// <summary>
    /// Enables durable persistence of crew execution states (crash recovery — R3.8).
    /// Requires an <see cref="Orkeon.Application.Interfaces.Checkpointing.IStateStore"/>
    /// registration (e.g., <c>AddOrkeonCheckpointing()</c>,
    /// <c>AddOrkeonSqliteCheckpointing(...)</c> or <c>AddOrkeonPostgresCheckpointing(...)</c>);
    /// without a store the manager logs a warning and stays in-memory only.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional extra configuration applied after enabling.</param>
    public static IServiceCollection AddCrewExecutionStatePersistence(
        this IServiceCollection services,
        Action<CrewExecutionStatePersistenceOptions>? configure = null)
    {
        services.AddOptions<CrewExecutionStatePersistenceOptions>()
            .Configure(options =>
            {
                options.Enabled = true;
                configure?.Invoke(options);
            });

        return services;
    }

    /// <summary>
    /// Enables durable persistence of crew execution states from configuration.
    /// Binds the <c>Orkeon:ExecutionState:Persistence</c> section
    /// (<c>Enabled</c>, <c>DeleteFromStoreOnArchive</c>).
    /// </summary>
    public static IServiceCollection AddCrewExecutionStatePersistence(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CrewExecutionStatePersistenceOptions>()
            .Bind(configuration.GetSection("Orkeon:ExecutionState:Persistence"));

        return services;
    }
}
