using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Stubs;
using Orkeon.Tests.Shared.Doubles;
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
                Result = JsonElement.Parse(json)
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

    // GAP-01: an MCP tool whose name is already taken is refused, never substituted. The
    // tool that held the name stays, and the server's disconnection cannot take it away.

    [Fact]
    public async Task McpToolNamedLikeARegisteredTool_IsRefused_AndTheOriginalSurvivesDisconnect()
    {
        var registry = new InMemoryToolRegistry(NullLogger<InMemoryToolRegistry>.Instance);
        var builtIn = new StubBaseTool("file_read");
        Assert.True(await registry.RegisterToolAsync(builtIn));
        await using var transport = McpDiscoveryTransport.Exposing("file_read", "search_issues");
        using var logs = new RecordingLoggerFactory();
        await using var provider = new McpToolProviderSut(registry, logs);

        await provider.ConnectServerAsync("x", transport, TestContext.Current.CancellationToken);

        Assert.Same(builtIn, await registry.GetToolByNameAsync("file_read"));
        Assert.IsType<McpToolAdapter>(await registry.GetToolByNameAsync("search_issues"));
        Assert.Equal(["file_read"], Assert.Single(provider.GetServerStatuses()).RejectedToolNames);
        Assert.True(logs.Logger.HasEntry(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Error
            && e.Message.Contains("tool 'file_read' of MCP server 'x' collides with an already-registered tool", StringComparison.Ordinal)));

        await provider.DisconnectServerAsync("x");

        Assert.Same(builtIn, await registry.GetToolByNameAsync("file_read"));
        Assert.Null(await registry.GetToolByNameAsync("search_issues"));
    }

    [Fact]
    public async Task TwoServersExposingTheSameName_TheSecondIsRefused_AndSurvivesItsDisconnect()
    {
        var registry = new InMemoryToolRegistry(NullLogger<InMemoryToolRegistry>.Instance);
        await using var first = McpDiscoveryTransport.Exposing("search");
        await using var second = McpDiscoveryTransport.Exposing("search", "fetch");
        await using var provider = new McpToolProviderSut(registry);

        await provider.ConnectServerAsync("one", first, TestContext.Current.CancellationToken);
        var owner = await registry.GetToolByNameAsync("search");
        await provider.ConnectServerAsync("two", second, TestContext.Current.CancellationToken);

        Assert.NotNull(owner);
        Assert.Same(owner, await registry.GetToolByNameAsync("search"));
        Assert.NotNull(await registry.GetToolByNameAsync("fetch"));

        await provider.DisconnectServerAsync("two");

        Assert.Same(owner, await registry.GetToolByNameAsync("search"));
        Assert.Null(await registry.GetToolByNameAsync("fetch"));
    }

    public async ValueTask DisposeAsync()
    {
        await _mockTransport.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
