using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.MongoDB;

/// <summary>
/// MongoDB operations supported by the MongoDbTool.
/// </summary>
public enum MongoDbOperation
{
    /// <summary>Query documents matching a filter.</summary>
    Find,
    /// <summary>Execute an aggregation pipeline.</summary>
    Aggregate,
    /// <summary>Insert a single document.</summary>
    InsertOne,
    /// <summary>Update a single document matching a filter.</summary>
    UpdateOne,
    /// <summary>Delete a single document matching a filter.</summary>
    DeleteOne,
    /// <summary>Count documents matching a filter.</summary>
    Count,
    /// <summary>Get distinct values for a field.</summary>
    Distinct
}

/// <summary>
/// Request parameters for executing a MongoDB operation.
/// </summary>
public record MongoDbRequest
{
    /// <summary>Gets the MongoDB connection string.</summary>
    [FieldSchema(Description = "MongoDB connection string", Example = "mongodb://localhost:27017")]
    public string ConnectionString { get; init; } = "";

    /// <summary>Gets the database name to operate on.</summary>
    [FieldSchema(Description = "Database name to operate on", Example = "mydb")]
    public string Database { get; init; } = "";

    /// <summary>Gets the collection name to operate on.</summary>
    [FieldSchema(Description = "Collection name to operate on", Example = "users")]
    public string Collection { get; init; } = "";

    /// <summary>Gets the operation to perform.</summary>
    [FieldSchema(Description = "Operation to perform: Find, Aggregate, InsertOne, UpdateOne, DeleteOne, Count, Distinct", Example = "Find")]
    public MongoDbOperation Operation { get; init; } = MongoDbOperation.Find;

    /// <summary>Gets the JSON filter document for Find/Update/Delete/Count/Distinct operations.</summary>
    [FieldSchema(Description = "JSON filter document for Find/Update/Delete/Count/Distinct operations", IsRequired = false, Example = """{"status": "active"}""")]
    public string? Filter { get; init; }

    /// <summary>Gets the JSON projection document for Find operations.</summary>
    [FieldSchema(Description = "JSON projection document for Find operations", IsRequired = false, Example = """{"name": 1, "email": 1}""")]
    public string? Projection { get; init; }

    /// <summary>Gets the JSON array of pipeline stages for Aggregate operations.</summary>
    [FieldSchema(Description = "JSON array of pipeline stages for Aggregate operations", IsRequired = false, Example = """[{"$match": {"status": "active"}}, {"$group": {"_id": "$department", "count": {"$sum": 1}}}]""")]
    public string? Pipeline { get; init; }

    /// <summary>Gets the JSON document to insert for InsertOne operations.</summary>
    [FieldSchema(Description = "JSON document to insert for InsertOne operations", IsRequired = false, Example = """{"name": "Alice", "email": "alice@example.com"}""")]
    public string? Document { get; init; }

    /// <summary>Gets the JSON update document for UpdateOne operations.</summary>
    [FieldSchema(Description = "JSON update document for UpdateOne operations", IsRequired = false, Example = """{"$set": {"status": "inactive"}}""")]
    public string? Update { get; init; }

    /// <summary>Gets the JSON sort document for Find operations.</summary>
    [FieldSchema(Description = "JSON sort document for Find operations", IsRequired = false, Example = """{"created_at": -1}""")]
    public string? Sort { get; init; }

    /// <summary>Gets the maximum number of documents to return.</summary>
    [FieldSchema(Description = "Maximum number of documents to return (default 100)", IsRequired = false, Example = "100")]
    public int Limit { get; init; } = 100;

    /// <summary>Gets the number of documents to skip.</summary>
    [FieldSchema(Description = "Number of documents to skip", IsRequired = false, Example = "0")]
    public int Skip { get; init; }
}
