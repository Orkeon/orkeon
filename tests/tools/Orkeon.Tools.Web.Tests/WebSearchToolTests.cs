using System.Net;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Web.Tests;

public sealed class WebSearchToolTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly MockSecretProvider _secretProvider;
    private readonly WebSearchTool _tool;

    private const string ValidApiKey = "tvly-test-api-key-12345";

    private const string TavilySuccessResponse = """
        {
            "results": [
                {
                    "title": "Introduction to AI",
                    "url": "https://example.com/ai-intro",
                    "content": "Artificial intelligence is a branch of computer science...",
                    "score": 0.95
                },
                {
                    "title": "Machine Learning Basics",
                    "url": "https://example.com/ml-basics",
                    "content": "Machine learning is a subset of artificial intelligence...",
                    "score": 0.87
                }
            ]
        }
        """;

    public WebSearchToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        _secretProvider = new MockSecretProvider();
        _secretProvider.AddSecret(WebSearchTool.TavilyApiKeySecretName, ValidApiKey);
        _tool = new WebSearchTool(_secretProvider, _httpClient);
    }

    [Fact]
    public async Task ShouldReturnResults_WhenSearchIsSuccessful()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, TavilySuccessResponse);

        var request = new ToolCallRequest(
            ToolName: "web_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "artificial intelligence"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("artificial intelligence", dict[ParamQuery]?.ToString());
        Assert.Equal(2, (int)dict["result_count"]!);
    }

    [Fact]
    public async Task ShouldReturnError_WhenQueryIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "web_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = ""
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Query cannot be empty", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenQueryIsMissing()
    {
        var request = new ToolCallRequest(
            ToolName: "web_search",
            Parameters: []
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(ParamQuery, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenApiReturns401()
    {
        _mockHandler.SetResponse(HttpStatusCode.Unauthorized, """{"error":"Invalid API key"}""");

        var request = new ToolCallRequest(
            ToolName: "web_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test query"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("401", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenApiReturns500()
    {
        _mockHandler.SetResponse(HttpStatusCode.InternalServerError, """{"error":"Internal server error"}""");

        var request = new ToolCallRequest(
            ToolName: "web_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test query"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("500", result.Error);
    }

    [Fact]
    public async Task ShouldPassMaxResults_WhenParameterIsProvided()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, TavilySuccessResponse);

        var request = new ToolCallRequest(
            ToolName: "web_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test query",
                ["max_results"] = 10
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(1, _mockHandler.SendCallCount);
        Assert.NotNull(_mockHandler.LastRequest);

        // Verify the request body contains max_results = 10
        var body = await _mockHandler.LastRequest!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"max_results\":10", body);
    }

    [Fact]
    public async Task ShouldPassSearchDepth_WhenParameterIsProvided()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, TavilySuccessResponse);

        var request = new ToolCallRequest(
            ToolName: "web_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test query",
                ["search_depth"] = "advanced"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(_mockHandler.LastRequest);

        var body = await _mockHandler.LastRequest!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"search_depth\":\"advanced\"", body);
    }

    [Fact]
    public async Task ShouldPostToTavilyEndpoint()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, TavilySuccessResponse);

        var request = new ToolCallRequest(
            ToolName: "web_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test query"
            }
        );

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(1, _mockHandler.SendCallCount);
        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Equal(HttpMethod.Post, _mockHandler.LastRequest!.Method);
        Assert.Equal("https://api.tavily.com/search", _mockHandler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task ShouldIncludeApiKeyInRequestBody()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, TavilySuccessResponse);

        var request = new ToolCallRequest(
            ToolName: "web_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test query"
            }
        );

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(_mockHandler.LastRequest);
        var body = await _mockHandler.LastRequest!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(ValidApiKey, body);
    }

    [Fact]
    public async Task ShouldReturnEmptyResults_WhenTavilyReturnsEmptyArray()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, """{"results":[]}""");

        var request = new ToolCallRequest(
            ToolName: "web_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "obscure query with no results"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(0, (int)dict["result_count"]!);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("web_search", _tool.Name);
        Assert.Equal("Web Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters[ParamQuery].Required);
        Assert.False(_tool.Schema.Parameters["max_results"].Required);
        Assert.False(_tool.Schema.Parameters["search_depth"].Required);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenSecretProviderIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new WebSearchTool(null!, _httpClient));
    }

    [Fact]
    public async Task ShouldReturnError_WhenApiKeyIsNotConfigured()
    {
        var emptyProvider = new MockSecretProvider(); // no secrets registered
        using var tool = new WebSearchTool(emptyProvider, _httpClient);

        var request = new ToolCallRequest(
            ToolName: "web_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test query"
            }
        );

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("TAVILY_API_KEY", result.Error);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }
}
