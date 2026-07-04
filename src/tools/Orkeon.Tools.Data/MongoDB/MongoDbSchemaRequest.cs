using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.MongoDB;

/// <summary>
/// Request parameters for inspecting a MongoDB database schema.
/// </summary>
public record MongoDbSchemaRequest
{
    /// <summary>Gets the MongoDB connection string.</summary>
    [FieldSchema(Description = "MongoDB connection string", Example = "mongodb://localhost:27017")]
    public string ConnectionString { get; init; } = "";

    /// <summary>Gets the database name to inspect.</summary>
    [FieldSchema(Description = "Database name to inspect", Example = "mydb")]
    public string Database { get; init; } = "";

    /// <summary>Gets the optional collection name to inspect.</summary>
    [FieldSchema(Description = "Optional collection name to inspect (if omitted, inspects all collections)", IsRequired = false)]
    public string? Collection { get; init; }

    /// <summary>Gets the number of documents to sample for schema inference.</summary>
    [FieldSchema(Description = "Number of documents to sample for schema inference (default 100)", IsRequired = false, Example = "100")]
    public int SampleSize { get; init; } = 100;
}
