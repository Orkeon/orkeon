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
    public async Task CallAsync_ForbiddenHostAndCookieHeaders_AreDropped()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "ok");

        var request = new ToolCallRequest("http_api", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://api.example.com/x",
            ["headers"] = new Dictionary<string, object?>
            {
                ["Host"] = "internal.corp",
                ["Cookie"] = "sid=1",
                ["X-Custom"] = "ok"
            }
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Null(_mockHandler.LastRequest!.Headers.Host);
        Assert.False(_mockHandler.LastRequest.Headers.Contains("Host"));
        Assert.False(_mockHandler.LastRequest.Headers.Contains("Cookie"));
        Assert.Equal("ok", _mockHandler.LastRequest.Headers.GetValues("X-Custom").Single());
    }

    [Fact]
    public async Task CallAsync_CrlfInjectedHeader_IsDropped()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "ok");

        var request = new ToolCallRequest("http_api", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://api.example.com/x",
            ["headers"] = new Dictionary<string, object?>
            {
                ["X-Injected"] = "a\r\nEvil: b",
                ["X-Custom"] = "ok"
            }
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(_mockHandler.LastRequest);
        Assert.False(_mockHandler.LastRequest!.Headers.Contains("X-Injected"));
        Assert.False(_mockHandler.LastRequest.Headers.Contains("Evil"));
        Assert.Equal("ok", _mockHandler.LastRequest.Headers.GetValues("X-Custom").Single());
    }

    [Fact]
    public async Task CallAsync_DroppedHeaders_AreReportedAsHeaderWarnings()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "ok");

        var request = new ToolCallRequest("http_api", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://api.example.com/x",
            ["headers"] = new Dictionary<string, object?>
            {
                ["Cookie"] = "sid=1"
            }
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        var warnings = Assert.IsType<IEnumerable<object?>>(dict["header_warnings"], exactMatch: false);
        // The parameter pipeline lower-cases nested keys, and the sanitizer is
        // case-insensitive, so the warning names the header as it was received.
        Assert.Contains(warnings, w => w is string text && text.Contains("Cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CallAsync_CleanHeaders_ReportNoHeaderWarnings()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "ok");

        var request = new ToolCallRequest("http_api", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://api.example.com/x",
            ["headers"] = new Dictionary<string, object?>
            {
                ["X-Custom"] = "ok"
            }
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.False(dict.ContainsKey("header_warnings"));
    }

    [Fact]
    public async Task CallAsync_WithoutInjectedSanitizer_StillDropsForbiddenHeaders()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "ok");
        // The legacy constructor injects no sanitizer; the tool must fall back to a
        // default one instead of forwarding LLM-supplied headers raw. A public IP
        // literal keeps the fail-closed default URL guard away from DNS.
        using var tool = new HttpApiTool(_httpClient);

        var request = new ToolCallRequest("http_api", new Dictionary<string, object?>
        {
            [ParamUrl] = "https://93.184.216.34/x",
            ["headers"] = new Dictionary<string, object?>
            {
                ["Cookie"] = "sid=1",
                ["X-Custom"] = "ok"
            }
        });

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(_mockHandler.LastRequest);
        Assert.False(_mockHandler.LastRequest!.Headers.Contains("Cookie"));
        Assert.Equal("ok", _mockHandler.LastRequest.Headers.GetValues("X-Custom").Single());
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
