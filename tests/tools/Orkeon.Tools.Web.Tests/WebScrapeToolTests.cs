using Orkeon.Domain.Tools.Protocol;
using System.Net;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

namespace Orkeon.Tools.Web.Tests;

public sealed class WebScrapeToolTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly WebScrapeTool _tool;

    public WebScrapeToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        _tool = new WebScrapeTool(_httpClient);
    }

    [Fact]
    public async Task ShouldExtractText_WhenHtmlIsValid()
    {
        var html = "<html><head><title>Test Page</title></head><body><p>Hello World</p></body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "web_scrape",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Contains("Hello World", dict["content"]!.ToString());
        Assert.Equal("Test Page", dict["title"]);
    }

    [Fact]
    public async Task ShouldFilterContent_WhenSelectorIsSpecified()
    {
        var html = "<html><body><div id='main'><p>Target content</p></div><div id='sidebar'>Ignore this</div></body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "web_scrape",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl,
                ["selector"] = "//div[@id='main']//p"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Contains("Target content", dict["content"]!.ToString());
        Assert.DoesNotContain("Ignore this", dict["content"]!.ToString()!);
    }

    [Fact]
    public async Task ShouldReturnError_WhenUrlIsInvalid()
    {
        var request = new ToolCallRequest(
            ToolName: "web_scrape",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = "not-a-valid-url"
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
            ToolName: "web_scrape",
            Parameters: []
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(ParamUrl, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldRemoveScriptAndStyleTags_WhenScrapingHtml()
    {
        var html = "<html><body><script>alert('xss')</script><style>.red{}</style><p>Clean text</p></body></html>";
        SetupHttpResponse(html);

        var request = new ToolCallRequest(
            ToolName: "web_scrape",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.DoesNotContain("alert", dict["content"]!.ToString()!);
        Assert.DoesNotContain(".red", dict["content"]!.ToString()!);
        Assert.Contains("Clean text", dict["content"]!.ToString());
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("web_scrape", _tool.Name);
        Assert.Equal("Web Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters[ParamUrl].Required);
        Assert.False(_tool.Schema.Parameters["selector"].Required);
    }

    [Fact]
    public async Task ShouldCancelSlowRequest_WhenCallerCancellationTriggers()
    {
        // Defense-in-depth: WebScrapeTool wraps the caller's CancellationToken in
        // a linked token that is cancelled after HttpDefaults.ScrapeTimeoutSeconds.
        // Here we cancel via the caller token quickly to assert that a hanging
        // server (TaskCompletionSource never completes) is interrupted promptly.
        using var slowHandler = new SlowHttpMessageHandler();
        using var slowClient = new HttpClient(slowHandler);
        using var slowTool = new WebScrapeTool(slowClient);

        var request = new ToolCallRequest(
            ToolName: "web_scrape",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = TestBaseUrl
            }
        );

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        // The tool should propagate the cancellation (either by returning a
        // failed result or throwing TaskCanceledException). Either way, we
        // must not hang forever.
        var call = slowTool.CallAsync(request, cts.Token);

        // Guard the test itself with a hard timeout in case the cancellation
        // is not honoured — this catches regressions immediately.
        // WaitAsync rather than WhenAny + Delay (CA2027): a timeout here is the regression.
        await call.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        try
        {
            var result = await call;
            // If the tool swallowed the cancellation into a failed ToolCallResult,
            // check that it is indeed a failure (cancellation-related).
            Assert.False(result.Success, "Slow handler must not produce a success result.");
        }
        catch (OperationCanceledException)
        {
            // Acceptable: cancellation propagated out as an exception.
        }
    }

    /// <summary>
    /// HTTP handler that never completes unless cancelled. Used to verify
    /// that WebScrapeTool honours its timeout/cancellation contract.
    /// </summary>
    private sealed class SlowHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<HttpResponseMessage>();
            using var registration = cancellationToken.Register(
                () => tcs.TrySetCanceled(cancellationToken));
            return await tcs.Task.ConfigureAwait(false);
        }
    }

    private void SetupHttpResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _mockHandler.SetResponse(statusCode, content);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }
}
