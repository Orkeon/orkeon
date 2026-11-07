using System.Diagnostics.CodeAnalysis;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-03: a call that outlives CallTimeout ends as a typed tool error.</summary>
public sealed class McpCallTimeoutTests
{
    [SuppressMessage("Reliability", "CA2000", Justification = "Ownership of the transport passes to the client or provider, which the test disposes.")]
    private static async Task<McpToolAdapter> SilentToolAsync(TimeSpan timeout)
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        transport.SetSendRequestFunc(async (request, ct) =>
        {
            if (request.Method == "tools/call")
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return ToolServer.Respond(request, ["slow"]);
        });
        var client = new McpClient(transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        return new McpToolAdapter(new McpToolDefinition { Name = "slow" }, client, callTimeout: timeout);
    }

    [Fact]
    public async Task ShouldEndAsTypedToolError_WhenTheServerStaysSilent()
    {
        var adapter = await SilentToolAsync(TimeSpan.FromMilliseconds(50));

        var response = await adapter.CallAsync(new ToolCallRequest("slow", []), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("slow", response.Error, StringComparison.Ordinal);
        Assert.Equal(nameof(McpCallTimeoutException), response.Metadata!["error_type"]);
    }

    [Fact]
    public async Task ShouldPropagateCancellation_WhenTheCallerCancels()
    {
        var adapter = await SilentToolAsync(TimeSpan.FromMinutes(5));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.CallAsync(new ToolCallRequest("slow", []), cts.Token));
    }
}
