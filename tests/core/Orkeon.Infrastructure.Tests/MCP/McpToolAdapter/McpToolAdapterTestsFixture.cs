using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

public sealed class McpToolAdapterTestsFixture : IAsyncDisposable
{
    private readonly MockMcpTransport _mockTransport;

    public McpToolAdapterTestsFixture()
    {
        _mockTransport = new MockMcpTransport();
        _mockTransport.SetConnected(true);
    }

    // --- Fluent configuration ---

    public McpToolAdapterTestsFixture WithTransportFunc(Func<JsonRpcRequest, JsonRpcResponse> func)
    {
        _mockTransport.SetSendRequestFunc(func);
        return this;
    }

    // --- Build ---

    public static McpToolAdapter CreateAdapter(McpToolDefinition definition, McpClient client)
        => new(definition, client);

    public async Task<McpClient> CreateInitializedClient()
    {
        SetupTransportForInitAndToolCall();
        var client = new McpClient(_mockTransport);
        await client.InitializeAsync();
        return client;
    }

    public McpClient CreateUninitializedClient()
        => new(_mockTransport);

    public void SetupTransportForInitAndToolCall()
    {
        _mockTransport.SetSendRequestFunc(req =>
        {
            if (req.Method == "initialize")
            {
                var initResult = new McpInitializeResult
                {
                    ProtocolVersion = "2024-11-05",
                    Capabilities = new McpServerCapabilities()
                };
                var json = JsonSerializer.Serialize(initResult);
                return new JsonRpcResponse
                {
                    Id = req.Id,
                    Result = JsonDocument.Parse(json).RootElement
                };
            }

            if (req.Method == "tools/call")
            {
                var toolResult = new McpToolCallResult
                {
                    Content =
                    [
                        new() { Type = "text", Text = "Result from MCP tool" }
                    ]
                };
                var json = JsonSerializer.Serialize(toolResult);
                return new JsonRpcResponse
                {
                    Id = req.Id,
                    Result = JsonDocument.Parse(json).RootElement
                };
            }

            return new JsonRpcResponse
            {
                Id = req.Id,
                Error = new JsonRpcError(-32601, "Unknown method")
            };
        });
    }

    // --- Definition factories ---

    public static McpToolDefinition CreateDefinition(string name = "test-tool", string desc = "A test tool")
    {
        var inputSchema = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""query"": { ""type"": ""string"", ""description"": ""Search query"" },
                ""limit"": { ""type"": ""integer"", ""description"": ""Max results"", ""default"": 10 }
            },
            ""required"": [""query""]
        }").RootElement;

        return new McpToolDefinition
        {
            Name = name,
            Description = desc,
            InputSchema = inputSchema
        };
    }

    public static McpToolDefinition CreateSimpleDefinition(string name = "simple-tool", string desc = "No schema")
        => new() { Name = name, Description = desc };

    public static McpToolDefinition CreateDefinitionWithSchema(string name, string desc, string schemaJson)
        => new()
        {
            Name = name,
            Description = desc,
            InputSchema = JsonDocument.Parse(schemaJson).RootElement
        };

    // --- Inspection ---

    public MockMcpTransport GetTransport() => _mockTransport;

    public async ValueTask DisposeAsync()
    {
        await _mockTransport.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
