using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Transport abstraction for MCP client-server communication.
/// Implementations handle the wire protocol (stdio, SSE, etc.).
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IMcpTransport : IAsyncDisposable
{
    /// <summary>
    /// Establishes the transport connection.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends a JSON-RPC request and waits for the corresponding response.
    /// </summary>
    Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sends a JSON-RPC notification (no id, no response expected).
    /// </summary>
    Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Whether the transport is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The notifications the server sends (frames without an <c>id</c>), in arrival order. The channel is
    /// bounded; when nobody reads it the oldest notification is dropped, with a warning. It completes when
    /// the transport goes away.
    /// </summary>
    ChannelReader<JsonRpcNotification> Notifications { get; }
}
