using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.MongoDB;

/// <summary>
/// Response from executing a MongoDB operation.
/// </summary>
public record MongoDbResponse
{
    /// <summary>Gets the result documents as JSON strings.</summary>
    [ReturnSchema(Description = "Result documents as JSON strings")]
    public IReadOnlyList<string>? Documents { get; init; }

    /// <summary>Gets the number of documents matching the query or in the collection.</summary>
    [ReturnSchema(Description = "Number of documents matching the query or in the collection")]
    public long DocumentCount { get; init; }

    /// <summary>Gets the number of documents matched by an update filter.</summary>
    [ReturnSchema(Description = "Number of documents matched by an update filter")]
    public long MatchedCount { get; init; }

    /// <summary>Gets the number of documents modified by an update operation.</summary>
    [ReturnSchema(Description = "Number of documents modified by an update operation")]
    public long ModifiedCount { get; init; }

    /// <summary>Gets the ID of the inserted document.</summary>
    [ReturnSchema(Description = "ID of the inserted document")]
    public string? InsertedId { get; init; }

    /// <summary>Gets the distinct values returned by a Distinct operation.</summary>
    [ReturnSchema(Description = "Distinct values returned by a Distinct operation")]
    public IReadOnlyList<string>? DistinctValues { get; init; }

    /// <summary>Gets the operation that was executed.</summary>
    [ReturnSchema(Description = "The operation that was executed")]
    public string Operation { get; init; } = "";

    /// <summary>Gets the execution time in milliseconds.</summary>
    [ReturnSchema(Description = "Execution time in milliseconds")]
    public long ExecutionTimeMs { get; init; }
}
