using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-10: an SSE-framed answer hands the notifications ahead of its final message to Notifications.</summary>
public sealed class SseNotificationTests
{
    private static readonly Uri Endpoint = new("https://mcp.example.test/rpc");

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The handler is owned by the HttpClient, which the caller disposes.")]
    private static (SseMcpTransport Transport, HttpClient Http) Create(string sseBody)
    {
        var handler = new MockHttpMessageHandler();
        handler.SetResponse(HttpStatusCode.OK, sseBody, "text/event-stream");
        var http = new HttpClient(handler);
        return (new SseMcpTransport(Endpoint, http), http);
    }

    private static JsonRpcRequest Request() =>
        new() { Id = JsonSerializer.SerializeToElement(1), Method = "tools/list" };

    [Fact]
    public async Task ShouldRouteTheNotificationsOfTheStream_AndReturnTheFinalMessage()
    {
        var (transport, http) = Create(
            "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n" +
            "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[]}}\n\n");
        using var _ = http;
        await using var __ = transport;
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var response = await transport.SendRequestAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Null(response.Error);
        Assert.Equal(1, response.Id!.Value.GetInt32());
        Assert.True(transport.Notifications.TryRead(out var notification));
        Assert.Equal("notifications/tools/list_changed", notification.Method);
    }

    [Fact]
    public async Task ShouldFailClearly_WhenTheStreamCarriesNotificationsAndNoAnswer()
    {
        var (transport, http) = Create("data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n");
        using var _ = http;
        await using var __ = transport;
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.SendRequestAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Contains("notifications", ex.Message, StringComparison.Ordinal);
        Assert.Contains("tools/list", ex.Message, StringComparison.Ordinal);
    }
}
