using Orkeon.Domain.Attributes;
using Orkeon.Tools.Data.MongoDB.Models;

namespace Orkeon.Tools.Data.MongoDB;

/// <summary>
/// Response from inspecting a MongoDB database schema.
/// </summary>
public record MongoDbSchemaResponse
{
    /// <summary>Gets the collection schemas inferred from sampled documents.</summary>
    [ReturnSchema(Description = "Collection schemas inferred from sampled documents")]
    public IReadOnlyList<CollectionInfo> Collections { get; init; } = [];

    /// <summary>Gets the database name that was inspected.</summary>
    [ReturnSchema(Description = "Database name that was inspected")]
    public string DatabaseName { get; init; } = "";
}
