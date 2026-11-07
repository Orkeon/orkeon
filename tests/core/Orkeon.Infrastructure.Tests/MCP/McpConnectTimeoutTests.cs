using System.Diagnostics.CodeAnalysis;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-03: the handshake and the tool listing are bounded by ConnectTimeout, whoever connects.</summary>
public sealed class McpConnectTimeoutTests
{
    [SuppressMessage("Reliability", "CA2000", Justification = "Ownership of the transport passes to the provider, which disposes it on failure.")]
    private static MockMcpTransport SilentServer()
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        transport.SetSendRequestFunc(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new JsonRpcResponse();
        });
        return transport;
    }

    [Fact]
    public async Task ShouldFailNamingTheKey_WhenTheServerNeverAnswersTheHandshake()
    {
        await using var provider = new McpToolProvider(new MockToolRegistry());
        var config = new McpServerConfig { ConnectTimeout = TimeSpan.FromMilliseconds(50) };

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => provider.ConnectServerAsync("srv", SilentServer(), config, TestContext.Current.CancellationToken));

        Assert.Contains("MCP:Servers:srv:ConnectTimeout", ex.Message, StringComparison.Ordinal);
        Assert.Empty(provider.GetServerStatuses());
    }

    [Fact]
    public async Task ShouldPropagateCancellation_WhenTheCallerCancelsTheConnection()
    {
        await using var provider = new McpToolProvider(new MockToolRegistry());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ConnectServerAsync("srv", SilentServer(), new McpServerConfig(), cts.Token));
    }
}
