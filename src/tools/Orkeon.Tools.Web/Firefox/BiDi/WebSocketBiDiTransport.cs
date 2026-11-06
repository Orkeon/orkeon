using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace Orkeon.Tools.Web.Firefox.BiDi;

/// <summary>
/// <see cref="IBiDiTransport"/> over a WebSocket: the endpoint a browser started with
/// <c>--remote-debugging-port</c> listens on. One receiver and one sender at a time, as the
/// WebSocket requires; <see cref="BiDiConnection"/> has a single receive loop, and sends are serialised here.
/// </summary>
internal sealed class WebSocketBiDiTransport : IBiDiTransport
{
    private const int ReceiveChunk = 16 * 1024;

    private readonly ClientWebSocket _socket;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ArrayBufferWriter<byte> _incoming = new(ReceiveChunk);

    private WebSocketBiDiTransport(ClientWebSocket socket) => _socket = socket;

    /// <summary>Opens the WebSocket at <paramref name="endpoint"/>.</summary>
    public static async Task<WebSocketBiDiTransport> ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return new WebSocketBiDiTransport(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(
                Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        _incoming.Clear();
        while (true)
        {
            if (_socket.State is not (WebSocketState.Open or WebSocketState.CloseSent))
                return null;

            ValueWebSocketReceiveResult frame;
            try
            {
                frame = await _socket.ReceiveAsync(_incoming.GetMemory(ReceiveChunk), cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // The browser went away without a close frame.
                return null;
            }

            if (frame.MessageType == WebSocketMessageType.Close)
                return null;

            _incoming.Advance(frame.Count);
            if (frame.EndOfMessage)
                return Encoding.UTF8.GetString(_incoming.WrittenSpan);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, grace.Token).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Closing is a courtesy: a browser already gone must not fail the disposal
            catch (Exception)
#pragma warning restore CA1031
            {
                // Nothing to tell a peer that left.
            }
        }

        _socket.Dispose();
        _sendGate.Dispose();
    }
}
