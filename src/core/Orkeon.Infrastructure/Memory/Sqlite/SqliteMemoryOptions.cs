using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory.Sqlite;

/// <summary>
/// Configuration options for the SQLite memory provider.
/// </summary>
public class SqliteMemoryOptions
{
    /// <summary>
    /// Gets or sets the SQLite connection string
    /// (e.g. <c>"Data Source=/data/orkeon-memory.db"</c> or <c>"Data Source=:memory:"</c>).
    /// Defaults to an in-memory database so the framework default never writes an
    /// ungoverned file to disk. When a file <c>Data Source</c> is supplied, its path is
    /// resolved and validated through the virtual file system (VFS-70 / 2F-A) before
    /// being handed to Microsoft.Data.Sqlite, so it must point at a writable VFS mount.
    /// </summary>
    public string ConnectionString { get; set; } = "Data Source=:memory:";

    /// <summary>
    /// Gets or sets the table name used for storing memory items.
    /// Must match <c>^[A-Za-z_][A-Za-z0-9_]*$</c> (validated at construction
    /// because the identifier is interpolated into SQL statements).
    /// </summary>
    public string TableName { get; set; } = "memory_items";

    /// <summary>Gets or sets the default number of results returned by vector queries.</summary>
    public int DefaultTopK { get; set; } = MemoryDefaults.DefaultSearchLimit;

    /// <summary>Gets or sets the minimum similarity score threshold for vector search results.</summary>
    public float MinSimilarityScore { get; set; }
}
