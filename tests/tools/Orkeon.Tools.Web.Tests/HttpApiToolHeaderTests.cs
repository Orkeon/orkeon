using System.Net;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Web.Tests;

/// <summary>
/// Exercises header parsing, response header projection, body/content-type handling
/// and HTTP method branches of HttpApiTool not covered by HttpApiToolTests.
/// </summary>
public sealed class HttpApiToolHeaderTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler = new();
    private readonly HttpClient _httpClient;
    private readonly HttpApiTool _tool;

    public HttpApiToolHeaderTests()
    {
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        _tool = new HttpApiTool(
            new AllowAllUrlValidator(),
            new Orkeon.Tools.Abstractions.Security.HttpHeaderSanitizer(),
            _httpClient);
    }

    [Fact]
    public async Task CallAsync_HeadersAsObject_AreForwarded()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "ok");

        var request = new ToolCallRequest("http_api", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://api.example.com/x",
            ["headers"] = new Dictionary<string, object?>
            {
                ["X-Custom"] = "abc",
                ["X-Token"] = "t1"
            }
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(_mockHandler.LastRequest);
        Assert.True(_mockHandler.LastRequest!.Headers.Contains("X-Custom"));
        Assert.Equal("abc", _mockHandler.LastRequest.Headers.GetValues("X-Custom").Single());
    }

    [Fact]
    public async Task CallAsync_NoHeaders_StillSucceeds()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "ok");

        var request = new ToolCallRequest("http_api", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://api.example.com/x"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task CallAsync_ProjectsResponseHeaders()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("body-data", System.Text.Encoding.UTF8, "text/plain")
        };
        response.Headers.Add("X-Response-Id", "r-123");
        _mockHandler.SetResponse(response);

        var request = new ToolCallRequest("http_api", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://api.example.com/headers"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var headers = dict!["headers"] as Dictionary<string, object?>;
        Assert.NotNull(headers);
        Assert.True(headers!.ContainsKey("X-Response-Id"));
        // Content header projected too.
        Assert.True(headers.ContainsKey("Content-Type"));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task CallAsync_SupportedMethods_SendCorrectVerb(string method)
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        var request = new ToolCallRequest("http_api", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://api.example.com/resource",
            ["method"] = method,
            ["body"] = "{\"k\":\"v\"}",
            ["content_type"] = "application/json"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(method, _mockHandler.LastRequest!.Method.Method);
    }

    [Fact]
    public async Task CallAsync_UnsupportedMethod_ReturnsValidationError()
    {
        var request = new ToolCallRequest("http_api", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://api.example.com/resource",
            ["method"] = "TRACE"
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("method", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }
}
