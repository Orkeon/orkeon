using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>
/// A loopback WebSocket server that accepts one client and hands its socket to <c>serve</c>:
/// the browser end of a <c>WebSocketBiDiTransport</c> under test.
/// </summary>
internal sealed class FakeWebSocketServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _serving;

    public FakeWebSocketServer(Func<WebSocket, CancellationToken, Task> serve)
    {
        (_listener, var prefix) = LoopbackPorts.StartListener();
        Endpoint = new UriBuilder(prefix) { Scheme = "ws" }.Uri;
        _serving = Task.Run(() => ServeAsync(serve));
    }

    public Uri Endpoint { get; }

    /// <summary>Sends every message received back to the client, until it leaves.</summary>
    public static async Task EchoAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        while (await ReadMessageAsync(socket, cancellationToken) is { } message)
            await SendMessageAsync(socket, message, cancellationToken);
    }

    public static Task SendMessageAsync(WebSocket socket, string message, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    public static async Task<string?> ReadMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        while (true)
        {
            var frame = await socket.ReceiveAsync(buffer.GetMemory(4096), cancellationToken);
            if (frame.MessageType == WebSocketMessageType.Close) return null;
            buffer.Advance(frame.Count);
            if (frame.EndOfMessage) return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Close();
        await _serving;
        _stopping.Dispose();
    }

    private async Task ServeAsync(Func<WebSocket, CancellationToken, Task> serve)
    {
        try
        {
            var context = await _listener.GetContextAsync();
            var upgrade = await context.AcceptWebSocketAsync(subProtocol: null);
            using var socket = upgrade.WebSocket;
            await serve(socket, _stopping.Token);
        }
        catch (Exception) when (_stopping.IsCancellationRequested)
        {
            // The test is over: the listener or the socket was closed under the server.
        }
        catch (WebSocketException)
        {
            // The client left without a close frame.
        }
    }
}
