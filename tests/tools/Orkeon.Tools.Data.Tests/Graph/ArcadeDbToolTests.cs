using Neo4j.Driver;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Graph;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Graph;

public sealed class ArcadeDbToolTests : IDisposable
{
    private readonly Neo4jStubs.SessionState _state;
    private readonly IDriver _driver;
    private readonly ArcadeDbTool _tool;

    public ArcadeDbToolTests()
    {
        _state = new Neo4jStubs.SessionState();
        _driver = Neo4jStubs.CreateDriver(_state);
        _tool = new ArcadeDbTool(_driver);
    }

    [Fact]
    public async Task ShouldReturnError_WhenBoltUriIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "",
                ["database"] = "mydb",
                [ParamQuery] = "MATCH (n) RETURN n"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("BoltUri", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenDatabaseIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "",
                [ParamQuery] = "MATCH (n) RETURN n"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Database", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenQueryIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = ""
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Query", result.Error);
    }

    [Theory]
    [InlineData("DROP DATABASE mydb")]
    [InlineData("drop database mydb")]
    [InlineData("DROP\tDATABASE mydb")]
    public async Task ShouldBlockDropOperations(string query)
    {
        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = query
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("DROP", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GRANT ALL ON mydb TO user1")]
    [InlineData("grant read on mydb to user1")]
    public async Task ShouldBlockGrantOperations(string query)
    {
        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = query
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("GRANT", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("REVOKE ALL ON mydb FROM user1")]
    [InlineData("revoke read on mydb from user1")]
    public async Task ShouldBlockRevokeOperations(string query)
    {
        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = query
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("REVOKE", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("DELETE n")]
    [InlineData("MATCH (n) DELETE n")]
    public async Task ShouldBlockDeleteWithoutWhereClause(string query)
    {
        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = query
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("DELETE without WHERE", result.Error);
    }

    [Fact]
    public async Task ShouldAllowDeleteWithWhereClause()
    {
        // RecordsForQuery returning empty list = empty cursor.
        _state.RecordsForQuery = _ => Array.Empty<IReadOnlyDictionary<string, object?>>();

        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = "MATCH (n) WHERE n.id = 1 DELETE n"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("MATCH (n) RETURN n LIMIT 10")]
    [InlineData("MATCH (n:Person) WHERE n.name = 'Alice' RETURN n")]
    [InlineData("CREATE (n:Person {name: 'Bob'}) RETURN n")]
    [InlineData("SELECT * FROM Person WHERE name = 'Alice'")]
    public async Task ShouldAllowSafeQueries(string query)
    {
        _state.RecordsForQuery = _ => Array.Empty<IReadOnlyDictionary<string, object?>>();

        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = query
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ShouldDefaultToCypherQueryLanguage()
    {
        _state.RecordsForQuery = _ => Array.Empty<IReadOnlyDictionary<string, object?>>();

        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = "MATCH (n) RETURN n"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("cypher", dict["query_language"]?.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldUseSqlQueryLanguageWhenSpecified()
    {
        _state.RecordsForQuery = _ => Array.Empty<IReadOnlyDictionary<string, object?>>();

        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = "SELECT * FROM Person",
                ["query_language"] = "Sql"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("sql", dict["query_language"]?.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldExtractRecordsFromCursor()
    {
        _state.RecordsForQuery = _ =>
        [
            new Dictionary<string, object?> { ["name"] = "Alice", ["age"] = 30 },
        ];

        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = "MATCH (n:Person) RETURN n.name AS name, n.age AS age"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1, (int)dict["record_count"]!);
    }

    [Fact]
    public async Task ShouldExtractMutationCounters()
    {
        _state.RecordsForQuery = _ => Array.Empty<IReadOnlyDictionary<string, object?>>();
        _state.NodesCreated = 3;
        _state.RelationshipsCreated = 2;
        _state.PropertiesSet = 6;

        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = "CREATE (a:Person {name:'Alice'})-[:KNOWS]->(b:Person {name:'Bob'})"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(3, (int)dict["nodes_created"]!);
        Assert.Equal(2, (int)dict["relationships_created"]!);
        Assert.Equal(6, (int)dict["properties_set"]!);
    }

    [Fact]
    public async Task ShouldRespectMaxResultsLimit()
    {
        // Provide an "unlimited" record stream — the tool should stop at MaxResults.
        var record = (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["id"] = 1 };
        _state.RecordsForQuery = _ => Enumerable.Repeat(record, 1000).ToList();

        var request = new ToolCallRequest(
            ToolName: "arcadedb_query",
            Parameters: new Dictionary<string, object?>
            {
                ["bolt_uri"] = "bolt://localhost:2480",
                ["database"] = "mydb",
                [ParamQuery] = "MATCH (n) RETURN n",
                ["max_results"] = 5
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(5, (int)dict["record_count"]!);
    }

    [Fact]
    public void ShouldHaveCorrectToolMetadata()
    {
        Assert.Equal("arcadedb_query", _tool.Name);
        Assert.Equal("Data Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters["bolt_uri"].Required);
        Assert.True(_tool.Schema.Parameters["database"].Required);
        Assert.True(_tool.Schema.Parameters[ParamQuery].Required);
        Assert.False(_tool.Schema.Parameters["query_language"].Required);
        Assert.False(_tool.Schema.Parameters["max_results"].Required);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _tool.Dispose();
        _driver.Dispose();
    }
}
