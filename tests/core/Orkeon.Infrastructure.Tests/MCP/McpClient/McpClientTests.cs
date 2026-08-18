using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using McpClientSut = Orkeon.Infrastructure.MCP.McpClient;

namespace Orkeon.Infrastructure.Tests.MCP;

public sealed class McpClientTests : IAsyncDisposable
{
    private readonly MockMcpTransport _mockTransport;

    public McpClientTests()
    {
        _mockTransport = new MockMcpTransport();
        _mockTransport.SetConnected(true);
    }

    private McpClientSut CreateClient() => new(_mockTransport);

    private static JsonRpcResponse CreateResponse(int id, object result)
    {
        var json = JsonSerializer.Serialize(result);
        return new JsonRpcResponse
        {
            Id = JsonSerializer.SerializeToElement(id),
            Result = JsonDocument.Parse(json).RootElement
        };
    }

    [Fact]
    public async Task ShouldSendCorrectRequest_WhenInitializing()
    {
        // Arrange
        var client = CreateClient();

        _mockTransport.SetSendRequestFunc(req => CreateResponse(req.Id!.Value.GetInt32(), new McpInitializeResult
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new McpServerCapabilities
            {
                Tools = new McpCapabilityInfo { ListChanged = true }
            },
            ServerInfo = new McpServerInfo { Name = "test-server", Version = "1.0" }
        }));

        // Act
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        var capturedRequest = _mockTransport.AllSentRequests[0];
        Assert.Equal("initialize", capturedRequest.Method);
        Assert.Equal("2.0", capturedRequest.Jsonrpc);
        Assert.NotNull(capturedRequest.Id);
        Assert.NotNull(capturedRequest.Params);
    }

    [Fact]
    public async Task ShouldReturnCapabilities_WhenInitializing()
    {
        // Arrange
        var client = CreateClient();
        _mockTransport.SetSendRequestFunc(req => CreateResponse(req.Id!.Value.GetInt32(), new McpInitializeResult
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new McpServerCapabilities
            {
                Tools = new McpCapabilityInfo { ListChanged = true },
                Resources = new McpCapabilityInfo { ListChanged = false }
            },
            ServerInfo = new McpServerInfo { Name = "test-server", Version = "2.0" }
        }));

        // Act
        var result = await client.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("2024-11-05", result.ProtocolVersion);
        Assert.NotNull(result.Capabilities.Tools);
        Assert.True(result.Capabilities.Tools!.ListChanged);
        Assert.NotNull(result.Capabilities.Resources);
        Assert.NotNull(result.ServerInfo);
        Assert.Equal("test-server", result.ServerInfo!.Name);
        Assert.Equal("2.0", result.ServerInfo.Version);

        Assert.NotNull(client.Capabilities);
        Assert.NotNull(client.ServerInfo);
        Assert.True(client.IsInitialized);
    }

    [Fact]
    public async Task ShouldReturnTools_WhenListingTools()
    {
        // Arrange
        var client = CreateClient();
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
            // tools/list
            return CreateResponse(req.Id!.Value.GetInt32(), new McpToolListResult
            {
                Tools =
                [
                    new() { Name = "search", Description = "Search the web" },
                    new() { Name = "calculate", Description = "Do math" }
                ]
            });
        });

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, tools.Count);
        Assert.Equal("search", tools[0].Name);
        Assert.Equal("Search the web", tools[0].Description);
        Assert.Equal("calculate", tools[1].Name);
    }

    [Fact]
    public async Task ShouldSendCorrectParams_WhenCallingTool()
    {
        // Arrange
        var client = CreateClient();
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
            // tools/call
            return CreateResponse(req.Id!.Value.GetInt32(), new McpToolCallResult
            {
                Content = [new() { Type = "text", Text = "42" }]
            });
        });

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        var args = JsonDocument.Parse("{\"expression\":\"6*7\"}").RootElement;

        // Act
        await client.CallToolAsync("calculate", args, TestContext.Current.CancellationToken);

        // Assert
        var capturedToolCall = _mockTransport.AllSentRequests[1]; // second request (after initialize)
        Assert.Equal("tools/call", capturedToolCall.Method);
        Assert.NotNull(capturedToolCall.Params);

        var paramsObj = JsonSerializer.Deserialize<McpToolCallParams>(
            capturedToolCall.Params!.Value.GetRawText());
        Assert.NotNull(paramsObj);
        Assert.Equal("calculate", paramsObj!.Name);
        Assert.NotNull(paramsObj.Arguments);
    }

    [Fact]
    public async Task ShouldReturnResult_WhenCallingTool()
    {
        // Arrange
        var client = CreateClient();
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
            // tools/call
            return CreateResponse(req.Id!.Value.GetInt32(), new McpToolCallResult
            {
                Content =
                [
                    new() { Type = "text", Text = "The answer is 42" }
                ]
            });
        });

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.CallToolAsync("calculate", ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.IsError);
        Assert.Single(result.Content);
        Assert.Equal("The answer is 42", result.Content[0].Text);
    }

    [Fact]
    public async Task ShouldReturnResources_WhenListingResources()
    {
        // Arrange
        var client = CreateClient();
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
            // resources/list
            return CreateResponse(req.Id!.Value.GetInt32(), new McpResourceListResult
            {
                Resources =
                [
                    new()
                    {
                        Uri = new Uri("file:///data/config.json"),
                        Name = "Config",
                        Description = "Application configuration",
                        MimeType = "application/json"
                    }
                ]
            });
        });

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        var resources = await client.ListResourcesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(resources);
        Assert.Equal(new Uri("file:///data/config.json"), resources[0].Uri);
        Assert.Equal("Config", resources[0].Name);
        Assert.Equal("Application configuration", resources[0].Description);
        Assert.Equal("application/json", resources[0].MimeType);
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenListingToolsWithoutInitialize()
    {
        // Arrange
        var client = CreateClient();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListToolsAsync(TestContext.Current.CancellationToken));
        Assert.Contains("not connected", ex.Message);
    }

    [Fact]
    public async Task ShouldReturnErrorResult_WhenServerReturnsError()
    {
        // Arrange
        var client = CreateClient();
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
            // tools/call returns error
            return new JsonRpcResponse
            {
                Id = req.Id,
                Error = new JsonRpcError(-32602, "Invalid tool parameters")
            };
        });

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.CallToolAsync("nonexistent", ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Assert.Single(result.Content);
        Assert.Contains("Invalid tool parameters", result.Content[0].Text);
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenServerReturnsInitError()
    {
        // Arrange
        var client = CreateClient();

        _mockTransport.SetSendRequestResult(new JsonRpcResponse
        {
            Id = JsonSerializer.SerializeToElement(1),
            Error = new JsonRpcError(-32600, "Unsupported protocol version")
        });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Contains("initialization failed", ex.Message);
    }

    public async ValueTask DisposeAsync()
    {
        await _mockTransport.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
