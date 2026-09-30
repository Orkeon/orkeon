using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// Builds a connected <see cref="MockMcpTransport"/> that answers the handshake and lists the
/// named tools, as an MCP server exposing exactly those tools would.
/// </summary>
internal static class McpDiscoveryTransport
{
    public static MockMcpTransport Exposing(params string[] toolNames)
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        var tools = toolNames
            .Select(n => new McpToolDefinition { Name = n, Description = $"{n} (MCP)" })
            .ToList();

        transport.SetSendRequestFunc((req, _) =>
        {
            object result;
            if (req.Method == "initialize")
            {
                result = new McpInitializeResult
                {
                    ProtocolVersion = "2024-11-05",
                    Capabilities = new McpServerCapabilities { Tools = new McpCapabilityInfo { ListChanged = true } },
                    ServerInfo = new McpServerInfo { Name = "test", Version = "1.0" }
                };
            }
            else if (req.Method == "tools/list")
            {
                result = new McpToolListResult { Tools = tools };
            }
            else
            {
                return Task.FromResult(new JsonRpcResponse { Id = req.Id, Error = new JsonRpcError(-32601, "Not found") });
            }

            return Task.FromResult(new JsonRpcResponse
            {
                Id = req.Id,
                Result = JsonElement.Parse(JsonSerializer.Serialize(result))
            });
        });

        return transport;
    }
}
