using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

public sealed class McpToolProviderTestsFixture : IAsyncDisposable
{
    private readonly MockToolRegistry _mockRegistry;
    private readonly MockMcpTransport _mockTransport;

    public McpToolProviderTestsFixture()
    {
        _mockRegistry = new MockToolRegistry();
        _mockTransport = new MockMcpTransport();
        _mockTransport.SetConnected(true);
    }

    // --- Fluent configuration ---

    public McpToolProviderTestsFixture WithToolDiscovery(List<McpToolDefinition>? tools = null)
    {
        tools ??=
        [
            new() { Name = "search", Description = "Search things" },
            new() { Name = "fetch", Description = "Fetch data" }
        ];

        _mockTransport.SetSendRequestFunc((req, _) =>
        {
            object result;
            if (req.Method == "initialize")
            {
                result = new McpInitializeResult
                {
                    ProtocolVersion = "2024-11-05",
                    Capabilities = new McpServerCapabilities
                    {
                        Tools = new McpCapabilityInfo { ListChanged = true }
                    },
                    ServerInfo = new McpServerInfo { Name = "test", Version = "1.0" }
                };
            }
            else if (req.Method == "tools/list")
            {
                result = new McpToolListResult { Tools = tools };
            }
            else
            {
                return Task.FromResult(new JsonRpcResponse
                {
                    Id = req.Id,
                    Error = new JsonRpcError(-32601, "Not found")
                });
            }

            var json = JsonSerializer.Serialize(result);
            return Task.FromResult(new JsonRpcResponse
            {
                Id = req.Id,
                Result = JsonDocument.Parse(json).RootElement
            });
        });

        return this;
    }

    // --- Build / Execution ---

    public McpToolProvider Build() => new(_mockRegistry);

    public async Task<McpToolProvider> BuildAndConnect(string serverId = "server1")
    {
        var provider = Build();
        await provider.ConnectServerAsync(serverId, _mockTransport);
        return provider;
    }

    // --- Inspection ---

    public MockToolRegistry GetRegistry() => _mockRegistry;
    public MockMcpTransport GetTransport() => _mockTransport;

    public static MockMcpTransport CreateSecondTransport()
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        return transport;
    }

    public async ValueTask DisposeAsync()
    {
        await _mockTransport.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
