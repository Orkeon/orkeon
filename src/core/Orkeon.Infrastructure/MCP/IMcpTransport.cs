namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Transport abstraction for MCP client-server communication.
/// Implementations handle the wire protocol (stdio, SSE, etc.).
/// </summary>
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
    /// Whether the transport is currently connected.
    /// </summary>
    bool IsConnected { get; }
}
