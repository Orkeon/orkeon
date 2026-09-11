using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

public class McpServerTestsFixture
{
    private readonly MockToolRegistry _mockRegistry;
    private readonly McpServerOptions _options;

    public McpServerTestsFixture()
    {
        _mockRegistry = new MockToolRegistry();
        _options = new McpServerOptions
        {
            Name = "TestServer",
            Version = "1.0.0"
        };
    }

    // --- Fluent configuration ---

    public McpServerTestsFixture WithTool(string name, string description,
        Dictionary<string, ParameterSchema>? parameters = null,
        ToolCallResponse? callResult = null)
    {
        var tool = new InlineMockBaseTool(name, description, parameters, callResult);
        _mockRegistry.AddTool(name, tool);
        return this;
    }

    public McpServerTestsFixture WithInlineTool(string name, IBaseTool tool)
    {
        _mockRegistry.AddTool(name, tool);
        return this;
    }

    // --- Build / Execution ---

    public McpServer Build() => new(_mockRegistry, _options);

    public async Task<JsonRpcResponse> ProcessRequestAsync(JsonRpcRequest request)
        => (await Build().ProcessRequestAsync(request))!;

    // --- Request factories ---

    public static JsonRpcRequest CreateInitializeRequest(int id = 1)
        => new()
        {
            Method = "initialize",
            Id = JsonSerializer.SerializeToElement(id),
            Params = JsonElement.Parse(@"{
                ""protocolVersion"": ""2024-11-05"",
                ""capabilities"": {},
                ""clientInfo"": { ""name"": ""test-client"", ""version"": ""1.0"" }
            }")
        };

    public static JsonRpcRequest CreateToolsListRequest(int id = 2)
        => new() { Method = "tools/list", Id = JsonSerializer.SerializeToElement(id) };

    public static JsonRpcRequest CreateToolCallRequest(string name, string argsJson = "{}", int id = 3)
        => new()
        {
            Method = "tools/call",
            Id = JsonSerializer.SerializeToElement(id),
            Params = JsonElement.Parse($@"{{""name"": ""{name}"", ""arguments"": {argsJson}}}")
        };

    // --- Inspection ---

    public MockToolRegistry GetRegistry() => _mockRegistry;
}
