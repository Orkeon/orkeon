using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Data.MongoDB.Models;

namespace Orkeon.Tools.Data.MongoDB;

/// <summary>
/// Tool for inspecting MongoDB database schema by sampling documents.
/// </summary>
[ToolContract("mongodb_schema",
    Name = "mongodb_schema",
    Description = "Inspect MongoDB database schema by sampling documents",
    Category = "Data Operations")]
public partial class MongoDbSchemaTool : ToolBase<MongoDbSchemaRequest, MongoDbSchemaResponse>
{
    private const int MaxSampleValues = 5;

    private readonly IMongoClient? _client;

    /// <summary>
    /// Initializes a new instance of <see cref="MongoDbSchemaTool"/>.
    /// </summary>
    /// <param name="client">Optional pre-configured MongoDB client. If null, creates from connection string.</param>
    /// <param name="logger">Optional logger instance.</param>
    public MongoDbSchemaTool(IMongoClient? client = null, ILogger<MongoDbSchemaTool>? logger = null) : base(logger)
    {
        _client = client;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(MongoDbSchemaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return "Connection string cannot be empty";

        if (string.IsNullOrWhiteSpace(request.Database))
            return "Database name cannot be empty";

        if (request.SampleSize <= 0)
            return "Sample size must be greater than zero";

        return null;
    }

    /// <inheritdoc />
    protected override Task<MongoDbSchemaResponse> ExecuteTypedAsync(
        MongoDbSchemaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync();

        async Task<MongoDbSchemaResponse> ExecuteCoreAsync()
        {
        // ownedClient is non-null only when this method creates the client, so the
        // finally block can dispose it unconditionally (an injected client is owned
        // by the caller and must not be disposed here).
        var ownedClient = _client is null ? new MongoClient(request.ConnectionString) : null;
        var client = _client ?? ownedClient!;
        try
        {
            var database = client.GetDatabase(request.Database);

            var collectionNames = new List<string>();

            if (!string.IsNullOrWhiteSpace(request.Collection))
            {
                collectionNames.Add(request.Collection);
            }
            else
            {
                using var cursor = await database.ListCollectionNamesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                collectionNames.AddRange(await cursor.ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            var collections = new List<CollectionInfo>();

            foreach (var collectionName in collectionNames)
            {
                var info = await InspectCollectionAsync(database, collectionName, request.SampleSize, cancellationToken).ConfigureAwait(false);
                collections.Add(info);
            }

            LogSchemaInspectionCompleted(request.Database, collections.Count);

            return new MongoDbSchemaResponse
            {
                Collections = collections,
                DatabaseName = request.Database
            };
        }
        finally
        {
            (ownedClient as IDisposable)?.Dispose();
        }
        }
    }

    private static async Task<CollectionInfo> InspectCollectionAsync(
        IMongoDatabase database, string collectionName, int sampleSize, CancellationToken ct)
    {
        var collection = database.GetCollection<BsonDocument>(collectionName);

        // Get document count
        var documentCount = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: ct).ConfigureAwait(false);

        // Get indexes
        var indexes = await GetIndexesAsync(collection, ct).ConfigureAwait(false);

        // Get indexed field names for cross-referencing
        var indexedFields = indexes
            .SelectMany(idx => idx.Keys.Keys)
            .ToHashSet(StringComparer.Ordinal);

        // Sample documents for field inference
        var inferredFields = await InferFieldsAsync(collection, sampleSize, indexedFields, ct).ConfigureAwait(false);

        return new CollectionInfo
        {
            Name = collectionName,
            DocumentCount = documentCount,
            Indexes = indexes,
            InferredFields = inferredFields
        };
    }

    private static async Task<List<MongoIndexInfo>> GetIndexesAsync(
        IMongoCollection<BsonDocument> collection, CancellationToken ct)
    {
        var result = new List<MongoIndexInfo>();

        using var cursor = await collection.Indexes.ListAsync(ct).ConfigureAwait(false);
        var indexDocs = await cursor.ToListAsync(ct).ConfigureAwait(false);

        foreach (var indexDoc in indexDocs)
        {
            var keys = new Dictionary<string, int>();
            if (indexDoc.TryGetValue("key", out var keyValue) && keyValue is BsonDocument keyDoc)
            {
                foreach (var element in keyDoc)
                {
                    keys[element.Name] = element.Value.IsInt32 ? element.Value.AsInt32 : 1;
                }
            }

            result.Add(new MongoIndexInfo
            {
                Name = indexDoc.GetValue("name", "").AsString,
                Keys = keys,
                IsUnique = indexDoc.GetValue("unique", false).ToBoolean(),
                IsSparse = indexDoc.GetValue("sparse", false).ToBoolean()
            });
        }

        return result;
    }

    internal static async Task<List<InferredField>> InferFieldsAsync(
        IMongoCollection<BsonDocument> collection, int sampleSize, HashSet<string> indexedFields, CancellationToken ct)
    {
        using var cursor = await collection.FindAsync(
            FilterDefinition<BsonDocument>.Empty,
            new FindOptions<BsonDocument> { Limit = sampleSize },
            ct).ConfigureAwait(false);

        var documents = await cursor.ToListAsync(ct).ConfigureAwait(false);
        if (documents.Count == 0)
            return [];

        var totalDocs = documents.Count;

        // Track field occurrences, types, and sample values
        var fieldOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var fieldTypes = new Dictionary<string, HashSet<BsonType>>(StringComparer.Ordinal);
        var fieldSamples = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var doc in documents)
        {
            CollectFields(doc, prefix: "", fieldOccurrences, fieldTypes, fieldSamples);
        }

        return fieldOccurrences
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv =>
            {
                var types = fieldTypes.GetValueOrDefault(kv.Key, []);
                var primaryType = types.OrderByDescending(t => t.ToString()).FirstOrDefault();

                return new InferredField
                {
                    Name = kv.Key,
                    BsonType = primaryType.ToString(),
                    Frequency = Math.Round((double)kv.Value / totalDocs, 4),
                    IsIndexed = indexedFields.Contains(kv.Key),
                    SampleValues = fieldSamples.GetValueOrDefault(kv.Key, [])
                };
            })
            .ToList();
    }

    private static void CollectFields(
        BsonDocument doc,
        string prefix,
        Dictionary<string, int> occurrences,
        Dictionary<string, HashSet<BsonType>> types,
        Dictionary<string, List<string>> samples)
    {
        foreach (var element in doc)
        {
            var fieldName = string.IsNullOrEmpty(prefix) ? element.Name : $"{prefix}.{element.Name}";

            occurrences[fieldName] = occurrences.GetValueOrDefault(fieldName) + 1;

            if (!types.TryGetValue(fieldName, out var typeSet))
            {
                typeSet = [];
                types[fieldName] = typeSet;
            }
            typeSet.Add(element.Value.BsonType);

            if (!samples.TryGetValue(fieldName, out var sampleList))
            {
                sampleList = [];
                samples[fieldName] = sampleList;
            }
            if (sampleList.Count < MaxSampleValues && element.Value.BsonType != BsonType.Document && element.Value.BsonType != BsonType.Array)
            {
                sampleList.Add(element.Value.ToString()!);
            }

            // Recurse into nested documents
            if (element.Value is BsonDocument nested)
            {
                CollectFields(nested, fieldName, occurrences, types, samples);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Schema inspection completed for database {Database}: {CollectionCount} collections")]
    private partial void LogSchemaInspectionCompleted(string database, int collectionCount);
}
