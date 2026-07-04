using Orkeon.Domain.Memory;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Web.Tests;

public sealed class CacheSearchToolTests : IDisposable
{
    private readonly FakeEmbeddingService _embedding = new();
    private readonly FakeMemoryProvider _memory = new();
    private readonly CacheSearchTool _tool;

    public CacheSearchToolTests()
    {
        _tool = new CacheSearchTool(_embedding, _memory);
    }

    public void Dispose()
    {
        _tool.Dispose();
    }

    private static MemoryItem MakeItem(
        string content,
        string source = "https://example.com/a",
        string[]? tags = null,
        Dictionary<string, string>? props = null)
    {
        return MemoryItem.Create(
            content: content,
            source: source,
            tags: tags ?? ["web_scrape"],
            customProperties: props ?? new Dictionary<string, string>
            {
                ["url"] = source,
                ["title"] = "Example Title"
            });
    }

    // ── Constructor guards ────────────────────────────────────────────

    [Fact]
    public void Constructor_NullEmbedding_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CacheSearchTool(null!, _memory));
    }

    [Fact]
    public void Constructor_NullMemory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CacheSearchTool(_embedding, null!));
    }

    [Fact]
    public void Schema_HasExpectedNameAndCategory()
    {
        Assert.Equal("cache_search", _tool.Name);
        Assert.Equal("Search", _tool.Category);
    }

    // ── Validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task CallAsync_EmptyQuery_ReturnsValidationError()
    {
        var request = new ToolCallRequest("cache_search", new Dictionary<string, object?>
        {
            [ParamQuery] = ""
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Query", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallAsync_TopKAboveMax_ReturnsValidationError()
    {
        var request = new ToolCallRequest("cache_search", new Dictionary<string, object?>
        {
            [ParamQuery] = "vaccines",
            ["top_k"] = 999
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("TopK", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallAsync_TopKZero_ReturnsValidationError()
    {
        var request = new ToolCallRequest("cache_search", new Dictionary<string, object?>
        {
            [ParamQuery] = "vaccines",
            ["top_k"] = 0
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("TopK", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallAsync_MinScoreOutOfRange_ReturnsValidationError()
    {
        var request = new ToolCallRequest("cache_search", new Dictionary<string, object?>
        {
            [ParamQuery] = "vaccines",
            ["min_score"] = 1.5
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("MinScore", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task CallAsync_NoFilters_ReturnsHitsAndEmbedsQuery()
    {
        _memory.SearchResults.Add(new ScoredMemoryItem(
            MakeItem("Chunk one content"), 0.91f));
        _memory.SearchResults.Add(new ScoredMemoryItem(
            MakeItem("Chunk two content", source: "https://example.com/b"), 0.80f));

        var request = new ToolCallRequest("cache_search", new Dictionary<string, object?>
        {
            [ParamQuery] = "MMR vaccine autism"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2, Convert.ToInt32(dict!["hit_count"]));
        Assert.Equal(1, _embedding.CallCount);
        Assert.Equal("MMR vaccine autism", _embedding.LastText);
        // Without filters, rawTopK == TopK (default 5).
        Assert.Equal(5, _memory.LastRequestedTopK);
    }

    [Fact]
    public async Task CallAsync_RespectsTopKLimit_WhenMoreHitsAvailable()
    {
        for (var i = 0; i < 5; i++)
        {
            _memory.SearchResults.Add(new ScoredMemoryItem(
                MakeItem($"Chunk {i}"), 0.9f - i * 0.01f));
        }

        var request = new ToolCallRequest("cache_search", new Dictionary<string, object?>
        {
            [ParamQuery] = "query",
            ["top_k"] = 2
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.Equal(2, Convert.ToInt32(dict!["hit_count"]));
    }

    // ── Source filter ──────────────────────────────────────────────────

    [Fact]
    public async Task CallAsync_SourceFilter_ExcludesNonMatchingTags()
    {
        _memory.SearchResults.Add(new ScoredMemoryItem(
            MakeItem("web chunk", tags: ["web_scrape"]), 0.9f));
        _memory.SearchResults.Add(new ScoredMemoryItem(
            MakeItem("pdf chunk", tags: ["pdf"]), 0.85f));

        var request = new ToolCallRequest("cache_search", new Dictionary<string, object?>
        {
            [ParamQuery] = "query",
            ["source"] = "web_scrape"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.Equal(1, Convert.ToInt32(dict!["hit_count"]));
        // With filters, the tool over-fetches: rawTopK = TopK * 4 = 20.
        Assert.Equal(20, _memory.LastRequestedTopK);
    }

    [Fact]
    public async Task CallAsync_SourceFilter_ExcludesItemsWithNoTags()
    {
        _memory.SearchResults.Add(new ScoredMemoryItem(
            MakeItem("no-tag chunk", tags: Array.Empty<string>()), 0.9f));

        var request = new ToolCallRequest("cache_search", new Dictionary<string, object?>
        {
            [ParamQuery] = "query",
            ["source"] = "web_scrape"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.Equal(0, Convert.ToInt32(dict!["hit_count"]));
    }

    // ── URL filter ─────────────────────────────────────────────────────

    [Fact]
    public async Task CallAsync_UrlFilter_KeepsMatchingUrlOnly()
    {
        _memory.SearchResults.Add(new ScoredMemoryItem(
            MakeItem("lancet chunk", source: "https://thelancet.com/article"), 0.9f));
        _memory.SearchResults.Add(new ScoredMemoryItem(
            MakeItem("other chunk", source: "https://example.com/other"), 0.85f));

        var request = new ToolCallRequest("cache_search", new Dictionary<string, object?>
        {
            [ParamQuery] = "query",
            ["url_filter"] = "thelancet.com"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.Equal(1, Convert.ToInt32(dict!["hit_count"]));
    }

    [Fact]
    public async Task CallAsync_FallsBackToItemSource_WhenUrlPropMissing()
    {
        // No "url" custom property → TryBuildHit uses item.Source for URL filtering.
        var item = MemoryItem.Create(
            content: "chunk without url prop",
            source: "https://fallback.example/page",
            tags: ["web_scrape"],
            customProperties: new Dictionary<string, string> { ["title"] = "T" });

        _memory.SearchResults.Add(new ScoredMemoryItem(item, 0.77f));

        var request = new ToolCallRequest("cache_search", new Dictionary<string, object?>
        {
            [ParamQuery] = "query",
            ["url_filter"] = "fallback.example"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.Equal(1, Convert.ToInt32(dict!["hit_count"]));
    }

    [Fact]
    public async Task CallAsync_NoMatches_ReturnsZeroHits()
    {
        _memory.SearchResults.Add(new ScoredMemoryItem(
            MakeItem("chunk", source: "https://example.com/x"), 0.9f));

        var request = new ToolCallRequest("cache_search", new Dictionary<string, object?>
        {
            [ParamQuery] = "query",
            ["url_filter"] = "does-not-exist.invalid"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.Equal(0, Convert.ToInt32(dict!["hit_count"]));
    }
}
