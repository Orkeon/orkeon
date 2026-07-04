namespace Orkeon.Tools.Data.Graph.Models;

/// <summary>
/// Information about a vertex label in a graph database.
/// </summary>
public sealed record VertexLabelInfo
{
    /// <summary>Gets the vertex label name.</summary>
    public string Label { get; init; } = "";

    /// <summary>Gets the property names associated with this vertex label.</summary>
    public IReadOnlyList<string> Properties { get; init; } = [];

    /// <summary>Gets the estimated vertex count for this label, if available.</summary>
    public long? CountEstimate { get; init; }
}
