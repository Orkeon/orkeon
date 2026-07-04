namespace Orkeon.Tools.Data.Relational.Models;

/// <summary>
/// Represents metadata about a database column.
/// </summary>
public sealed record ColumnInfo
{
    /// <summary>Gets the column name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the data type (provider-specific, e.g. "varchar", "INTEGER").</summary>
    public string DataType { get; init; } = "";

    /// <summary>Gets whether the column allows NULL values.</summary>
    public bool IsNullable { get; init; }

    /// <summary>Gets whether this column is part of the primary key.</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>Gets the default value expression, if any.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Gets the maximum character/byte length, if applicable.</summary>
    public int? MaxLength { get; init; }
}
