namespace Orkeon.Infrastructure.Orchestration;

/// <summary>
/// Options controlling durable persistence of crew execution states
/// (<c>ScopedCrewExecutionStateManager</c> → <c>IStateStore</c>).
/// </summary>
/// <remarks>
/// Disabled by default (backward compatible: states live in memory only, no crash
/// recovery). When <see cref="Enabled"/> is <see langword="true"/> and an
/// <c>IStateStore</c> is registered (see <c>CheckpointingExtensions</c> —
/// <c>AddOrkeonCheckpointing</c> / <c>AddOrkeonSqliteCheckpointing</c> /
/// <c>AddOrkeonPostgresCheckpointing</c>), every state transition is persisted and
/// states are reloaded after a process restart.
/// Bindable from the <c>Orkeon:ExecutionState:Persistence</c> configuration section.
/// </remarks>
public sealed class CrewExecutionStatePersistenceOptions
{
    /// <summary>
    /// Enables durable persistence of execution states. Default: <see langword="false"/>
    /// (in-memory only, identical to the historical behavior).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// When <see langword="true"/>, archiving an execution (completion or expiry cleanup)
    /// also deletes its persisted entry from the state store. Default:
    /// <see langword="false"/> — the final snapshot is kept in the store so that status
    /// queries keep working after the in-memory entry is evicted.
    /// </summary>
    public bool DeleteFromStoreOnArchive { get; set; }
}
