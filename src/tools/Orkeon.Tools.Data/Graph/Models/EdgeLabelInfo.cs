namespace Orkeon.Tools.Data.Graph.Models;

/// <summary>
/// Information about an edge label in a graph database.
/// </summary>
public sealed record EdgeLabelInfo
{
    /// <summary>Gets the edge label name.</summary>
    public string Label { get; init; } = "";

    /// <summary>Gets the source vertex label, if known.</summary>
    public string? SourceLabel { get; init; }

    /// <summary>Gets the target vertex label, if known.</summary>
    public string? TargetLabel { get; init; }

    /// <summary>Gets the property names associated with this edge label.</summary>
    public IReadOnlyList<string> Properties { get; init; } = [];
}
