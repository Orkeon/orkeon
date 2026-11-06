using Orkeon.Tools.Web.Firefox.BiDi;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests.Firefox;

public class WebSocketBiDiTransportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_SendAndReceiveATextMessage()
    {
        await using var server = new FakeWebSocketServer(FakeWebSocketServer.EchoAsync);
        await using var transport = await WebSocketBiDiTransport.ConnectAsync(server.Endpoint, Ct);

        await transport.SendAsync("""{"id":1,"method":"session.status","params":{}}""", Ct);

        Assert.Equal("""{"id":1,"method":"session.status","params":{}}""", await transport.ReceiveAsync(Ct));
    }

    [Fact]
    public async Task Should_ReceiveAMessageWhole_When_ItIsLargerThanOneReadAndNotAscii()
    {
        // 3 bytes per character: a read boundary falls inside a character.
        var large = new string('€', 40_000);
        await using var server = new FakeWebSocketServer(FakeWebSocketServer.EchoAsync);
        await using var transport = await WebSocketBiDiTransport.ConnectAsync(server.Endpoint, Ct);

        await transport.SendAsync(large, Ct);
        await transport.SendAsync("after", Ct);

        Assert.Equal(large, await transport.ReceiveAsync(Ct));
        Assert.Equal("after", await transport.ReceiveAsync(Ct));
    }

    [Fact]
    public async Task Should_ReceiveNull_When_TheServerClosesTheSocket()
    {
        await using var server = new FakeWebSocketServer(async (socket, ct) =>
        {
            await FakeWebSocketServer.SendMessageAsync(socket, "last", ct);
            await socket.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, ct);
        });
        await using var transport = await WebSocketBiDiTransport.ConnectAsync(server.Endpoint, Ct);

        Assert.Equal("last", await transport.ReceiveAsync(Ct));
        Assert.Null(await transport.ReceiveAsync(Ct));
        Assert.Null(await transport.ReceiveAsync(Ct));
    }

    [Fact]
    public async Task Should_ReceiveNull_When_TheServerGoesAwayWithoutClosing()
    {
        await using var server = new FakeWebSocketServer((socket, _) =>
        {
            socket.Abort();
            return Task.CompletedTask;
        });
        await using var transport = await WebSocketBiDiTransport.ConnectAsync(server.Endpoint, Ct);

        Assert.Null(await transport.ReceiveAsync(Ct));
    }
}
