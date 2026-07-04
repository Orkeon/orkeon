using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Messages;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Graph;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Graph;

public sealed class JanusGraphToolTests : IDisposable
{
    private readonly StubGremlinClient _client;
    private readonly JanusGraphTool _tool;

    public JanusGraphToolTests()
    {
        _client = new StubGremlinClient();
        _tool = new JanusGraphTool(_client);
    }

    // ── Validation tests ──────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenGremlinEndpointIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "janusgraph_query",
            Parameters: new Dictionary<string, object?>
            {
                ["gremlin_endpoint"] = "",
                ["traversal"] = "g.V().limit(10)"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("GremlinEndpoint", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenTraversalIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "janusgraph_query",
            Parameters: new Dictionary<string, object?>
            {
                ["gremlin_endpoint"] = "ws://localhost:8182/gremlin",
                ["traversal"] = ""
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Traversal", result.Error);
    }

    // ── Security tests ──────────────────────────────────────────────

    [Theory]
    [InlineData("g.V().drop()")]
    [InlineData("g.E().drop()")]
    [InlineData("g.V().hasLabel('person').drop()")]
    [InlineData("g.V().has('name','Alice').properties('age').remove()")]
    [InlineData("g.V().has('name','Bob'). drop ()")]
    public async Task ShouldBlockDestructiveTraversals(string traversal)
    {
        var request = new ToolCallRequest(
            ToolName: "janusgraph_query",
            Parameters: new Dictionary<string, object?>
            {
                ["gremlin_endpoint"] = "ws://localhost:8182/gremlin",
                ["traversal"] = traversal
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Destructive", result.Error);
    }

    // ── Safe traversal tests ──────────────────────────────────────────

    [Theory]
    [InlineData("g.V().limit(10)")]
    [InlineData("g.V().hasLabel('person').values('name')")]
    [InlineData("g.E().hasLabel('knows')")]
    [InlineData("g.V().count()")]
    [InlineData("g.V().has('name','Alice').out('knows')")]
    public async Task ShouldAllowSafeTraversals(string traversal)
    {
        SetupEmptyResult();

        var request = new ToolCallRequest(
            ToolName: "janusgraph_query",
            Parameters: new Dictionary<string, object?>
            {
                ["gremlin_endpoint"] = "ws://localhost:8182/gremlin",
                ["traversal"] = traversal
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }

    // ── Result type detection tests ──────────────────────────────────

    [Fact]
    public void ShouldDetectVertexResultType()
    {
        var results = new List<Dictionary<string, object?>>
        {
            new() { ["id"] = 1, ["label"] = "person", ["properties"] = new Dictionary<string, object?>() }
        };

        var resultType = JanusGraphTool.DetectResultType(results);

        Assert.Equal("vertex", resultType);
    }

    [Fact]
    public void ShouldDetectEdgeResultType()
    {
        var results = new List<Dictionary<string, object?>>
        {
            new() { ["id"] = 1, ["label"] = "knows", ["inV"] = 2, ["outV"] = 3 }
        };

        var resultType = JanusGraphTool.DetectResultType(results);

        Assert.Equal("edge", resultType);
    }

    [Fact]
    public void ShouldDetectPathResultType()
    {
        var results = new List<Dictionary<string, object?>>
        {
            new() { ["labels"] = new List<object>(), ["objects"] = new List<object>() }
        };

        var resultType = JanusGraphTool.DetectResultType(results);

        Assert.Equal(ParamPath, resultType);
    }

    [Fact]
    public void ShouldDetectValueResultType()
    {
        var results = new List<Dictionary<string, object?>>
        {
            new() { ["count"] = 42 }
        };

        var resultType = JanusGraphTool.DetectResultType(results);

        Assert.Equal("value", resultType);
    }

    [Fact]
    public void ShouldReturnValueForEmptyResults()
    {
        var results = new List<Dictionary<string, object?>>();

        var resultType = JanusGraphTool.DetectResultType(results);

        Assert.Equal("value", resultType);
    }

    // ── Execution tests ──────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnResultsFromClient()
    {
        var expectedResults = new List<Dictionary<string, object?>>
        {
            new() { ["id"] = 1, ["label"] = "person", ["name"] = "Alice" },
            new() { ["id"] = 2, ["label"] = "person", ["name"] = "Bob" }
        };

        _client.SetDictResult(expectedResults);

        var request = new ToolCallRequest(
            ToolName: "janusgraph_query",
            Parameters: new Dictionary<string, object?>
            {
                ["gremlin_endpoint"] = "ws://localhost:8182/gremlin",
                ["traversal"] = "g.V().hasLabel('person')"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2, (int)dict["result_count"]!);
    }

    [Fact]
    public async Task ShouldRespectMaxResults()
    {
        var manyResults = Enumerable.Range(1, 100)
            .Select(i => new Dictionary<string, object?> { ["id"] = i })
            .ToList();

        _client.SetDictResult(manyResults);

        var request = new ToolCallRequest(
            ToolName: "janusgraph_query",
            Parameters: new Dictionary<string, object?>
            {
                ["gremlin_endpoint"] = "ws://localhost:8182/gremlin",
                ["traversal"] = "g.V()",
                ["max_results"] = 5
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(5, (int)dict["result_count"]!);
    }

    // ── Schema tests ──────────────────────────────────────────────

    [Fact]
    public void ShouldHaveCorrectToolMetadata()
    {
        Assert.Equal("janusgraph_query", _tool.Name);
        Assert.Equal("Data Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters["gremlin_endpoint"].Required);
        Assert.True(_tool.Schema.Parameters["traversal"].Required);
        Assert.False(_tool.Schema.Parameters["bindings"].Required);
        Assert.False(_tool.Schema.Parameters["max_results"].Required);
        Assert.False(_tool.Schema.Parameters["timeout_ms"].Required);
    }

    // ── Request message building tests ──────────────────────────────

    [Fact]
    public void ShouldBuildRequestMessageWithBindings()
    {
        var request = new JanusGraphRequest
        {
            GremlinEndpoint = "ws://localhost:8182/gremlin",
            Traversal = "g.V().has('name', name)",
            Bindings = new Dictionary<string, object> { ["name"] = "Alice" },
            TimeoutMs = 5000
        };

        var message = JanusGraphTool.BuildRequestMessage(request);

        Assert.Equal(Tokens.OpsEval, message.Operation);
        Assert.Equal("g.V().has('name', name)", message.Arguments[Tokens.ArgsGremlin]);
        Assert.Equal(5000L, message.Arguments[Tokens.ArgsEvalTimeout]);
        Assert.NotNull(message.Arguments[Tokens.ArgsBindings]);
    }

    [Fact]
    public void ShouldBuildRequestMessageWithoutBindings()
    {
        var request = new JanusGraphRequest
        {
            GremlinEndpoint = "ws://localhost:8182/gremlin",
            Traversal = "g.V().count()",
            TimeoutMs = 30000
        };

        var message = JanusGraphTool.BuildRequestMessage(request);

        Assert.Equal(Tokens.OpsEval, message.Operation);
        Assert.Equal("g.V().count()", message.Arguments[Tokens.ArgsGremlin]);
        Assert.False(message.Arguments.ContainsKey(Tokens.ArgsBindings));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private void SetupEmptyResult() => _client.SetDictResult(new List<Dictionary<string, object?>>());

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _tool.Dispose();
        _client.Dispose();
    }

    /// <summary>
    /// Hand-rolled <see cref="IGremlinClient"/> stub that returns a configurable
    /// dictionary <see cref="ResultSet{T}"/> on every <see cref="SubmitAsync{T}(RequestMessage, CancellationToken)"/>
    /// call. Replaces the previous Moq usage.
    /// </summary>
    public sealed class StubGremlinClient : IGremlinClient
    {
        private List<Dictionary<string, object?>> _dictResult = new();

        public void SetDictResult(List<Dictionary<string, object?>> result) => _dictResult = result;

        public Task<ResultSet<T>> SubmitAsync<T>(RequestMessage requestMessage, CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(Dictionary<string, object?>))
            {
                var rs = new ResultSet<Dictionary<string, object?>>(_dictResult, new Dictionary<string, object>());
                return Task.FromResult((ResultSet<T>)(object)rs);
            }
            var empty = new ResultSet<T>(new List<T>(), new Dictionary<string, object>());
            return Task.FromResult(empty);
        }

        public static ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
        public static int NrConnections => 0;
    }
}
