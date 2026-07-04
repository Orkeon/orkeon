using Orkeon.Domain.Tools.Protocol;
using System.Net;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Web.Tests;

public sealed class BraveSearchToolTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly BraveSearchTool _tool;

    private const string ValidApiKey = "test-brave-api-key";

    private const string BraveSuccessResponse = """
        {
            "query": { "original": "AI frameworks" },
            "web": {
                "results": [
                    {
                        "title": "Top AI Frameworks 2026",
                        "url": "https://example.com/ai-frameworks",
                        "description": "A comprehensive guide to AI frameworks in 2026.",
                        "extra_snippets": ["TensorFlow", "PyTorch"]
                    },
                    {
                        "title": "Getting Started with LLMs",
                        "url": "https://example.com/llms",
                        "description": "Learn how to use large language models effectively.",
                        "extra_snippets": []
                    }
                ]
            }
        }
        """;

    private const string BraveEmptyResponse = """
        {
            "query": { "original": "xyznonexistentquery" },
            "web": {
                "results": []
            }
        }
        """;

    public BraveSearchToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        _tool = new BraveSearchTool(ValidApiKey, _httpClient);
    }

    [Fact]
    public async Task ShouldReturnResults_WhenSearchIsSuccessful()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, BraveSuccessResponse);

        var request = new ToolCallRequest(
            ToolName: "brave_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "AI frameworks"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("AI frameworks", dict[ParamQuery]);

        var results = dict["results"] as List<object>;
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);

        var first = results[0] as Dictionary<string, object?>;
        Assert.NotNull(first);
        Assert.Equal("Top AI Frameworks 2026", first["title"]);
        Assert.Equal("https://example.com/ai-frameworks", first[ParamUrl]);
        Assert.Equal("A comprehensive guide to AI frameworks in 2026.", first["content"]);
        Assert.Equal(0.0, Convert.ToDouble(first["score"]));
    }

    [Fact]
    public async Task ShouldReturnError_WhenQueryIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "brave_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = ""
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Query", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenQueryIsMissing()
    {
        var request = new ToolCallRequest(
            ToolName: "brave_search",
            Parameters: []
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(ParamQuery, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldThrowException_WhenApiKeyIsNull()
    {
        Assert.Throws<ArgumentException>(() => new BraveSearchTool(null!));
    }

    [Fact]
    public void ShouldThrowException_WhenApiKeyIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new BraveSearchTool(""));
    }

    [Fact]
    public void ShouldThrowException_WhenApiKeyIsWhitespace()
    {
        Assert.Throws<ArgumentException>(() => new BraveSearchTool("   "));
    }

    [Fact]
    public async Task ShouldReturnError_WhenApiReturns401()
    {
        _mockHandler.SetResponse(HttpStatusCode.Unauthorized, """{"error":"Unauthorized"}""");

        var request = new ToolCallRequest(
            ToolName: "brave_search",
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
        _mockHandler.SetResponse(HttpStatusCode.InternalServerError, """{"error":"Internal Server Error"}""");

        var request = new ToolCallRequest(
            ToolName: "brave_search",
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
    public async Task ShouldPassMaxResults_AsQueryParameter()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, BraveSuccessResponse);

        var request = new ToolCallRequest(
            ToolName: "brave_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test",
                ["max_results"] = 10
            }
        );

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(_mockHandler.LastRequest);
        var requestUri = _mockHandler.LastRequest!.RequestUri!.ToString();
        Assert.Contains("count=10", requestUri);
    }

    [Fact]
    public async Task ShouldUseCorrectEndpointUrl()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, BraveSuccessResponse);

        var request = new ToolCallRequest(
            ToolName: "brave_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test"
            }
        );

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(_mockHandler.LastRequest);
        var requestUri = _mockHandler.LastRequest!.RequestUri!.ToString();
        Assert.StartsWith("https://api.search.brave.com/res/v1/web/search", requestUri);
    }

    [Fact]
    public async Task ShouldSendSubscriptionTokenHeader()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, BraveSuccessResponse);

        var request = new ToolCallRequest(
            ToolName: "brave_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test"
            }
        );

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(_mockHandler.LastRequest);
        Assert.True(_mockHandler.LastRequest!.Headers.Contains("X-Subscription-Token"));
        var tokenValues = _mockHandler.LastRequest.Headers.GetValues("X-Subscription-Token").ToList();
        Assert.Single(tokenValues);
        Assert.Equal(ValidApiKey, tokenValues[0]);
    }

    [Fact]
    public async Task ShouldSendAcceptJsonHeader()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, BraveSuccessResponse);

        var request = new ToolCallRequest(
            ToolName: "brave_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test"
            }
        );

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Contains(_mockHandler.LastRequest!.Headers.Accept, a => a.MediaType == "application/json");
    }

    [Fact]
    public async Task ShouldReturnEmptyResults_WhenBraveReturnsNoResults()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, BraveEmptyResponse);

        var request = new ToolCallRequest(
            ToolName: "brave_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "xyznonexistentquery"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(0, Convert.ToInt32(dict["result_count"]));

        var results = dict["results"] as List<object>;
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public void ShouldHaveCorrectSchemaProperties()
    {
        Assert.Equal("brave_search", _tool.Name);
        Assert.Equal("Web Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters[ParamQuery].Required);
        Assert.False(_tool.Schema.Parameters["max_results"].Required);
        Assert.False(_tool.Schema.Parameters["search_depth"].Required);
    }

    [Fact]
    public async Task ShouldEncodeQueryInUrl()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, BraveEmptyResponse);

        var request = new ToolCallRequest(
            ToolName: "brave_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "hello world & more"
            }
        );

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(_mockHandler.LastRequest);
        var requestUri = _mockHandler.LastRequest!.RequestUri!;
        // The query should contain the search term (URL-encoded or decoded by Uri)
        Assert.Contains("hello", requestUri.Query);
        // The & in the query value must be encoded so it is not treated as a parameter separator
        Assert.Contains("%26", requestUri.Query);
    }

    [Fact]
    public async Task ShouldUseDefaultMaxResults_WhenNotSpecified()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, BraveSuccessResponse);

        var request = new ToolCallRequest(
            ToolName: "brave_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test"
            }
        );

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(_mockHandler.LastRequest);
        var requestUri = _mockHandler.LastRequest!.RequestUri!.ToString();
        Assert.Contains("count=5", requestUri);
    }

    [Fact]
    public async Task ShouldHandleNullWebResults()
    {
        var responseWithNullWeb = """
            {
                "query": { "original": "test" }
            }
            """;
        _mockHandler.SetResponse(HttpStatusCode.OK, responseWithNullWeb);

        var request = new ToolCallRequest(
            ToolName: "brave_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(0, Convert.ToInt32(dict["result_count"]));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }
}
