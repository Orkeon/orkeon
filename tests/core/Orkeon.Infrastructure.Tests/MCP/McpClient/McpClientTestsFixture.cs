using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

public sealed class McpClientTestsFixture : IAsyncDisposable
{
    private readonly MockMcpTransport _mockTransport;

    public McpClientTestsFixture()
    {
        _mockTransport = new MockMcpTransport();
        _mockTransport.SetConnected(true);
    }

    // --- Fluent configuration ---

    public McpClientTestsFixture WithSendRequestFunc(Func<JsonRpcRequest, JsonRpcResponse> func)
    {
        _mockTransport.SetSendRequestFunc(func);
        return this;
    }

    public McpClientTestsFixture WithSendRequestResult(JsonRpcResponse response)
    {
        _mockTransport.SetSendRequestResult(response);
        return this;
    }

    public McpClientTestsFixture WithInitializeResponse(
        string protocolVersion = "2024-11-05",
        McpServerCapabilities? capabilities = null,
        McpServerInfo? serverInfo = null)
    {
        return WithSendRequestFunc(req => CreateResponse(req.Id!.Value.GetInt32(), new McpInitializeResult
        {
            ProtocolVersion = protocolVersion,
            Capabilities = capabilities ?? new McpServerCapabilities(),
            ServerInfo = serverInfo
        }));
    }

    public McpClientTestsFixture WithInitializeAndToolCall(
        Func<JsonRpcRequest, int, JsonRpcResponse> nonInitHandler)
    {
        var callCount = 0;
        _mockTransport.SetSendRequestFunc(req =>
        {
            callCount++;
            if (callCount == 1) // initialize
            {
                return CreateResponse(req.Id!.Value.GetInt32(), new McpInitializeResult
                {
                    ProtocolVersion = "2024-11-05",
                    Capabilities = new McpServerCapabilities()
                });
            }
            return nonInitHandler(req, callCount);
        });
        return this;
    }

    // --- Build / Execution ---

    public McpClient Build() => new(_mockTransport);

    public async Task<McpClient> BuildAndInitialize()
    {
        var client = Build();
        await client.InitializeAsync();
        return client;
    }

    // --- Response factory ---

    public static JsonRpcResponse CreateResponse(int id, object result)
    {
        var json = JsonSerializer.Serialize(result);
        return new JsonRpcResponse
        {
            Id = JsonSerializer.SerializeToElement(id),
            Result = JsonElement.Parse(json)
        };
    }

    // --- Inspection ---

    public MockMcpTransport GetTransport() => _mockTransport;
    public IReadOnlyList<JsonRpcRequest> GetSentRequests() => _mockTransport.AllSentRequests;

    public async ValueTask DisposeAsync()
    {
        await _mockTransport.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
