using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Messages;
using Neo4j.Driver;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Graph;

namespace Orkeon.Tools.Data.Tests.Graph;

public sealed class GraphSchemaToolTests : IDisposable
{
    private readonly StubGremlinClient _gremlinClient;
    private readonly Neo4jStubs.SessionState _state;
    private readonly IDriver _boltDriver;
    private readonly GraphSchemaTool _tool;

    public GraphSchemaToolTests()
    {
        _gremlinClient = new StubGremlinClient();
        _state = new Neo4jStubs.SessionState();
        _boltDriver = Neo4jStubs.CreateDriver(_state);
        _tool = new GraphSchemaTool(_gremlinClient, _boltDriver);
    }

    [Fact]
    public async Task ShouldReturnError_WhenEndpointIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "graph_schema",
            Parameters: new Dictionary<string, object?>
            {
                ["endpoint"] = "",
                ["graph_type"] = "JanusGraph"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Endpoint", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenArcadeDBMissingDatabase()
    {
        var request = new ToolCallRequest(
            ToolName: "graph_schema",
            Parameters: new Dictionary<string, object?>
            {
                ["endpoint"] = "bolt://localhost:2480",
                ["graph_type"] = "ArcadeDB"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Database", result.Error);
    }

    [Fact]
    public async Task ShouldIntrospectJanusGraphSchema()
    {
        _gremlinClient.SetStringResult("g.V().label().dedup()", ["person", "software"]);
        _gremlinClient.SetStringResult("g.E().label().dedup()", ["knows", "created"]);
        _gremlinClient.SetStringResult(
            "g.V().hasLabel('person').properties().key().dedup()", ["name", "age"]);
        _gremlinClient.SetStringResult(
            "g.V().hasLabel('software').properties().key().dedup()", ["name", "lang"]);
        _gremlinClient.SetStringResult(
            "g.E().hasLabel('knows').properties().key().dedup()", ["since"]);
        _gremlinClient.SetStringResult(
            "g.E().hasLabel('created').properties().key().dedup()", ["weight"]);

        var request = new ToolCallRequest(
            ToolName: "graph_schema",
            Parameters: new Dictionary<string, object?>
            {
                ["endpoint"] = "ws://localhost:8182/gremlin",
                ["graph_type"] = "JanusGraph"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("janusGraph", dict["graph_type"]?.ToString());
    }

    [Fact]
    public async Task ShouldIntrospectArcadeDbSchema()
    {
        var byQuery = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>
        {
            ["MATCH (n) RETURN DISTINCT labels(n) AS labels"] =
            [
                new Dictionary<string, object?> { ["labels"] = (IReadOnlyList<object>)new List<object> { "Person" }.AsReadOnly() },
                new Dictionary<string, object?> { ["labels"] = (IReadOnlyList<object>)new List<object> { "Movie" }.AsReadOnly() },
            ],
            ["MATCH ()-[r]->() RETURN DISTINCT type(r) AS type"] =
            [
                new Dictionary<string, object?> { ["type"] = "ACTED_IN" },
                new Dictionary<string, object?> { ["type"] = "DIRECTED" },
            ],
        };
        _state.RecordsForQuery = q => byQuery.TryGetValue(q, out var rs) ? rs : Array.Empty<IReadOnlyDictionary<string, object?>>();

        var request = new ToolCallRequest(
            ToolName: "graph_schema",
            Parameters: new Dictionary<string, object?>
            {
                ["endpoint"] = "bolt://localhost:2480",
                ["graph_type"] = "ArcadeDB",
                ["database"] = "mydb"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("arcadeDB", dict["graph_type"]?.ToString());
    }

    [Fact]
    public void ShouldHaveCorrectToolMetadata()
    {
        Assert.Equal("graph_schema", _tool.Name);
        Assert.Equal("Data Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters["endpoint"].Required);
        Assert.True(_tool.Schema.Parameters["graph_type"].Required);
        Assert.False(_tool.Schema.Parameters["database"].Required);
        Assert.False(_tool.Schema.Parameters["username"].Required);
        Assert.False(_tool.Schema.Parameters["password"].Required);
    }

    [Theory]
    [InlineData("person", "person")]
    [InlineData("it's", "it\\'s")]
    [InlineData("back\\slash", "back\\\\slash")]
    public void ShouldEscapeGremlinStrings(string input, string expected)
    {
        var result = GraphSchemaTool.EscapeGremlinString(input);
        Assert.Equal(expected, result);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _tool.Dispose();
        _boltDriver.Dispose();
        _gremlinClient.Dispose();
    }

    /// <summary>Hand-rolled IGremlinClient for the schema tests.</summary>
    private sealed class StubGremlinClient : IGremlinClient
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _stringResults = new(StringComparer.Ordinal);

        public void SetStringResult(string traversal, IEnumerable<string> values)
            => _stringResults[traversal] = values.ToList();

        public Task<ResultSet<T>> SubmitAsync<T>(RequestMessage requestMessage, CancellationToken cancellationToken = default)
        {
            var traversal = requestMessage.Arguments.TryGetValue(Tokens.ArgsGremlin, out var g) ? g as string : null;
            if (typeof(T) == typeof(string) && traversal is not null && _stringResults.TryGetValue(traversal, out var v))
            {
                var rs = new ResultSet<string>(v.ToList(), new Dictionary<string, object>());
                return Task.FromResult((ResultSet<T>)(object)rs);
            }
            return Task.FromResult(new ResultSet<T>(new List<T>(), new Dictionary<string, object>()));
        }

        public static ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
        public static int NrConnections => 0;
    }
}
