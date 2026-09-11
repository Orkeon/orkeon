using Orkeon.Domain.Tools.Protocol;
using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using McpClientSut = Orkeon.Infrastructure.MCP.McpClient;
using McpToolAdapterSut = Orkeon.Infrastructure.MCP.McpToolAdapter;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Infrastructure.Tests.MCP;

public sealed class McpToolAdapterTests : IAsyncDisposable
{
    private readonly MockMcpTransport _mockTransport;

    public McpToolAdapterTests()
    {
        _mockTransport = new MockMcpTransport();
        _mockTransport.SetConnected(true);
    }

    private void SetupTransportForInitAndToolCall()
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
                    Result = JsonElement.Parse(json)
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
                    Result = JsonElement.Parse(json)
                };
            }

            return new JsonRpcResponse
            {
                Id = req.Id,
                Error = new JsonRpcError(-32601, "Unknown method")
            };
        });
    }

    private async Task<McpClientSut> CreateInitializedClient()
    {
        SetupTransportForInitAndToolCall();
        var client = new McpClientSut(_mockTransport);
        await client.InitializeAsync();
        return client;
    }

    private static McpToolDefinition CreateDefinition(string name = "test-tool", string desc = "A test tool")
    {
        var inputSchema = JsonElement.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""query"": { ""type"": ""string"", ""description"": ""Search query"" },
                ""limit"": { ""type"": ""integer"", ""description"": ""Max results"", ""default"": 10 }
            },
            ""required"": [""query""]
        }");

        return new McpToolDefinition
        {
            Name = name,
            Description = desc,
            InputSchema = inputSchema
        };
    }

    [Fact]
    public async Task ShouldDelegateToMcpClient_WhenCallingTool()
    {
        // Arrange
        await using var client = await CreateInitializedClient();
        var definition = CreateDefinition();
        var adapter = new McpToolAdapterSut(definition, client);

        var request = new ToolCallRequest("test-tool", new Dictionary<string, object?>
        {
            [ParamQuery] = "hello world"
        });

        // Act
        var response = await adapter.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.Result);
        Assert.Equal("Result from MCP tool", response.Result!.ToString());
    }

    [Fact]
    public async Task ShouldConvertToFailedResponse_WhenMcpReturnsError()
    {
        // Arrange
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
                    Result = JsonElement.Parse(json)
                };
            }

            // Return error for tools/call
            var errorResult = new McpToolCallResult
            {
                IsError = true,
                Content =
                [
                    new() { Type = "text", Text = "Tool execution failed" }
                ]
            };
            var errorJson = JsonSerializer.Serialize(errorResult);
            return new JsonRpcResponse
            {
                Id = req.Id,
                Result = JsonElement.Parse(errorJson)
            };
        });

        await using var client = new McpClientSut(_mockTransport);
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        var definition = CreateDefinition();
        var adapter = new McpToolAdapterSut(definition, client);

        var request = new ToolCallRequest("test-tool", new Dictionary<string, object?>
        {
            [ParamQuery] = "bad query"
        });

        // Act
        var response = await adapter.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Tool execution failed", response.Error);
    }

    [Fact]
    public void ShouldConvertFromMcpInputSchema_WhenAccessingSchema()
    {
        // Arrange
        var inputSchema = JsonElement.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""query"": { ""type"": ""string"", ""description"": ""Search query"" },
                ""limit"": { ""type"": ""integer"", ""description"": ""Max results"", ""default"": 10 },
                ""format"": { ""type"": ""string"", ""description"": ""Output format"", ""enum"": [""json"", ""csv"", ""text""] }
            },
            ""required"": [""query""]
        }");

        var definition = new McpToolDefinition
        {
            Name = "search",
            Description = "Search something",
            InputSchema = inputSchema
        };

        // Act
        var schema = McpToolAdapterSut.ConvertToToolSchema(definition);

        // Assert
        Assert.Equal("search", schema.Name);
        Assert.Equal("Search something", schema.Description);
        Assert.Equal(3, schema.Parameters.Count);

        Assert.Equal("string", schema.Parameters[ParamQuery].Type);
        Assert.Equal("Search query", schema.Parameters[ParamQuery].Description);
        Assert.True(schema.Parameters[ParamQuery].Required);

        Assert.Equal("integer", schema.Parameters["limit"].Type);
        Assert.Equal(10.0, schema.Parameters["limit"].Default);
        Assert.False(schema.Parameters["limit"].Required);

        Assert.NotNull(schema.Parameters["format"].Enum);
        Assert.Equal(3, schema.Parameters["format"].Enum!.Count);
        Assert.Contains("json", schema.Parameters["format"].Enum!);
        Assert.Contains("csv", schema.Parameters["format"].Enum!);
    }

    [Fact]
    public async Task ShouldReturnToolDefinitionName_WhenAccessingName()
    {
        // Arrange
        var definition = new McpToolDefinition { Name = "my-tool", Description = "desc" };
        await using var client = new McpClientSut(_mockTransport);
        var adapter = new McpToolAdapterSut(definition, client);

        // Assert
        Assert.Equal("my-tool", adapter.Name);
    }

    [Fact]
    public async Task ShouldReturnToolDefinitionDescription_WhenAccessingDescription()
    {
        // Arrange
        var definition = new McpToolDefinition { Name = "tool", Description = "A great tool" };
        await using var client = new McpClientSut(_mockTransport);
        var adapter = new McpToolAdapterSut(definition, client);

        // Assert
        Assert.Equal("A great tool", adapter.Description);
    }

    [Fact]
    public async Task ShouldAlwaysReturnTrue_WhenValidatingInput()
    {
        // Arrange
        var definition = new McpToolDefinition { Name = "tool", Description = "desc" };
        await using var client = new McpClientSut(_mockTransport);
        var adapter = new McpToolAdapterSut(definition, client);

        // Assert
        Assert.True(adapter.ValidateInput("anything"));
        Assert.True(adapter.ValidateInput(""));
        Assert.True(adapter.ValidateInput("invalid json {{{"));
    }

    [Fact]
    public void ShouldReturnEmptyParameters_WhenNoInputSchema()
    {
        // Arrange
        var definition = new McpToolDefinition
        {
            Name = "simple-tool",
            Description = "No schema"
        };

        // Act
        var schema = McpToolAdapterSut.ConvertToToolSchema(definition);

        // Assert
        Assert.Empty(schema.Parameters);
    }

    [Fact]
    public async Task ShouldDelegateToCallAsync_WhenExecuting()
    {
        // Arrange
        await using var client = await CreateInitializedClient();
        var definition = CreateDefinition();
        var adapter = new McpToolAdapterSut(definition, client);

        // Act
        var result = await adapter.ExecuteAsync("{\"query\": \"test\"}", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Result from MCP tool", result.Output);
    }

    [Fact]
    public void ShouldConvertBooleanDefault_WhenConvertingSchema()
    {
        // Arrange
        var inputSchema = JsonElement.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""verbose"": { ""type"": ""boolean"", ""description"": ""Verbose output"", ""default"": true }
            }
        }");

        var definition = new McpToolDefinition
        {
            Name = "tool",
            Description = "desc",
            InputSchema = inputSchema
        };

        // Act
        var schema = McpToolAdapterSut.ConvertToToolSchema(definition);

        // Assert
        Assert.Equal("boolean", schema.Parameters["verbose"].Type);
        Assert.True((bool)schema.Parameters["verbose"].Default!);
    }

    public async ValueTask DisposeAsync()
    {
        await _mockTransport.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
