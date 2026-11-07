using System.Text.Json;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>Builds a <see cref="MockMcpTransport"/> that behaves like an MCP server listing the given tools.</summary>
public static class ToolServer
{
    public static MockMcpTransport Create(params string[] tools)
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        transport.SetSendRequestFunc(request => Respond(request, tools));
        return transport;
    }

    public static JsonRpcResponse Respond(JsonRpcRequest request, string[] tools)
    {
        object? result = request.Method switch
        {
            "initialize" => new McpInitializeResult
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = new McpServerCapabilities { Tools = new McpCapabilityInfo { ListChanged = true } },
                ServerInfo = new McpServerInfo { Name = "test", Version = "1.0" },
            },
            "tools/list" => new McpToolListResult
            {
                Tools = tools.Select(name => new McpToolDefinition { Name = name, Description = name }).ToList(),
            },
            "tools/call" => new McpToolCallResult { Content = [new() { Type = "text", Text = "done" }] },
            _ => null,
        };

        if (result is null)
            return new JsonRpcResponse { Id = request.Id, Error = new JsonRpcError(-32601, "Not found") };

        return new JsonRpcResponse { Id = request.Id, Result = JsonSerializer.SerializeToElement(result, result.GetType()) };
    }
}
