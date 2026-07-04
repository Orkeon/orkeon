using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.Graph;

/// <summary>
/// Response from executing a query on ArcadeDB via the Bolt protocol.
/// </summary>
public record ArcadeDbResponse
{
    /// <summary>Gets the result records as key-value pairs.</summary>
    [ReturnSchema(Description = "Result records as key-value pairs (column name to value)")]
    public IReadOnlyList<Dictionary<string, object>>? Records { get; init; }

    /// <summary>Gets the number of records returned.</summary>
    [ReturnSchema(Description = "Number of records returned", Example = 42)]
    public int RecordCount { get; init; }

    /// <summary>Gets the number of nodes created by the query.</summary>
    [ReturnSchema(Description = "Number of nodes created by the query", Example = 0)]
    public int NodesCreated { get; init; }

    /// <summary>Gets the number of relationships created by the query.</summary>
    [ReturnSchema(Description = "Number of relationships created by the query", Example = 0)]
    public int RelationshipsCreated { get; init; }

    /// <summary>Gets the number of properties set by the query.</summary>
    [ReturnSchema(Description = "Number of properties set by the query", Example = 0)]
    public int PropertiesSet { get; init; }

    /// <summary>Gets the query language that was used.</summary>
    [ReturnSchema(Description = "The query language that was used", Example = "Cypher")]
    public ArcadeDbQueryLanguage QueryLanguage { get; init; } = ArcadeDbQueryLanguage.Cypher;

    /// <summary>Gets the query execution time in milliseconds.</summary>
    [ReturnSchema(Description = "Query execution time in milliseconds", Example = 15)]
    public long ExecutionTimeMs { get; init; }
}
