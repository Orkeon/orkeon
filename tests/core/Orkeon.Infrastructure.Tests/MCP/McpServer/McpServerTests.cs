using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using McpServerSut = Orkeon.Infrastructure.MCP.McpServer;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// Simple inline mock tool for MCP server tests.
/// </summary>
internal class InlineMockBaseTool : IBaseTool
{
    private readonly ToolCallResponse _callResult;

    public string Name { get; }
    public string Description { get; }
    public ToolSchema Schema { get; }

    public InlineMockBaseTool(
        string name,
        string description,
        Dictionary<string, ParameterSchema>? parameters = null,
        ToolCallResponse? callResult = null)
    {
        Name = name;
        Description = description;
        Schema = new ToolSchema(name, description, parameters ?? []);
        _callResult = callResult ?? new ToolCallResponse(true, "mock result", null);
    }

    public int CallAsyncCallCount { get; private set; }
    public ToolCallRequest? LastCallRequest { get; private set; }

    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        CallAsyncCallCount++;
        LastCallRequest = request;
        return Task.FromResult(_callResult);
    }

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(ToolResult.CreateSuccess("mock output"));

    public bool ValidateInput(string input) => true;
}

public class McpServerTests
{
    private readonly MockToolRegistry _mockRegistry;
    private readonly McpServerOptions _options;

    public McpServerTests()
    {
        _mockRegistry = new MockToolRegistry();
        _options = new McpServerOptions
        {
            Name = "TestServer",
            Version = "1.0.0"
        };
    }

    private McpServerSut CreateServer() => new(_mockRegistry, _options);

    [Fact]
    public async Task ShouldReturnCapabilities_WhenHandlingInitialize()
    {
        // Arrange
        var server = CreateServer();
        var request = new JsonRpcRequest
        {
            Method = "initialize",
            Id = 1,
            Params = JsonDocument.Parse(@"{
                ""protocolVersion"": ""2024-11-05"",
                ""capabilities"": {},
                ""clientInfo"": { ""name"": ""test-client"", ""version"": ""1.0"" }
            }").RootElement
        };

        // Act
        var response = await server.ProcessRequestAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(response.Error);
        Assert.Equal(1, response.Id);
        Assert.NotNull(response.Result);

        var result = JsonSerializer.Deserialize<McpInitializeResult>(
            response.Result!.Value.GetRawText());
        Assert.NotNull(result);
        Assert.Equal("2024-11-05", result!.ProtocolVersion);
        Assert.NotNull(result.ServerInfo);
        Assert.Equal("TestServer", result.ServerInfo!.Name);
        Assert.Equal("1.0.0", result.ServerInfo.Version);
        Assert.NotNull(result.Capabilities.Tools);
    }

    [Fact]
    public async Task ShouldReturnRegistryTools_WhenHandlingToolsList()
    {
        // Arrange
        var server = CreateServer();

        var tool1 = new InlineMockBaseTool("search", "Search the web",
            new Dictionary<string, ParameterSchema>
            {
                [ParamQuery] = new("string", "Search query", true)
            });

        var tool2 = new InlineMockBaseTool("calculate", "Do math",
            new Dictionary<string, ParameterSchema>
            {
                ["expression"] = new("string", "Math expression", true)
            });

        _mockRegistry.AddTool("search", tool1);
        _mockRegistry.AddTool("calculate", tool2);

        var request = new JsonRpcRequest { Method = "tools/list", Id = 2 };

        // Act
        var response = await server.ProcessRequestAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(response.Error);

        var result = JsonSerializer.Deserialize<McpToolListResult>(
            response.Result!.Value.GetRawText());
        Assert.NotNull(result);
        Assert.Equal(2, result!.Tools.Count);
        Assert.Equal("search", result.Tools[0].Name);
        Assert.Equal("calculate", result.Tools[1].Name);
    }

    [Fact]
    public async Task ShouldExecuteTool_WhenHandlingToolsCall()
    {
        // Arrange
        var server = CreateServer();

        var mockTool = new InlineMockBaseTool("search", "Search the web",
            callResult: new ToolCallResponse(true, "Found 5 results", null));

        _mockRegistry.AddTool("search", mockTool);

        var request = new JsonRpcRequest
        {
            Method = "tools/call",
            Id = 3,
            Params = JsonDocument.Parse(@"{
                ""name"": ""search"",
                ""arguments"": { ""query"": ""hello"" }
            }").RootElement
        };

        // Act
        var response = await server.ProcessRequestAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(response.Error);

        var result = JsonSerializer.Deserialize<McpToolCallResult>(
            response.Result!.Value.GetRawText());
        Assert.NotNull(result);
        Assert.False(result!.IsError);
        Assert.Single(result.Content);
        Assert.Equal("Found 5 results", result.Content[0].Text);

        Assert.Equal(1, mockTool.CallAsyncCallCount);
        Assert.Equal("search", mockTool.LastCallRequest!.ToolName);
        Assert.True(mockTool.LastCallRequest.Parameters.ContainsKey(ParamQuery));
    }

