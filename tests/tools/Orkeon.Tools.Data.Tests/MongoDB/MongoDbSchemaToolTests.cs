using MongoDB.Bson;
using MongoDB.Driver;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.MongoDB;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

namespace Orkeon.Tools.Data.Tests.MongoDB;

public sealed class MongoDbSchemaToolTests : IDisposable
{
    private readonly MongoStubs.State _state;
    private readonly IMongoClient _client;
    private readonly MongoDbSchemaTool _tool;

    public MongoDbSchemaToolTests()
    {
        _state = new MongoStubs.State
        {
            IndexesResult = new List<BsonDocument>
            {
                new() { { "name", "_id_" }, { "key", new BsonDocument("_id", 1) } }
            }
        };
        _client = MongoStubs.CreateClient(_state);
        _tool = new MongoDbSchemaTool(_client);
    }

    [Fact]
    public async Task ShouldReturnError_WhenConnectionStringIsEmpty()
    {
        var request = MakeRequest(new Dictionary<string, object?>
        {
            ["database"] = "testdb"
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
            [ParamConnectionString] = TestMongoDbConnectionString
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("database", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldInspectSingleCollection_WhenCollectionSpecified()
    {
        _state.FindResult = new List<BsonDocument>
        {
            new() { { "_id", ObjectId.GenerateNewId() }, { "name", "Alice" }, { "age", 30 } },
            new() { { "_id", ObjectId.GenerateNewId() }, { "name", "Bob" }, { "age", 25 } }
        };
        _state.CountResult = _state.FindResult.Count;

        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "users"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("testdb", dict["database_name"]?.ToString());
    }

    [Fact]
    public async Task ShouldListAllCollections_WhenNoCollectionSpecified()
    {
        _state.CollectionNames = new List<string> { "users", "orders" };
        _state.FindResult = new List<BsonDocument>
        {
            new() { { "_id", ObjectId.GenerateNewId() }, { "name", "Alice" } }
        };
        _state.CountResult = _state.FindResult.Count;

        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ShouldReturnEmptyFields_WhenCollectionIsEmpty()
    {
        _state.FindResult = new List<BsonDocument>();
        _state.CountResult = 0;

        var request = MakeRequest(new Dictionary<string, object?>
        {
            [ParamConnectionString] = TestMongoDbConnectionString,
            ["database"] = "testdb",
            ["collection"] = "empty_collection"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task InferFields_ShouldDetectFieldTypes()
    {
        var docs = new List<BsonDocument>
        {
            new() { { "name", "Alice" }, { "age", 30 }, { "active", true } },
            new() { { "name", "Bob" }, { "age", 25 }, { "active", false } }
        };

        var col = MongoStubs.CreateCollection(docs);

        var fields = await MongoDbSchemaTool.InferFieldsAsync(
            col, sampleSize: 100, new HashSet<string> { "name" }, CancellationToken.None);

        Assert.Equal(3, fields.Count);

        var nameField = fields.First(f => f.Name == "name");
        Assert.Equal("String", nameField.BsonType);
        Assert.Equal(1.0, nameField.Frequency);
        Assert.True(nameField.IsIndexed);
        Assert.Contains("Alice", nameField.SampleValues);

        var ageField = fields.First(f => f.Name == "age");
        Assert.Equal("Int32", ageField.BsonType);
        Assert.False(ageField.IsIndexed);
    }

    [Fact]
    public async Task InferFields_ShouldCalculateFrequency()
    {
        var docs = new List<BsonDocument>
        {
            new() { { "name", "Alice" }, { "email", "a@test.com" } },
            new() { { "name", "Bob" } }  // no email
        };

        var col = MongoStubs.CreateCollection(docs);

        var fields = await MongoDbSchemaTool.InferFieldsAsync(
            col, sampleSize: 100, new HashSet<string>(), CancellationToken.None);

        var nameField = fields.First(f => f.Name == "name");
        Assert.Equal(1.0, nameField.Frequency);

        var emailField = fields.First(f => f.Name == "email");
        Assert.Equal(0.5, emailField.Frequency);
    }

    [Fact]
    public async Task InferFields_ShouldHandleNestedDocuments()
    {
        var docs = new List<BsonDocument>
        {
            new() { { "name", "Alice" }, { "address", new BsonDocument { { "city", "Paris" }, { "zip", "75001" } } } }
        };

        var col = MongoStubs.CreateCollection(docs);

        var fields = await MongoDbSchemaTool.InferFieldsAsync(
            col, sampleSize: 100, new HashSet<string>(), CancellationToken.None);

        Assert.Contains(fields, f => f.Name == "address");
        Assert.Contains(fields, f => f.Name == "address.city");
        Assert.Contains(fields, f => f.Name == "address.zip");
    }

    [Fact]
    public void ShouldHaveCorrectToolName()
    {
        Assert.Equal("mongodb_schema", _tool.Name);
    }

    [Fact]
    public void ShouldHaveCorrectCategory()
    {
        Assert.Equal("Data Operations", _tool.Category);
    }

    private static ToolCallRequest MakeRequest(Dictionary<string, object?> parameters)
        => new(ToolName: "mongodb_schema", Parameters: parameters);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _tool.Dispose();
        _client.Dispose();
    }
}
