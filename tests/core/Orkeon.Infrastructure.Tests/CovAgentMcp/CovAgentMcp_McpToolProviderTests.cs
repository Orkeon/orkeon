using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using ProviderSut = Orkeon.Infrastructure.MCP.McpToolProvider;

namespace Orkeon.Infrastructure.Tests.CovAgentMcp;

/// <summary>
/// Coverage tests for <see cref="McpToolProvider"/> config-driven and error paths
/// not covered by the existing happy-path suite. No real processes or network.
/// </summary>
public sealed class CovAgentMcp_McpToolProviderTests
{
    private readonly MockToolRegistry _registry = new();

    [Fact]
    public void Constructor_WithNullRegistry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProviderSut(null!));
    }

    [Fact]
    public async Task ConnectServerAsync_WithSseConfigMissingUrl_ThrowsArgument()
    {
        await using var provider = new ProviderSut(_registry);
        var config = new McpServerConfig { Transport = McpTransportType.Sse, Url = null };

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.ConnectServerAsync("s1", config, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConnectServerAsync_WithUnknownTransport_ThrowsArgument()
    {
        await using var provider = new ProviderSut(_registry);
        var config = new McpServerConfig { Transport = (McpTransportType)999 };

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.ConnectServerAsync("s1", config, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConnectServerAsync_WhenInitializeFails_DisposesTransportAndRethrows()
    {
        await using var transport = new MockMcpTransport();
        transport.SetConnected(true);
        // initialize returns an error -> McpClient throws -> provider cleans up.
        transport.SetSendRequestFunc((req, _) => Task.FromResult(new JsonRpcResponse
        {
            Id = req.Id,
            Error = new JsonRpcError(JsonRpcErrorCodes.InternalError, "init failed")
        }));
        await using var provider = new ProviderSut(_registry);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ConnectServerAsync("s1", transport, TestContext.Current.CancellationToken));

        // Client disposed the transport during cleanup.
        Assert.True(transport.DisposeCallCount >= 1);
        Assert.Empty(provider.GetServerStatuses());
    }

    [Fact]
    public async Task ConnectServerAsync_DuplicateWithTransport_Throws()
    {
        var provider = new ProviderSut(_registry);
        var transport = BuildToolDiscoveryTransport();
        await provider.ConnectServerAsync("dup", transport, TestContext.Current.CancellationToken);

        var second = BuildToolDiscoveryTransport();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ConnectServerAsync("dup", second, TestContext.Current.CancellationToken));
        Assert.Contains("already connected", ex.Message);

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task GetServerStatuses_ReflectsTransportConnectionState()
    {
        var provider = new ProviderSut(_registry);
        var transport = BuildToolDiscoveryTransport();
        await provider.ConnectServerAsync("live", transport, TestContext.Current.CancellationToken);

        var statuses = provider.GetServerStatuses();

        Assert.Single(statuses);
        Assert.Equal("live", statuses[0].ServerId);
        Assert.True(statuses[0].IsConnected);

        await provider.DisposeAsync();
    }

    private static MockMcpTransport BuildToolDiscoveryTransport()
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        transport.SetSendRequestFunc((req, _) =>
        {
            object result = req.Method switch
            {
                "initialize" => new McpInitializeResult
                {
                    ProtocolVersion = "2024-11-05",
                    Capabilities = new McpServerCapabilities
                    {
                        Tools = new McpCapabilityInfo { ListChanged = true }
                    },
                    ServerInfo = new McpServerInfo { Name = "t", Version = "1.0" }
                },
                "tools/list" => new McpToolListResult
                {
                    Tools = [new McpToolDefinition { Name = "do", Description = "d" }]
                },
                _ => new object()
            };
            var json = JsonSerializer.Serialize(result);
            return Task.FromResult(new JsonRpcResponse
            {
                Id = req.Id,
                Result = JsonElement.Parse(json)
            });
        });
        return transport;
    }
}
