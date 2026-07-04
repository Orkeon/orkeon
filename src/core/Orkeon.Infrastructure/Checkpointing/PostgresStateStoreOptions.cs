namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>
/// Configuration options for <see cref="PostgresStateStore"/>.
/// </summary>
public sealed class PostgresStateStoreOptions
{
    /// <summary>Gets or sets the PostgreSQL connection string.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Gets or sets the schema name (default: "orkeon").</summary>
    public string SchemaName { get; set; } = "orkeon";

    /// <summary>Gets or sets whether to auto-create the schema and tables on first use.</summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>Gets or sets the maximum number of version entries per session (default: 1000).</summary>
    public int MaxHistoryPerSession { get; set; } = 1000;
}
