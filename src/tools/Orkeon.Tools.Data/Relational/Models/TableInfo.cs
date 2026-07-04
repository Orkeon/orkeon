namespace Orkeon.Tools.Data.Relational.Models;

/// <summary>
/// Represents metadata about a database table.
/// </summary>
public sealed record TableInfo
{
    /// <summary>Gets the table name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the schema name (e.g. "dbo", "public").</summary>
    public string? Schema { get; init; }

    /// <summary>Gets the columns in this table.</summary>
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = [];

    /// <summary>Gets the estimated row count, if available.</summary>
    public long? RowCountEstimate { get; init; }
}
