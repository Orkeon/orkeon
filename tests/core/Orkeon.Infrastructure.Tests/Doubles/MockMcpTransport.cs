using System.Text.Json;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IMcpTransport with call tracking and configurable results.
/// </summary>
public class MockMcpTransport : IMcpTransport
{
    private JsonRpcResponse _sendRequestResult = new() { Jsonrpc = "2.0", Id = JsonSerializer.SerializeToElement(1) };
    private Func<JsonRpcRequest, CancellationToken, Task<JsonRpcResponse>>? _sendRequestFunc;
    private bool _isConnected;

    // --- Tracking ---
    public int ConnectCallCount { get; private set; }
    public int SendRequestCallCount { get; private set; }
    public int SendNotificationCallCount { get; private set; }
    public List<JsonRpcNotification> AllSentNotifications { get; } = [];
    public JsonRpcRequest? LastSentRequest { get; private set; }
    public List<JsonRpcRequest> AllSentRequests { get; } = [];
    public int DisposeCallCount { get; private set; }

    // --- Configuration ---
    public bool IsConnected => _isConnected;

    public void SetConnected(bool connected) => _isConnected = connected;

    public void SetSendRequestResult(JsonRpcResponse result) => _sendRequestResult = result;

    public void SetSendRequestError(int code, string message) =>
        _sendRequestResult = new JsonRpcResponse
        {
            Jsonrpc = "2.0",
            Error = new JsonRpcError(code, message)
        };

    /// <summary>
    /// Sets a sync function that produces a response based on the request.
    /// </summary>
    public void SetSendRequestFunc(Func<JsonRpcRequest, JsonRpcResponse> func) =>
        _sendRequestFunc = (req, _) => Task.FromResult(func(req));

    /// <summary>
    /// Sets an async function to dynamically generate responses based on the request.
    /// </summary>
    public void SetSendRequestFunc(Func<JsonRpcRequest, CancellationToken, Task<JsonRpcResponse>> func) =>
        _sendRequestFunc = func;

    // --- IMcpTransport ---
    public Task ConnectAsync(CancellationToken ct = default)
    {
        ConnectCallCount++;
        _isConnected = true;
        return Task.CompletedTask;
    }

    public async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken ct = default)
    {
        SendRequestCallCount++;
        LastSentRequest = request;
        AllSentRequests.Add(request);

        if (_sendRequestFunc != null)
            return await _sendRequestFunc(request, ct);

        // Mirror the request ID in the response
        var response = new JsonRpcResponse
        {
            Jsonrpc = _sendRequestResult.Jsonrpc,
            Result = _sendRequestResult.Result,
            Error = _sendRequestResult.Error,
            Id = request.Id ?? _sendRequestResult.Id
        };

        return response;
    }

    public Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken ct = default)
    {
        SendNotificationCallCount++;
        AllSentNotifications.Add(notification);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;
        _isConnected = false;
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
