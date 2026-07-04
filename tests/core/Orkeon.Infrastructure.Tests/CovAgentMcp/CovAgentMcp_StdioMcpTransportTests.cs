using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.MCP;
using StdioSut = Orkeon.Infrastructure.MCP.StdioMcpTransport;
using SysProcess = System.Diagnostics.Process;

namespace Orkeon.Infrastructure.Tests.CovAgentMcp;

/// <summary>
/// Coverage tests for <see cref="StdioMcpTransport"/> paths that do not require
/// launching a real subprocess (argument/state validation, dispose-before-connect).
/// </summary>
public sealed class CovAgentMcp_StdioMcpTransportTests
{
    [Fact]
    public void Constructor_WithNullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new StdioSut((McpServerConfig)null!));
    }

    [Fact]
    public void Constructor_WithNullProcess_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new StdioSut((SysProcess)null!));
    }

    [Fact]
    public async Task NewTransport_IsNotConnected()
    {
        await using var transport = new StdioSut(new McpServerConfig { Command = "noop" });
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task SendRequestAsync_WhenNotConnected_ThrowsInvalidOperation()
    {
        var transport = new StdioSut(new McpServerConfig { Command = "noop" });
        var request = new JsonRpcRequest { Method = "test", Id = 1 };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.SendRequestAsync(request, TestContext.Current.CancellationToken));
        Assert.Contains("not connected", ex.Message);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WithoutConnect_DoesNotThrow()
    {
        var transport = new StdioSut(
            new McpServerConfig { Command = "noop" }, NullLogger<StdioSut>.Instance);

        await transport.DisposeAsync();

        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotent()
    {
        var transport = new StdioSut(new McpServerConfig { Command = "noop" });

        await transport.DisposeAsync();
        await transport.DisposeAsync();

        Assert.False(transport.IsConnected);
    }
}