    [Fact]
    public async Task ShouldReturnError_WhenToolNotFound()
    {
        // Arrange
        var server = CreateServer();
        // No tools registered

        var request = new JsonRpcRequest
        {
            Method = "tools/call",
            Id = 4,
            Params = JsonDocument.Parse(@"{
                ""name"": ""nonexistent"",
                ""arguments"": {}
            }").RootElement
        };

        // Act
        var response = await server.ProcessRequestAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error!.Code);
        Assert.Contains("Tool not found", response.Error.Message);
    }

    [Fact]
    public async Task ShouldReturnMethodNotFound_WhenMethodIsUnknown()
    {
        // Arrange
        var server = CreateServer();
        var request = new JsonRpcRequest
        {
            Method = "unknown/method",
            Id = 5
        };

        // Act
        var response = await server.ProcessRequestAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response.Error);
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
        Assert.Contains("Method not found", response.Error.Message);
    }

    [Fact]
    public async Task ShouldDispatchCorrectly_WhenProcessingRequests()
    {
        // Arrange
        var server = CreateServer();
        // No tools in registry

        // Act — verify each method dispatches to the correct handler
        var initResponse = await server.ProcessRequestAsync(
            new JsonRpcRequest { Method = "initialize", Id = 1 }, TestContext.Current.CancellationToken);
        var toolsListResponse = await server.ProcessRequestAsync(
            new JsonRpcRequest { Method = "tools/list", Id = 2 }, TestContext.Current.CancellationToken);
        var unknownResponse = await server.ProcessRequestAsync(
            new JsonRpcRequest { Method = "prompts/list", Id = 3 }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(initResponse.Error);
        Assert.Null(toolsListResponse.Error);
        Assert.NotNull(unknownResponse.Error);
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, unknownResponse.Error!.Code);
    }

    [Fact]
    public async Task ShouldReturnInvalidParams_WhenToolNameMissing()
    {
        // Arrange
        var server = CreateServer();
        var request = new JsonRpcRequest
        {
            Method = "tools/call",
            Id = 6,
            Params = JsonDocument.Parse(@"{}").RootElement
        };

        // Act
        var response = await server.ProcessRequestAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error!.Code);
        Assert.Contains("Missing tool name", response.Error.Message);
    }

    [Fact]
    public async Task ShouldReturnIsErrorTrue_WhenToolReturnsError()
    {
        // Arrange
        var server = CreateServer();

        var mockTool = new InlineMockBaseTool("failing-tool", "A failing tool",
            callResult: new ToolCallResponse(false, null, "Something went wrong"));

        _mockRegistry.AddTool("failing-tool", mockTool);

        var request = new JsonRpcRequest
        {
            Method = "tools/call",
            Id = 7,
            Params = JsonDocument.Parse(@"{
                ""name"": ""failing-tool"",
                ""arguments"": {}
            }").RootElement
        };

        // Act
        var response = await server.ProcessRequestAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(response.Error); // JSON-RPC level success, tool-level error

        var result = JsonSerializer.Deserialize<McpToolCallResult>(
            response.Result!.Value.GetRawText());
        Assert.NotNull(result);
        Assert.True(result!.IsError);
        Assert.Equal("Something went wrong", result.Content[0].Text);
    }

    [Fact]
    public void ShouldConvertCorrectly_WhenConvertingToolSchemaToJsonSchema()
    {
        // Arrange
        var schema = new ToolSchema("test", "A test", new Dictionary<string, ParameterSchema>
        {
            ["name"] = new("string", "The name", true),
            ["count"] = new("integer", "The count", false, 5)
        });

        // Act
        var jsonSchema = McpServerSut.ConvertToolSchemaToJsonSchema(schema);

        // Assert
        Assert.NotNull(jsonSchema);
        var obj = jsonSchema!.Value;

        Assert.Equal("object", obj.GetProperty("type").GetString());

        var props = obj.GetProperty("properties");
        Assert.Equal("string", props.GetProperty("name").GetProperty("type").GetString());
        Assert.Equal("The name", props.GetProperty("name").GetProperty("description").GetString());
        Assert.Equal(5, props.GetProperty("count").GetProperty("default").GetInt32());

        var required = obj.GetProperty("required");
        Assert.Equal(1, required.GetArrayLength());
        Assert.Equal("name", required[0].GetString());
    }
}
