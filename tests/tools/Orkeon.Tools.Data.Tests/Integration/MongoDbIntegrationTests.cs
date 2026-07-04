using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.MongoDB;
using Orkeon.Tools.Data.Tests.Integration.Fixtures;
using Testcontainers.MongoDb;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Integration;

/// <summary>
/// Integration tests for MongoDB using Testcontainers (MongoDbContainer).
/// Tests the MongoDbTool and MongoDbSchemaTool against a real MongoDB instance.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MongoDbIntegrationTests : DatabaseTestFixture, IAsyncLifetime
{
    private const string DatabaseName = "integration_test_db";
    private const string CollectionName = "test_items";
    private const string SkipReason = "Docker is not available or container failed to start.";

    private MongoDbContainer? _container;
    private MongoDbTool _tool = null!;
    private MongoDbSchemaTool _schemaTool = null!;
    private string _connectionString = "";
    private bool _containerStarted;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MongoDbBuilder("mongo:7.0")
                .Build();
            // R5.6: bound container startup so a wedged Docker daemon or stalled
            // image pull cannot hang the test run; on timeout the tests skip.
            using var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await _container.StartAsync(startupCts.Token);
            _containerStarted = true;
        }
        catch
        {
            _containerStarted = false;
            return;
        }

        _connectionString = _container.GetConnectionString();

        // Create tools - MongoDbTool can accept a null client (it creates from connection string)
        _tool = new MongoDbTool(logger: null);
        _schemaTool = new MongoDbSchemaTool(logger: null);

        await SeedDatabaseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _tool?.Dispose();
        _schemaTool?.Dispose();
        if (_containerStarted && _container is not null)
            await _container.DisposeAsync();
    }

    private void SkipIfContainerUnavailable()
    {
        Assert.SkipWhen(!_containerStarted, SkipReason);
    }

    private async Task SeedDatabaseAsync()
    {
        using var client = new MongoClient(_connectionString);
        var database = client.GetDatabase(DatabaseName);
        var collection = database.GetCollection<BsonDocument>(CollectionName);

        var documents = new[]
        {
            BsonDocument.Parse("""{"name": "Alice", "age": 30, "department": "Engineering", "active": true}"""),
            BsonDocument.Parse("""{"name": "Bob", "age": 25, "department": "Marketing", "active": true}"""),
            BsonDocument.Parse("""{"name": "Charlie", "age": 35, "department": "Engineering", "active": false}"""),
            BsonDocument.Parse("""{"name": "Diana", "age": 28, "department": "Sales", "active": true}"""),
        };

        await collection.InsertManyAsync(documents);

        // Create an index for schema tests
        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("department")));
    }

    // ── Helper methods ──────────────────────────────────────────────────

    private ToolCallRequest BuildMongoRequest(
        MongoDbOperation operation,
        string? filter = null,
        string? document = null,
        string? update = null,
        string? pipeline = null,
        string? projection = null,
        string? sort = null,
        int limit = 100)
    {
        var dict = new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["database"] = DatabaseName,
            ["collection"] = CollectionName,
            ["operation"] = operation.ToString(),
            ["limit"] = limit,
        };

        if (filter is not null) dict["filter"] = filter;
        if (document is not null) dict["document"] = document;
        if (update is not null) dict["update"] = update;
        if (pipeline is not null) dict["pipeline"] = pipeline;
        if (projection is not null) dict["projection"] = projection;
        if (sort is not null) dict["sort"] = sort;

        return new ToolCallRequest("mongodb_query", dict);
    }

    private ToolCallRequest BuildMongoSchemaRequest(string? collection = null, int sampleSize = 100)
    {
        var dict = new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["database"] = DatabaseName,
            ["sample_size"] = sampleSize,
        };

        if (collection is not null)
            dict["collection"] = collection;

        return new ToolCallRequest("mongodb_schema", dict);
    }

    // ── Find tests ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Find_NoFilter_ReturnsAllDocuments()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoRequest(MongoDbOperation.Find);

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(4L, Convert.ToInt64(data["document_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Find_WithFilter_ReturnsMatchingDocuments()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoRequest(
            MongoDbOperation.Find,
            filter: """{"department": "Engineering"}""");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(2L, Convert.ToInt64(data["document_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Find_WithProjection_ReturnsSelectedFields()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoRequest(
            MongoDbOperation.Find,
            filter: """{"name": "Alice"}""",
            projection: """{"name": 1, "age": 1, "_id": 0}""");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(1L, Convert.ToInt64(data["document_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Find_WithSort_ReturnsSortedDocuments()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoRequest(
            MongoDbOperation.Find,
            sort: """{"age": -1}""",
            limit: 2);

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(2L, Convert.ToInt64(data["document_count"]));
    }

    // ── InsertOne test ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertOne_ValidDocument_InsertsSuccessfully()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoRequest(
            MongoDbOperation.InsertOne,
            document: """{"name": "Eve", "age": 22, "department": "HR", "active": true}""");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.NotNull(data["inserted_id"]);
        Assert.Equal(1L, Convert.ToInt64(data["document_count"]));
    }

    // ── UpdateOne test ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateOne_ExistingDocument_UpdatesSuccessfully()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoRequest(
            MongoDbOperation.UpdateOne,
            filter: """{"name": "Bob"}""",
            update: """{"$set": {"age": 26}}""");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(1L, Convert.ToInt64(data["matched_count"]));
        Assert.Equal(1L, Convert.ToInt64(data["modified_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateOne_NoMatch_ReportsZeroMatches()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoRequest(
            MongoDbOperation.UpdateOne,
            filter: """{"name": "NonExistent"}""",
            update: """{"$set": {"age": 99}}""");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(0L, Convert.ToInt64(data["matched_count"]));
    }

    // ── DeleteOne test ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteOne_ExistingDocument_DeletesSuccessfully()
    {
        SkipIfContainerUnavailable();

        // Insert a document to delete
        var insertReq = BuildMongoRequest(
            MongoDbOperation.InsertOne,
            document: """{"name": "ToDelete", "age": 99, "department": "Temp", "active": false}""");
        await _tool.CallAsync(insertReq, TestContext.Current.CancellationToken);

        var request = BuildMongoRequest(
            MongoDbOperation.DeleteOne,
            filter: """{"name": "ToDelete"}""");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Equal(1L, Convert.ToInt64(data["document_count"]));
    }

    // ── Count test ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Count_AllDocuments_ReturnsCorrectCount()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoRequest(MongoDbOperation.Count);

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.True(Convert.ToInt64(data["document_count"]) >= 4);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Count_WithFilter_ReturnsFilteredCount()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoRequest(
            MongoDbOperation.Count,
            filter: """{"active": true}""");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.True(Convert.ToInt64(data["document_count"]) >= 3);
    }

    // ── Aggregate test ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Aggregate_GroupByDepartment_ReturnsAggregatedResults()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoRequest(
            MongoDbOperation.Aggregate,
            pipeline: """[{"$group": {"_id": "$department", "count": {"$sum": 1}}}]""");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.True(Convert.ToInt64(data["document_count"]) >= 2);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Aggregate_MatchAndSort_ReturnsFilteredSortedResults()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoRequest(
            MongoDbOperation.Aggregate,
            pipeline: """[{"$match": {"active": true}}, {"$sort": {"age": 1}}]""");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.True(Convert.ToInt64(data["document_count"]) >= 1);
    }

    // ── Schema inspection tests ─────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_SingleCollection_ReturnsInferredFields()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoSchemaRequest(CollectionName);

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.NotNull(data["collections"]);
        Assert.Equal(DatabaseName, data["database_name"]?.ToString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_AllCollections_ReturnsCollectionList()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoSchemaRequest();

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.NotNull(data["collections"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_SmallSample_ReturnsSchemaInfo()
    {
        SkipIfContainerUnavailable();

        var request = BuildMongoSchemaRequest(CollectionName, sampleSize: 2);

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.NotNull(data["collections"]);
    }

    // ── Full CRUD cycle ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FullCrudCycle_InsertFindUpdateDelete_Succeeds()
    {
        SkipIfContainerUnavailable();

        // Insert
        var insert = BuildMongoRequest(
            MongoDbOperation.InsertOne,
            document: """{"name": "CRUDTest", "age": 40, "department": "QA", "active": true}""");
        var insertResult = await _tool.CallAsync(insert, TestContext.Current.CancellationToken);
        Assert.True(insertResult.Success, insertResult.Error);

        // Find
        var find = BuildMongoRequest(
            MongoDbOperation.Find,
            filter: """{"name": "CRUDTest"}""");
        var findResult = await _tool.CallAsync(find, TestContext.Current.CancellationToken);
        Assert.True(findResult.Success);
        var findData = Assert.IsType<Dictionary<string, object?>>(findResult.Result);
        Assert.Equal(1L, Convert.ToInt64(findData["document_count"]));

        // Update
        var update = BuildMongoRequest(
            MongoDbOperation.UpdateOne,
            filter: """{"name": "CRUDTest"}""",
            update: """{"$set": {"age": 41, "active": false}}""");
        var updateResult = await _tool.CallAsync(update, TestContext.Current.CancellationToken);
        Assert.True(updateResult.Success);
        var updateData = Assert.IsType<Dictionary<string, object?>>(updateResult.Result);
        Assert.Equal(1L, Convert.ToInt64(updateData["modified_count"]));

        // Delete
        var delete = BuildMongoRequest(
            MongoDbOperation.DeleteOne,
            filter: """{"name": "CRUDTest"}""");
        var deleteResult = await _tool.CallAsync(delete, TestContext.Current.CancellationToken);
        Assert.True(deleteResult.Success);

        // Verify gone
        var verify = BuildMongoRequest(
            MongoDbOperation.Count,
            filter: """{"name": "CRUDTest"}""");
        var verifyResult = await _tool.CallAsync(verify, TestContext.Current.CancellationToken);
        Assert.True(verifyResult.Success);
        var verifyData = Assert.IsType<Dictionary<string, object?>>(verifyResult.Result);
        Assert.Equal(0L, Convert.ToInt64(verifyData["document_count"]));
    }
}
