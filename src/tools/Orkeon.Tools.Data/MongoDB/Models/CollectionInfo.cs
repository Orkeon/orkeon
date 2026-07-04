namespace Orkeon.Tools.Data.MongoDB.Models;

/// <summary>
/// Schema information for a MongoDB collection including inferred fields and indexes.
/// </summary>
public record CollectionInfo
{
    /// <summary>Gets the collection name.</summary>
    public string Name { get; init; } = "";
    /// <summary>Gets the total number of documents in the collection.</summary>
    public long DocumentCount { get; init; }
    /// <summary>Gets the indexes defined on this collection.</summary>
    public IReadOnlyList<MongoIndexInfo> Indexes { get; init; } = [];
    /// <summary>Gets the fields inferred from document sampling.</summary>
    public IReadOnlyList<InferredField> InferredFields { get; init; } = [];
}
