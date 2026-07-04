namespace Orkeon.Tools.Data.Graph.Models;

/// <summary>
/// Information about an index in a graph database.
/// </summary>
public sealed record GraphIndexInfo
{
    /// <summary>Gets the index name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the property keys covered by this index.</summary>
    public IReadOnlyList<string> PropertyKeys { get; init; } = [];

    /// <summary>Gets the index type (e.g., Composite, MixedIndex).</summary>
    public string? IndexType { get; init; }

    /// <summary>Gets whether this index enforces uniqueness.</summary>
    public bool IsUnique { get; init; }
}
