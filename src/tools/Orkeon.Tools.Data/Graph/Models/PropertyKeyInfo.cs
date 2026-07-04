namespace Orkeon.Tools.Data.Graph.Models;

/// <summary>
/// Information about a property key in a graph database.
/// </summary>
public sealed record PropertyKeyInfo
{
    /// <summary>Gets the property key name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the data type of the property.</summary>
    public string DataType { get; init; } = "";

    /// <summary>Gets the cardinality of the property (e.g., SINGLE, LIST, SET).</summary>
    public string? Cardinality { get; init; }
}
