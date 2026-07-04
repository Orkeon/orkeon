using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.Graph;

/// <summary>
/// Response from executing a Gremlin traversal on JanusGraph.
/// </summary>
public record JanusGraphResponse
{
    /// <summary>Gets the traversal results as key-value pairs.</summary>
    [ReturnSchema(Description = "Traversal results as key-value pairs")]
    public IReadOnlyList<Dictionary<string, object?>>? Results { get; init; }

    /// <summary>Gets the number of results returned.</summary>
    [ReturnSchema(Description = "Number of results returned", Example = 42)]
    public int ResultCount { get; init; }

    /// <summary>Gets the detected result type (vertex, edge, path, value).</summary>
    [ReturnSchema(Description = "Detected result type: vertex, edge, path, or value", Example = "vertex")]
    public string ResultType { get; init; } = "value";

    /// <summary>Gets the traversal execution time in milliseconds.</summary>
    [ReturnSchema(Description = "Traversal execution time in milliseconds", Example = 15)]
    public long ExecutionTimeMs { get; init; }
}
