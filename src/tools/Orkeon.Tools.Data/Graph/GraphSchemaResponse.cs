using Orkeon.Domain.Attributes;
using Orkeon.Tools.Data.Graph.Models;

namespace Orkeon.Tools.Data.Graph;

/// <summary>
/// Response containing graph database schema information.
/// </summary>
public record GraphSchemaResponse
{
    /// <summary>Gets the vertex labels found in the graph.</summary>
    [ReturnSchema(Description = "Vertex labels with their properties and optional count estimates")]
    public IReadOnlyList<VertexLabelInfo> VertexLabels { get; init; } = [];

    /// <summary>Gets the edge labels found in the graph.</summary>
    [ReturnSchema(Description = "Edge labels with source/target vertex labels and properties")]
    public IReadOnlyList<EdgeLabelInfo> EdgeLabels { get; init; } = [];

    /// <summary>Gets the property keys defined in the graph.</summary>
    [ReturnSchema(Description = "Property keys with data types and cardinality")]
    public IReadOnlyList<PropertyKeyInfo> PropertyKeys { get; init; } = [];

    /// <summary>Gets the indexes defined in the graph.</summary>
    [ReturnSchema(Description = "Graph indexes with property keys, type, and uniqueness")]
    public IReadOnlyList<GraphIndexInfo> Indexes { get; init; } = [];

    /// <summary>Gets the graph database type that was inspected.</summary>
    [ReturnSchema(Description = "The graph database type that was inspected", Example = "JanusGraph")]
    public GraphType GraphType { get; init; }
}
