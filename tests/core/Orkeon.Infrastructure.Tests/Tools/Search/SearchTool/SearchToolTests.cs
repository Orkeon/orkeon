using Orkeon.Domain.Memory;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tools.Search;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Infrastructure.Tests.Tools.Search;

public sealed class SearchToolTests : IDisposable
{
    private static readonly float[] s_defaultEmbedding = [0.1f, 0.2f, 0.3f];
    private static readonly string[] s_aiTags = ["ai"];
    private static readonly string[] s_mlTags = ["ml"];

    private readonly MockEmbeddingService _mockEmbeddingService;
    private readonly MockVectorMemoryStore _mockVectorStore;
    private readonly SearchTool _tool;

    public SearchToolTests()
    {
        _mockEmbeddingService = new MockEmbeddingService();
        _mockVectorStore = new MockVectorMemoryStore();

        // Default setup: return a simple embedding
        _mockEmbeddingService.SetEmbeddingResult(s_defaultEmbedding);

        // Default setup: return empty results
        _mockVectorStore.SetSearchSimilarResult([]);

        _tool = new SearchTool(
            _mockEmbeddingService,
            _mockVectorStore
        );
    }

    [Fact]
    public async Task ShouldReturnResults_WhenCallAsyncWithValidQuery()
    {
        var memoryItems = new List<MemoryItem>
        {
            MemoryItem.Create("Result about AI", importance: 0.9f, source: "docs", tags: s_aiTags),
            MemoryItem.Create("Result about ML", importance: 0.8f, source: "docs", tags: s_mlTags)
        };

        _mockVectorStore.SetSearchSimilarResult(memoryItems);

        var request = new ToolCallRequest(
            ToolName: "semantic_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "Tell me about AI"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2, (int)dict["result_count"]!);
    }

    [Fact]
    public async Task ShouldPassCorrectLimit_WhenCallAsyncWithCustomTopK()
    {
        var request = new ToolCallRequest(
            ToolName: "semantic_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "search query",
                ["top_k"] = 10
            }
        );

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(1, _mockVectorStore.SearchSimilarCallCount);
        Assert.Equal(10, _mockVectorStore.LastSearchLimit);
    }

    [Fact]
    public async Task ShouldReturnError_WhenCallAsyncWithMissingQuery()
    {
        var request = new ToolCallRequest(
            ToolName: "semantic_search",
            Parameters: []
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(ParamQuery, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenCallAsyncWhenEmbeddingServiceThrows()
    {
        _mockEmbeddingService.SetExceptionToThrow(new InvalidOperationException("Service unavailable"));

        var request = new ToolCallRequest(
            ToolName: "semantic_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test query"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Service unavailable", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenCallAsyncWithEmptyQuery()
    {
        var request = new ToolCallRequest(
            ToolName: "semantic_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = ""
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenSchema()
    {
        Assert.Equal("semantic_search", _tool.Name);
        Assert.Equal("Search", _tool.Category);
        Assert.True(_tool.Schema.Parameters[ParamQuery].Required);
        Assert.False(_tool.Schema.Parameters["top_k"].Required);
        Assert.False(_tool.Schema.Parameters["threshold"].Required);
    }

    [Fact]
    public void ShouldThrowArgumentNull_WhenConstructorWithNullEmbeddingService()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SearchTool(null!, _mockVectorStore));
    }

    [Fact]
    public void ShouldThrowArgumentNull_WhenConstructorWithNullVectorStore()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SearchTool(_mockEmbeddingService, null!));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _tool.Dispose();
    }
}
