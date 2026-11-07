using System.Text.Json;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// FWK-10: a frame without an <c>id</c> reaches <see cref="IMcpTransport.Notifications"/> instead of being
/// dropped; a request of the server is answered -32601. The echo process (<c>cat</c>) plays a server that
/// sends back every line it is given.
/// </summary>
public sealed class StdioNotificationDispatchTests
{
    private static async Task<JsonRpcNotification> ReadOneAsync(IMcpTransport transport)
    {
        // A hang guard far above what a loaded machine needs; the verdict is the assertion below.
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(HangGuard);
        try
        {
            return await transport.Notifications.ReadAsync(guard.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            Assert.Fail("No notification reached the channel.");
            throw;
        }
    }

    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(2);

    /// <summary>A token that ends a request the echo never answers: a hang guard, not a bound on the transport.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The source lives as long as the test; it fires at most once, after the guard.")]
    private static CancellationToken Guard()
    {
        var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(HangGuard);
        return guard.Token;
    }

    [Fact]
    public async Task ShouldDeliverAFrameWithoutId_ToTheNotificationChannel()
    {
        await using var transport = await StdioMcpTransportTestsFixture.CreateConnectedTransport();

        await transport.SendNotificationAsync(
            new JsonRpcNotification
            {
                Method = "notifications/tools/list_changed",
                Params = JsonSerializer.SerializeToElement(new { k = 1 }),
            },
            TestContext.Current.CancellationToken);

        var received = await ReadOneAsync(transport);
        Assert.Equal("notifications/tools/list_changed", received.Method);
        Assert.Equal(1, received.Params!.Value.GetProperty("k").GetInt32());
    }

    [Fact]
    public async Task ShouldAnswerMethodNotFound_ToARequestOfTheServer()
    {
        await using var transport = await StdioMcpTransportTestsFixture.CreateConnectedTransport();

        // The echo returns this request as a request of the server: the transport writes the -32601
        // answer, the echo returns it, and it completes this request.
        var response = await transport.SendRequestAsync(
            StdioMcpTransportTestsFixture.CreateRequest(method: "sampling/createMessage", id: 7),
            Guard());

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
        Assert.Equal(7, response.Id!.Value.GetInt32());
    }

    [Fact]
    public async Task ShouldAnswerMethodNotFound_ToARequestOfTheServerWithAStringId()
    {
        await using var transport = await StdioMcpTransportTestsFixture.CreateConnectedTransport();
        var request = new JsonRpcRequest
        {
            Method = "roots/list",
            Id = JsonSerializer.SerializeToElement("srv-1"),
        };

        var response = await transport.SendRequestAsync(request, Guard());

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
        Assert.Equal("srv-1", response.Id!.Value.GetString());
    }

    [Fact]
    public async Task ShouldKeepTheNewest256_WhenNobodyReadsTheChannel()
    {
        await using var transport = await StdioMcpTransportTestsFixture.CreateConnectedTransport();
        for (var i = 0; i < 300; i++)
        {
            await transport.SendNotificationAsync(
                new JsonRpcNotification { Method = $"n/{i}" }, TestContext.Current.CancellationToken);
        }

        // The echo is ordered: once this one is answered, the 300 notifications before it were dispatched.
        await transport.SendRequestAsync(
            StdioMcpTransportTestsFixture.CreateRequest(method: "barrier", id: 9),
            Guard());

        var methods = new List<string>();
        while (transport.Notifications.TryRead(out var notification))
            methods.Add(notification.Method);

        Assert.Equal(256, methods.Count);
        Assert.Equal("n/44", methods[0]);
        Assert.Equal("n/299", methods[^1]);
    }
}
