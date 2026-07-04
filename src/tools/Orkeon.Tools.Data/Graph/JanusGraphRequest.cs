using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.Graph;

/// <summary>
/// Request parameters for executing a Gremlin traversal on JanusGraph.
/// </summary>
public record JanusGraphRequest
{
    /// <summary>Gets the Gremlin WebSocket endpoint URI.</summary>
    [FieldSchema(Description = "Gremlin WebSocket endpoint URI", Example = "ws://localhost:8182/gremlin", IsRequired = true)]
    public string GremlinEndpoint { get; init; } = "";

    /// <summary>Gets the Gremlin traversal to execute.</summary>
    [FieldSchema(Description = "Gremlin traversal string to execute", Example = "g.V().hasLabel('person').limit(10)", IsRequired = true)]
    public string Traversal { get; init; } = "";

    /// <summary>Gets optional parameter bindings for the traversal.</summary>
    [FieldSchema(Description = "Parameter bindings for the traversal as key-value pairs", IsRequired = false)]
    public Dictionary<string, object>? Bindings { get; init; }

    /// <summary>Gets the maximum number of results to return.</summary>
    [FieldSchema(Description = "Maximum number of results to return", IsRequired = false, Example = "1000")]
    public int MaxResults { get; init; } = 1000;

    /// <summary>Gets the timeout in milliseconds for the traversal execution.</summary>
    [FieldSchema(Description = "Timeout in milliseconds for traversal execution", IsRequired = false, Example = "30000")]
    public long TimeoutMs { get; init; } = 30000;
}
