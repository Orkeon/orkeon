namespace Orkeon.Tools.Data.MongoDB.Models;

/// <summary>
/// Describes a MongoDB index on a collection.
/// </summary>
public record MongoIndexInfo
{
    /// <summary>Gets the index name.</summary>
    public string Name { get; init; } = "";
    /// <summary>Gets the index key fields and their sort direction (1 = ascending, -1 = descending).</summary>
    public Dictionary<string, int> Keys { get; init; } = [];
    /// <summary>Gets whether this is a unique index.</summary>
    public bool IsUnique { get; init; }
    /// <summary>Gets whether this is a sparse index.</summary>
    public bool IsSparse { get; init; }
}
