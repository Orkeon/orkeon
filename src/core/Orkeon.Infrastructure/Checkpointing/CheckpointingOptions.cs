namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>The durable store a host keeps its checkpoint sessions in.</summary>
public enum CheckpointStore
{
    /// <summary>Process memory: nothing survives the process.</summary>
    Memory,

    /// <summary>One JSON file per session, under <see cref="CheckpointingOptions.Path"/>, through the VFS.</summary>
    Json,

    /// <summary>A SQLite database: <see cref="CheckpointingOptions.ConnectionString"/>, or the file at <see cref="CheckpointingOptions.Path"/>.</summary>
    Sqlite,

    /// <summary>A PostgreSQL database: <see cref="CheckpointingOptions.ConnectionString"/>.</summary>
    Postgres,
}

/// <summary>
/// The <c>Orkeon:Checkpointing</c> section: whether a host checkpoints its runs and in which store.
/// </summary>
public sealed class CheckpointingOptions
{
    /// <summary>The section path.</summary>
    public const string SectionName = "Orkeon:Checkpointing";

    /// <summary>The virtual path a host mounts for its state, the base of the default <see cref="Path"/>.</summary>
    public const string StateRoot = "/state";

    /// <summary>Gets or sets whether the store named by <see cref="Store"/> is used (default: false, the in-memory store).</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the store (default: <see cref="CheckpointStore.Memory"/>).</summary>
    public CheckpointStore Store { get; set; } = CheckpointStore.Memory;

    /// <summary>
    /// Gets or sets the virtual path of the folder (Json) or the file (Sqlite) the store writes, under
    /// the <see cref="StateRoot"/> mount. Null: <c>/state/checkpoints</c> or <c>/state/checkpoints.db</c>.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets the connection string of the Sqlite (instead of <see cref="Path"/>) or Postgres store.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Gets or sets the PostgreSQL schema name (default: "orkeon").</summary>
    public string SchemaName { get; set; } = "orkeon";

    /// <summary>Gets or sets whether the PostgreSQL schema and tables are created on first use.</summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>Gets or sets the maximum number of version entries per session in the PostgreSQL store.</summary>
    public int MaxHistoryPerSession { get; set; } = 1000;
}
