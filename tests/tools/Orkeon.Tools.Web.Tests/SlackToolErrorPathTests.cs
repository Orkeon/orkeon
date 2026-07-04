using System.Net;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests;

/// <summary>
/// Error/exception paths for SlackTool (send message) that are not covered by SlackToolTests.
/// </summary>
public class SlackToolErrorPathTests
{
    private static ToolCallRequest SendRequest(string channel = "#general", string message = "hi") =>
        new("slack_send_message", new Dictionary<string, object?>
        {
            ["channel"] = channel,
            ["message"] = message
        });

    [Fact]
    public void Constructor_NullToken_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SlackTool(null!));
    }

    [Fact]
    public async Task CallAsync_HttpRequestException_ReturnsFailure()
    {
        using var handler = new ThrowingHandler(new HttpRequestException("Connection refused"));
        using var client = new HttpClient(handler);
        using var tool = new SlackTool("xoxb-token", client);

        var result = await tool.CallAsync(SendRequest(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("HTTP request failed", result.Error);
    }

    [Fact]
    public async Task CallAsync_Timeout_ReturnsFailure()
    {
        using var handler = new ThrowingHandler(new TaskCanceledException("timed out"));
        using var client = new HttpClient(handler);
        using var tool = new SlackTool("xoxb-token", client);

        var result = await tool.CallAsync(SendRequest(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.True(
            result.Error!.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            result.Error.Contains("Unexpected error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CallAsync_UnexpectedException_ReturnsFailure()
    {
        using var handler = new ThrowingHandler(new InvalidOperationException("boom"));
        using var client = new HttpClient(handler);
        using var tool = new SlackTool("xoxb-token", client);

        var result = await tool.CallAsync(SendRequest(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Unexpected error", result.Error);
    }

    [Fact]
    public async Task CallAsync_ThreadTs_IncludedInPayload()
    {
        string? capturedBody = null;
        using var handler = new MockHttpMessageHandler();
        handler.SetResponseFactory(req =>
        {
            // Read the request body synchronously before it is disposed by the tool.
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok": true, "ts": "1.2"}""")
            };
        });
        using var client = new HttpClient(handler);
        using var tool = new SlackTool("xoxb-token", client);

        var request = new ToolCallRequest("slack_send_message", new Dictionary<string, object?>
        {
            ["channel"] = "#general",
            ["message"] = "hello",
            ["thread_ts"] = "1700000000.000100"
        });

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(capturedBody);
        Assert.Contains("thread_ts", capturedBody);
        Assert.Contains("1700000000.000100", capturedBody);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public ThrowingHandler(Exception ex) => _ex = ex;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _ex;
    }
}
