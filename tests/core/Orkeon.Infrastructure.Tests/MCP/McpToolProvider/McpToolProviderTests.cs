using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using McpToolProviderSut = Orkeon.Infrastructure.MCP.McpToolProvider;

namespace Orkeon.Infrastructure.Tests.MCP;

public sealed class McpToolProviderTests : IAsyncDisposable
{
    private readonly MockToolRegistry _mockRegistry;
    private readonly MockMcpTransport _mockTransport;

    public McpToolProviderTests()
    {
        _mockRegistry = new MockToolRegistry();
        _mockTransport = new MockMcpTransport();
        _mockTransport.SetConnected(true);
    }

    private void SetupTransportForToolDiscovery(List<McpToolDefinition>? tools = null)
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
    }

    [Fact]
    public async Task ShouldRegisterToolsInRegistry_WhenConnectingServer()
    {
        // Arrange
        SetupTransportForToolDiscovery();
        await using var provider = new McpToolProviderSut(_mockRegistry);

        // Act
        await provider.ConnectServerAsync("server1", _mockTransport, TestContext.Current.CancellationToken);

        // Assert - search and fetch should be registered
        Assert.True(_mockRegistry.RegisterToolCallCount >= 2);
    }

    [Fact]
    public async Task ShouldRemoveClient_WhenDisconnectingServer()
    {
        // Arrange
        SetupTransportForToolDiscovery();
        await using var provider = new McpToolProviderSut(_mockRegistry);
        await provider.ConnectServerAsync("server1", _mockTransport, TestContext.Current.CancellationToken);

        // Act
        await provider.DisconnectServerAsync("server1");

        // Assert
        Assert.True(_mockRegistry.UnregisterToolCallCount >= 2);

        var statuses = provider.GetServerStatuses();
        Assert.Empty(statuses);
    }

    [Fact]
    public async Task ShouldReturnConnectedServers_WhenGettingStatuses()
    {
        // Arrange
        SetupTransportForToolDiscovery();
        await using var provider = new McpToolProviderSut(_mockRegistry);
        await provider.ConnectServerAsync("server1", _mockTransport, TestContext.Current.CancellationToken);

        // Act
        var statuses = provider.GetServerStatuses();

        // Assert
        Assert.Single(statuses);
        Assert.Equal("server1", statuses[0].ServerId);
        Assert.True(statuses[0].IsConnected);
        Assert.NotNull(statuses[0].Capabilities);
        Assert.NotNull(statuses[0].Capabilities!.Tools);
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenConnectingDuplicateServerId()
    {
        // Arrange
        SetupTransportForToolDiscovery();
        await using var provider = new McpToolProviderSut(_mockRegistry);
        await provider.ConnectServerAsync("server1", _mockTransport, TestContext.Current.CancellationToken);

        // Create a second mock transport for the duplicate attempt
        await using var mockTransport2 = new MockMcpTransport();
        mockTransport2.SetConnected(true);

        // Act & Assert
        var act = () => provider.ConnectServerAsync("server1", mockTransport2);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("already connected", ex.Message);
    }

    [Fact]
    public async Task ShouldDoNothing_WhenDisconnectingUnknownServer()
    {
        // Arrange
        await using var provider = new McpToolProviderSut(_mockRegistry);

        // Act & Assert (should not throw)
        await provider.DisconnectServerAsync("nonexistent");

        Assert.Equal(0, _mockRegistry.UnregisterToolCallCount);
    }

    [Fact]
    public async Task ShouldRegisterNothing_WhenServerHasNoTools()
    {
        // Arrange
        SetupTransportForToolDiscovery([]);
        await using var provider = new McpToolProviderSut(_mockRegistry);

        // Act
        await provider.ConnectServerAsync("server1", _mockTransport, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, _mockRegistry.RegisterToolCallCount);
    }

    [Fact]
    public async Task ShouldDisconnectAllServers_WhenDisposed()
    {
        // Arrange
        SetupTransportForToolDiscovery();
        var provider = new McpToolProviderSut(_mockRegistry);
        await provider.ConnectServerAsync("server1", _mockTransport, TestContext.Current.CancellationToken);

        // Act
        await provider.DisposeAsync();

        // Assert
        var statuses = provider.GetServerStatuses();
        Assert.Empty(statuses);
    }

    public async ValueTask DisposeAsync()
    {
        await _mockTransport.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
