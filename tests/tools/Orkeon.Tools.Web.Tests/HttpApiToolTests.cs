using Orkeon.Domain.Tools.Protocol;
using System.Net;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Web.Tests;

public sealed class HttpApiToolTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly HttpApiTool _tool;

    public HttpApiToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        _tool = new HttpApiTool(
            new AllowAllUrlValidator(),
            new Orkeon.Tools.Abstractions.Security.HttpHeaderSanitizer(),
            _httpClient);
    }

    [Fact]
    public async Task ShouldReturnResponse_WhenSendingGetRequest()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{\"status\":\"ok\"}");

        var request = new ToolCallRequest(
            ToolName: "http_api",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = "https://api.example.com/health"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(200, (int)dict["status_code"]!);
        Assert.Contains("ok", dict["body"]!.ToString());
        Assert.True((bool)dict["is_success"]!);
    }

    [Fact]
    public async Task ShouldSendBody_WhenSendingPostRequest()
    {
        _mockHandler.SetResponse(HttpStatusCode.Created, "{\"id\":1}");

        var request = new ToolCallRequest(
            ToolName: "http_api",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = "https://api.example.com/items",
                ["method"] = "POST",
                ["body"] = "{\"name\":\"test\"}"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(201, (int)dict["status_code"]!);

        Assert.Equal(1, _mockHandler.SendCallCount);
        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Equal(HttpMethod.Post, _mockHandler.LastRequest!.Method);
    }

    [Fact]
    public async Task ShouldReturnError_WhenUrlIsInvalid()
    {
        var request = new ToolCallRequest(
            ToolName: "http_api",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = "not-a-url"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Invalid URL", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenUrlIsMissing()
    {
        var request = new ToolCallRequest(
            ToolName: "http_api",
            Parameters: []
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(ParamUrl, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnSuccessfully_WhenServerReturnsErrorStatusCode()
    {
        _mockHandler.SetResponse(HttpStatusCode.NotFound, "{\"error\":\"not found\"}");

        var request = new ToolCallRequest(
            ToolName: "http_api",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = "https://api.example.com/missing"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // The tool itself succeeds (the HTTP call was made), even though the server returned 404
        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(404, (int)dict["status_code"]!);
        Assert.False((bool)dict["is_success"]!);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("http_api", _tool.Name);
        Assert.Equal("Web Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters[ParamUrl].Required);
        Assert.False(_tool.Schema.Parameters["method"].Required);
        Assert.False(_tool.Schema.Parameters["body"].Required);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }
}
