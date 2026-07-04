using System.Net;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

namespace Orkeon.Tools.Web.Tests;

/// <summary>
/// Covers the RAG-cache, dedup and Wikipedia-REST code paths of WebScrapeTool that
/// are not exercised by the bare (non-cached) tests.
/// </summary>
public sealed class WebScrapeToolCacheTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler = new();
    private readonly HttpClient _httpClient;
    private readonly FakeEmbeddingService _embedding = new();
    private readonly FakeMemoryProvider _memory = new();
    private readonly WebScrapeTool _tool;

    public WebScrapeToolCacheTests()
    {
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        _tool = new WebScrapeTool(_embedding, _memory, _httpClient);
    }

    [Fact]
    public void Constructor_NullEmbedding_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WebScrapeTool(null!, _memory, _httpClient));
    }

    [Fact]
    public void Constructor_NullMemory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WebScrapeTool(_embedding, null!, _httpClient));
    }

    [Fact]
    public async Task CallAsync_Cached_ChunksEmbedsAndStores()
    {
        // Long body across multiple sentences so the chunker yields several chunks.
        var sentence = "This is a sentence about web scraping and caching content. ";
        var body = string.Concat(Enumerable.Repeat(sentence, 200)); // ~11.8k chars
        var html = $"<html><head><title>Big Page</title></head><body><p>{body}</p></body></html>";
        _mockHandler.SetResponse(HttpStatusCode.OK, html);

        var request = new ToolCallRequest("web_scrape", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://example.com/big",
            ["cached"] = true,
            ["chunk_size"] = 500
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.True((bool)dict!["cached"]!);
        var totalChunks = Convert.ToInt32(dict["total_chunks"]);
        Assert.True(totalChunks >= 1);
        // Content dropped when cached.
        Assert.Equal("", dict["content"]!.ToString());
        Assert.False(string.IsNullOrEmpty(dict["summary"]!.ToString()));
        Assert.StartsWith("web:", dict["key_prefix"]!.ToString());
        // One Store + one embedding call per chunk.
        Assert.Equal(totalChunks, _memory.StoreCallCount);
        Assert.Equal(totalChunks, _embedding.CallCount);
    }

    [Fact]
    public async Task CallAsync_Cached_EmptyPage_ReturnsEmptyPageSummary()
    {
        var html = "<html><head><title>Empty</title></head><body></body></html>";
        _mockHandler.SetResponse(HttpStatusCode.OK, html);

        var request = new ToolCallRequest("web_scrape", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://example.com/empty",
            ["cached"] = true
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.True((bool)dict!["cached"]!);
        Assert.Equal("(empty page)", dict["summary"]!.ToString());
        Assert.Equal(0, _memory.StoreCallCount);
    }

    [Fact]
    public async Task CallAsync_Cached_DedupHit_SkipsHttpFetch()
    {
        const string url = "https://example.com/dedup";
        // Compute the same key prefix the tool computes (web:<sha256[0..6]>).
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url));
        var prefix = $"web:{Convert.ToHexString(bytes, 0, 6).ToLowerInvariant()}";

        var cachedItem = MemoryItem.Create(
            content: "Previously cached chunk content",
            source: url,
            tags: ["web_scrape"],
            customProperties: new Dictionary<string, string>
            {
                ["url"] = url,
                ["title"] = "Cached Title",
                ["total_chunks"] = "3"
            });
        _memory.Preload($"{prefix}:0000", cachedItem);

        var request = new ToolCallRequest("web_scrape", new Dictionary<string, object?>
        {
            [ParamUrl] = url,
            ["cached"] = true
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.True((bool)dict!["cached"]!);
        Assert.Equal("Cached Title", dict["title"]!.ToString());
        Assert.Equal(3, Convert.ToInt32(dict["total_chunks"]));
        // No HTTP call because the dedup short-circuit hit.
        Assert.Equal(0, _mockHandler.SendCallCount);
    }

    [Fact]
    public async Task CallAsync_Cached_InvalidChunkSize_ReturnsValidationError()
    {
        var request = new ToolCallRequest("web_scrape", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://example.com/x",
            ["cached"] = true,
            ["chunk_size"] = 50
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("ChunkSize", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallAsync_WikipediaUrl_UsesRestApiExtract()
    {
        var wikiJson = """{"title":"Albert Einstein","extract":"Einstein was a physicist."}""";
        _mockHandler.SetResponseFactory(req =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(wikiJson) });

        var request = new ToolCallRequest("web_scrape", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://en.wikipedia.org/wiki/Albert_Einstein"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.Equal("Albert Einstein", dict!["title"]!.ToString());
        Assert.Contains("physicist", dict["content"]!.ToString());
        // Verify the REST API endpoint was used.
        Assert.Contains("/api/rest_v1/page/summary/", _mockHandler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CallAsync_SelectorNoMatch_ReturnsEmptyContentNotMatched()
    {
        var html = "<html><head><title>T</title></head><body><p>data</p></body></html>";
        _mockHandler.SetResponse(HttpStatusCode.OK, html);

        var request = new ToolCallRequest("web_scrape", new Dictionary<string, object?>
        {
            [ParamUrl] = TestBaseUrl,
            ["selector"] = "//div[@id='not-there']"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.Equal("", dict!["content"]!.ToString());
        Assert.False((bool)dict["selector_matched"]!);
    }

    [Fact]
    public async Task CallAsync_HttpError_RethrowsWithUrlContext()
    {
        using var throwing = new ThrowingHandler(new HttpRequestException("boom", null, HttpStatusCode.NotFound));
        using var client = new HttpClient(throwing);
        using var tool = new WebScrapeTool(client);

        var request = new ToolCallRequest("web_scrape", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://example.com/missing-page"
        });

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("missing-page", result.Error);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public ThrowingHandler(Exception ex) => _ex = ex;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _ex;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }
}
