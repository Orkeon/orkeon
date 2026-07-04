namespace Orkeon.Tools.Data.Relational.Models;

/// <summary>
/// Represents metadata about a database index.
/// </summary>
public sealed record IndexInfo
{
    /// <summary>Gets the index name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the table this index belongs to.</summary>
    public string TableName { get; init; } = "";

    /// <summary>Gets the columns included in this index.</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Gets whether this is a unique index.</summary>
    public bool IsUnique { get; init; }

    /// <summary>Gets the index type (e.g. "BTREE", "HASH"), if available.</summary>
    public string? Type { get; init; }
}
