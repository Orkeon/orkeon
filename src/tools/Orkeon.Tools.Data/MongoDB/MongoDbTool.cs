using System.Diagnostics;
using global::MongoDB.Bson;
using global::MongoDB.Bson.IO;
using global::MongoDB.Bson.Serialization;
using global::MongoDB.Driver;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Data.MongoDB;

/// <summary>
/// Tool for executing operations on MongoDB databases (Find, Aggregate, CRUD).
/// </summary>
[ToolContract("mongodb_query",
    Name = "mongodb_query",
    Description = "Execute operations on MongoDB databases (Find, Aggregate, CRUD)",
    Category = "Data Operations")]
public partial class MongoDbTool : ToolBase<MongoDbRequest, MongoDbResponse>
{
    private readonly IMongoClient? _client;

    /// <summary>
    /// Allowlist of safe aggregation pipeline stages. Any stage not in this set is rejected.
    /// This is a fail-closed allowlist (not a blocklist): server-side JS execution
    /// ($where, $function, $accumulator), output stages ($out, $merge) and any unknown
    /// stage are blocked by default. Cross-collection stages ($lookup, $graphLookup,
    /// $unionWith) are intentionally excluded and require an explicit opt-in.
    /// </summary>
    private static readonly HashSet<string> AllowedAggregationStages = new(StringComparer.Ordinal)
    {
        "$match", "$group", "$project", "$sort", "$limit", "$skip", "$count",
        "$unwind", "$addFields", "$set", "$unset", "$replaceRoot", "$replaceWith",
        "$sortByCount", "$bucket", "$bucketAuto", "$facet", "$sample", "$redact"
    };

    /// <summary>
    /// Initializes a new instance of <see cref="MongoDbTool"/>.
    /// </summary>
    /// <param name="client">Optional pre-configured MongoDB client. If null, creates from connection string.</param>
    /// <param name="logger">Optional logger instance.</param>
    public MongoDbTool(IMongoClient? client = null, ILogger<MongoDbTool>? logger = null) : base(logger)
    {
        _client = client;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(MongoDbRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return "Connection string cannot be empty";

        if (string.IsNullOrWhiteSpace(request.Database))
            return "Database name cannot be empty";

        if (string.IsNullOrWhiteSpace(request.Collection))
            return "Collection name cannot be empty";

        if (request.Filter is not null && !IsValidJson(request.Filter))
            return "Filter must be valid JSON";

        if (request.Projection is not null && !IsValidJson(request.Projection))
            return "Projection must be valid JSON";

        if (request.Document is not null && !IsValidJson(request.Document))
            return "Document must be valid JSON";

        if (request.Update is not null && !IsValidJson(request.Update))
            return "Update must be valid JSON";

        if (request.Pipeline is not null && !IsValidJsonArray(request.Pipeline))
            return "Pipeline must be a valid JSON array";

        if (request.Sort is not null && !IsValidJson(request.Sort))
            return "Sort must be valid JSON";

        return null;
    }

    /// <inheritdoc />
    protected override Task<MongoDbResponse> ExecuteTypedAsync(
        MongoDbRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<MongoDbResponse> ExecuteTypedCoreAsync()
        {
            // ownedClient is non-null only when this method creates the client, so the
            // finally block can dispose it unconditionally (an injected client is owned
            // by the caller and must not be disposed here).
            var ownedClient = _client is null ? new MongoClient(request.ConnectionString) : null;
            var client = _client ?? ownedClient!;
            try
            {
                var database = client.GetDatabase(request.Database);
                var collection = database.GetCollection<BsonDocument>(request.Collection);

                var sw = Stopwatch.StartNew();

                var response = request.Operation switch
                {
                    MongoDbOperation.Find => await ExecuteFindAsync(collection, request, cancellationToken).ConfigureAwait(false),
                    MongoDbOperation.Aggregate => await ExecuteAggregateAsync(collection, request, cancellationToken).ConfigureAwait(false),
                    MongoDbOperation.InsertOne => await ExecuteInsertOneAsync(collection, request, cancellationToken).ConfigureAwait(false),
                    MongoDbOperation.UpdateOne => await ExecuteUpdateOneAsync(collection, request, cancellationToken).ConfigureAwait(false),
                    MongoDbOperation.DeleteOne => await ExecuteDeleteOneAsync(collection, request, cancellationToken).ConfigureAwait(false),
                    MongoDbOperation.Count => await ExecuteCountAsync(collection, request, cancellationToken).ConfigureAwait(false),
                    MongoDbOperation.Distinct => await ExecuteDistinctAsync(collection, request, cancellationToken).ConfigureAwait(false),
                    _ => throw new InvalidOperationException($"Unsupported operation: {request.Operation}")
                };

                sw.Stop();

                LogOperationCompleted(request.Operation, request.Collection, sw.ElapsedMilliseconds);

                return response with
                {
                    Operation = request.Operation.ToString(),
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };
            }
            finally
            {
                (ownedClient as IDisposable)?.Dispose();
            }
        }
    }

    private static async Task<MongoDbResponse> ExecuteFindAsync(
        IMongoCollection<BsonDocument> collection, MongoDbRequest request, CancellationToken ct)
    {
        var filter = ParseFilter(request.Filter);
        var options = new FindOptions<BsonDocument>
        {
            Limit = request.Limit,
            Skip = request.Skip
        };

        if (request.Projection is not null)
            options.Projection = BsonDocument.Parse(request.Projection);

        if (request.Sort is not null)
            options.Sort = BsonDocument.Parse(request.Sort);

        using var cursor = await collection.FindAsync(filter, options, ct).ConfigureAwait(false);
        var documents = await cursor.ToListAsync(ct).ConfigureAwait(false);

        return new MongoDbResponse
        {
            Documents = documents.Select(BsonToJson).ToList(),
            DocumentCount = documents.Count
        };
    }

    private static async Task<MongoDbResponse> ExecuteAggregateAsync(
        IMongoCollection<BsonDocument> collection, MongoDbRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Pipeline))
            return new MongoDbResponse { Documents = [] };

