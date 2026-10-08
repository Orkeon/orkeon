using System.Text.RegularExpressions;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory.Sqlite;

/// <summary>
/// Configuration options for the SQLite memory provider.
/// </summary>
public partial class SqliteMemoryOptions
{
    /// <summary>The configuration section bound to these options.</summary>
    public const string SectionName = "Orkeon:Sqlite";

    /// <summary>
    /// Gets or sets the SQLite connection string
    /// (e.g. <c>"Data Source=/data/orkeon-memory.db"</c> or <c>"Data Source=:memory:"</c>).
    /// Defaults to an in-memory database (<c>Data Source=:memory:</c>); a file
    /// <c>Data Source</c> is a virtual path, which must point at a writable mount.
    /// </summary>
    /// <remarks>
    /// The default is in memory so the framework default never writes an ungoverned file to
    /// disk. When a file <c>Data Source</c> is supplied, its path is resolved and validated
    /// through the virtual file system (VFS-70 / 2F-A) before being handed to
    /// Microsoft.Data.Sqlite.
    /// </remarks>
    public string ConnectionString { get; set; } = "Data Source=:memory:";

    /// <summary>
    /// Gets or sets the table name used for storing memory items.
    /// Must match <c>^[A-Za-z_][A-Za-z0-9_]*$</c>: the identifier is interpolated into SQL
    /// statements. Checked when a host starts (<see cref="TableNameProblem"/>, GAP-40) and again at
    /// construction.
    /// </summary>
    public string TableName { get; set; } = "memory_items";

    /// <summary>Gets or sets the default number of results returned by vector queries.</summary>
    public int DefaultTopK { get; set; } = MemoryDefaults.DefaultSearchLimit;

    /// <summary>Gets or sets the minimum similarity score threshold for vector search results.</summary>
    public float MinSimilarityScore { get; set; }

    /// <summary>
    /// What a host says of a table name SQLite cannot be given (GAP-40), or null when it is one: the
    /// key, the value and the rule — at the start, rather than at the first memory stored.
    /// </summary>
    /// <param name="tableName">The configured table name.</param>
    public static string? TableNameProblem(string? tableName) =>
        tableName is not null && TableNamePattern().IsMatch(tableName)
            ? null
            : $"{SectionName}:TableName is '{tableName}', which is not a SQLite table name: write letters, digits " +
              "and underscores only, not starting with a digit (such as memory_items).";

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TableNamePattern();
}
