using MongoDB.Bson;
using MongoDB.Driver;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.MongoDB;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

namespace Orkeon.Tools.Data.Tests.MongoDB;

public sealed class MongoDbToolTests : IDisposable
{
    private readonly MongoStubs.State _state;
    private readonly IMongoClient _client;
    private readonly MongoDbTool _tool;

    public MongoDbToolTests()
    {
        _state = new MongoStubs.State();
        _client = MongoStubs.CreateClient(_state);
        _tool = new MongoDbTool(_client);
    }

    // ── Validation Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenConnectionStringIsEmpty()
    {
        var request = MakeRequest(new Dictionary<string, object?>
        {
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "Find"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(ParamConnectionString, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenDatabaseIsEmpty()
    {
        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["collection"] = "users",
            ["operation"] = "Find"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("database", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenCollectionIsEmpty()
    {
        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["operation"] = "Find"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("collection", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenFilterIsInvalidJson()
    {
        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "Find",
            ["filter"] = "not valid json{{"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Filter must be valid JSON", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenPipelineIsInvalidJson()
    {
        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "Aggregate",
            ["pipeline"] = "not an array"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Pipeline must be a valid JSON array", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenDocumentIsInvalidJson()
    {
        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "InsertOne",
            ["document"] = "{invalid"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Document must be valid JSON", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenUpdateIsInvalidJson()
    {
        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "UpdateOne",
            ["filter"] = "{}",
            ["update"] = "bad json"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Update must be valid JSON", result.Error);
    }

    // ── Security Tests ────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldBlockDeleteOne_WhenFilterIsEmpty()
    {
        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "DeleteOne"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("blocked for safety", result.Error);
    }

    [Fact]
    public async Task ShouldBlockDeleteOne_WhenFilterIsEmptyObject()
    {
        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "DeleteOne",
            ["filter"] = "{}"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("blocked for safety", result.Error);
    }

    // ── Operation Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task ShouldExecuteFind_WhenValidRequest()
    {
        _state.FindResult = new List<BsonDocument>
        {
            new() { { "_id", ObjectId.GenerateNewId() }, { "name", "Alice" } },
            new() { { "_id", ObjectId.GenerateNewId() }, { "name", "Bob" } }
        };

        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "Find",
            ["filter"] = """{"name": "Alice"}"""
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2L, Convert.ToInt64(dict["document_count"]));

        var documents = dict["documents"] as List<object>;
        Assert.NotNull(documents);
        Assert.Equal(2, documents.Count);
    }

    [Fact]
    public async Task ShouldExecuteCount_WhenValidRequest()
    {
        _state.CountResult = 42;

        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "Count",
            ["filter"] = """{"status": "active"}"""
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(42L, Convert.ToInt64(dict["document_count"]));
    }

    [Fact]
    public async Task ShouldExecuteInsertOne_WhenValidRequest()
    {
        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "InsertOne",
            ["document"] = """{"name": "Charlie", "email": "charlie@test.com"}"""
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.NotNull(dict["inserted_id"]);
        Assert.True(_state.InsertCalled);
    }

    [Fact]
    public async Task ShouldExecuteUpdateOne_WhenValidRequest()
    {
        _state.UpdateMatched = 1;
        _state.UpdateModified = 1;

        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "UpdateOne",
            ["filter"] = """{"name": "Alice"}""",
            ["update"] = """{"$set": {"status": "inactive"}}"""
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1L, Convert.ToInt64(dict["matched_count"]));
        Assert.Equal(1L, Convert.ToInt64(dict["modified_count"]));
    }

    [Fact]
    public async Task ShouldExecuteDeleteOne_WhenFilterIsSpecific()
    {
        _state.DeletedCount = 1;

        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "DeleteOne",
            ["filter"] = """{"_id": "some-id"}"""
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1L, Convert.ToInt64(dict["document_count"]));
    }

    // ── R2.4: aggregation stage allowlist ─────────────────────────────────

    [Theory]
    [InlineData("""[{"$where": "this.x == 1"}]""")]
    [InlineData("""[{"$function": {"body": "function(){}", "args": [], "lang": "js"}}]""")]
    [InlineData("""[{"$group": {"_id": null, "t": {"$accumulator": {}}}}, {"$out": "evil"}]""")]
    [InlineData("""[{"$merge": "target"}]""")]
    [InlineData("""[{"$out": "evil"}]""")]
    [InlineData("""[{"$lookup": {"from": "secrets", "localField": "a", "foreignField": "b", "as": "c"}}]""")]
    public async Task ShouldBlockAggregate_WhenStageNotAllowed(string pipeline)
    {
        _state.FindResult = new List<BsonDocument>();

        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "Aggregate",
            ["pipeline"] = pipeline
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("not allowed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldAllowAggregate_WhenStagesAreSafe()
    {
        _state.FindResult = new List<BsonDocument>
        {
            new() { { "_id", "active" }, { "total", 3 } }
        };

        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users",
            ["operation"] = "Aggregate",
            ["pipeline"] = """[{"$match": {"status": "active"}}, {"$group": {"_id": "$status", "total": {"$sum": 1}}}, {"$sort": {"total": -1}}]"""
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }

    // ── Schema Tests ──────────────────────────────────────────────────────

    [Fact]
    public void ShouldHaveCorrectToolName()
    {
        Assert.Equal("mongodb_query", _tool.Name);
    }

    [Fact]
    public void ShouldHaveCorrectCategory()
    {
        Assert.Equal("Data Operations", _tool.Category);
    }

    [Fact]
    public void ShouldHaveRequiredSchemaParameters()
    {
        Assert.True(_tool.Schema.Parameters[ParamConnectionString].Required);
        Assert.True(_tool.Schema.Parameters["database"].Required);
        Assert.True(_tool.Schema.Parameters["collection"].Required);
    }

    [Fact]
    public void ShouldHaveOptionalSchemaParameters()
    {
        Assert.False(_tool.Schema.Parameters["filter"].Required);
        Assert.False(_tool.Schema.Parameters["projection"].Required);
        Assert.False(_tool.Schema.Parameters["pipeline"].Required);
        Assert.False(_tool.Schema.Parameters["document"].Required);
        Assert.False(_tool.Schema.Parameters["update"].Required);
        Assert.False(_tool.Schema.Parameters["sort"].Required);
    }

    private static ToolCallRequest MakeRequest(Dictionary<string, object?> parameters)
        => new(ToolName: "mongodb_query", Parameters: parameters);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _tool.Dispose();
        _client.Dispose();
    }
}