        var pipeline = BsonSerializer.Deserialize<BsonArray>(request.Pipeline);
        var stages = pipeline.Select(s => (BsonDocument)s).ToList();

        // Allowlist: reject any stage not explicitly permitted (fail-closed).
        // This blocks server-side JS ($where/$function/$accumulator), output stages
        // ($out/$merge), cross-collection joins ($lookup/$graphLookup/$unionWith) and
        // any unknown/future stage by default.
        foreach (var stage in stages)
        {
            var stageName = stage.Names.FirstOrDefault();
            if (stageName is null || !AllowedAggregationStages.Contains(stageName))
                throw new InvalidOperationException(
                    $"Aggregation stage '{stageName ?? "(empty)"}' is not allowed. Only read-only stages are permitted.");
        }

        var pipelineDefinition = PipelineDefinition<BsonDocument, BsonDocument>.Create(stages);

        using var cursor = await collection.AggregateAsync(pipelineDefinition, cancellationToken: ct).ConfigureAwait(false);
        var documents = await cursor.ToListAsync(ct).ConfigureAwait(false);

        return new MongoDbResponse
        {
            Documents = documents.Select(BsonToJson).ToList(),
            DocumentCount = documents.Count
        };
    }

    private static async Task<MongoDbResponse> ExecuteInsertOneAsync(
        IMongoCollection<BsonDocument> collection, MongoDbRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Document))
            throw new InvalidOperationException("Document is required for InsertOne operation");

        var document = BsonDocument.Parse(request.Document);

        // Ensure _id is present before insertion (MongoDB driver normally generates one)
        if (!document.Contains("_id"))
            document["_id"] = ObjectId.GenerateNewId();

        await collection.InsertOneAsync(document, cancellationToken: ct).ConfigureAwait(false);

        return new MongoDbResponse
        {
            InsertedId = document["_id"].ToString(),
            DocumentCount = 1
        };
    }

    private static async Task<MongoDbResponse> ExecuteUpdateOneAsync(
        IMongoCollection<BsonDocument> collection, MongoDbRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Update))
            throw new InvalidOperationException("Update document is required for UpdateOne operation");

        var filter = ParseFilter(request.Filter);
        var update = BsonDocument.Parse(request.Update);

        var result = await collection.UpdateOneAsync(filter, update, cancellationToken: ct).ConfigureAwait(false);

        return new MongoDbResponse
        {
            MatchedCount = result.MatchedCount,
            ModifiedCount = result.ModifiedCount
        };
    }

    private static async Task<MongoDbResponse> ExecuteDeleteOneAsync(
        IMongoCollection<BsonDocument> collection, MongoDbRequest request, CancellationToken ct)
    {
        var filter = ParseFilter(request.Filter);

        // Block empty filter to prevent accidental mass deletion
        if (filter == FilterDefinition<BsonDocument>.Empty || request.Filter is null or "{}")
            throw new InvalidOperationException("DeleteOne with an empty filter is blocked for safety. Provide a specific filter.");

        var result = await collection.DeleteOneAsync(filter, ct).ConfigureAwait(false);

        return new MongoDbResponse
        {
            DocumentCount = result.DeletedCount
        };
    }

    private static async Task<MongoDbResponse> ExecuteCountAsync(
        IMongoCollection<BsonDocument> collection, MongoDbRequest request, CancellationToken ct)
    {
        var filter = ParseFilter(request.Filter);
        var count = await collection.CountDocumentsAsync(filter, cancellationToken: ct).ConfigureAwait(false);

        return new MongoDbResponse
        {
            DocumentCount = count
        };
    }

    private static async Task<MongoDbResponse> ExecuteDistinctAsync(
        IMongoCollection<BsonDocument> collection, MongoDbRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Projection))
            throw new InvalidOperationException("Projection field name is required for Distinct operation (use the projection parameter as the field name)");

        var filter = ParseFilter(request.Filter);

        using var cursor = await collection.DistinctAsync<BsonValue>(request.Projection, filter, cancellationToken: ct).ConfigureAwait(false);
        var values = await cursor.ToListAsync(ct).ConfigureAwait(false);

        return new MongoDbResponse
        {
            DistinctValues = values.Select(v => v.ToString()!).ToList(),
            DocumentCount = values.Count
        };
    }

    private static FilterDefinition<BsonDocument> ParseFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return FilterDefinition<BsonDocument>.Empty;

        return BsonDocument.Parse(filter);
    }

    private static string BsonToJson(BsonDocument doc)
    {
        return doc.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Validity-probe fault barrier: any parse failure from BsonDocument.Parse means the input is not valid JSON and the method returns false; the specific exception type is irrelevant to the boolean verdict.")]
    private static bool IsValidJson(string json)
    {
        try
        {
            BsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Validity-probe fault barrier: any deserialization failure from BsonSerializer.Deserialize means the input is not a valid JSON array and the method returns false; the specific exception type is irrelevant to the boolean verdict.")]
    private static bool IsValidJsonArray(string json)
    {
        try
        {
            BsonSerializer.Deserialize<BsonArray>(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MongoDB {Operation} on {Collection} completed in {ElapsedMs}ms")]
    private partial void LogOperationCompleted(MongoDbOperation operation, string collection, long elapsedMs);
}
