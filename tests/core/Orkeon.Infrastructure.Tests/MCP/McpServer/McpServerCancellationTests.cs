using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// FWK-18: <c>notifications/cancelled</c> cancels the request it names, running or still waiting
/// for its turn, and a cancelled request is never answered (MCP cancellation).
/// </summary>
public sealed class McpServerCancellationTests
{
    [Fact]
    public async Task ShouldCancelTheRunningCallAndSendNoAnswer_WhenTheClientCancelsIt()
    {
        var tool = new MockGateTool("slow");
        await using var harness = new McpServerStdioHarness(new McpServerOptions(), tool);

        harness.Reader.Send(McpServerStdioHarness.Call(7, "slow"));
        await harness.EventuallyAsync(() => tool.Entered == 1);
        harness.Reader.Send(McpServerStdioHarness.Cancel(999));
        harness.Reader.Send(McpServerStdioHarness.Cancel(7));
        await harness.EventuallyAsync(() => tool.Cancelled == 1);
        harness.Reader.Send(McpServerStdioHarness.Ping(8));
        await harness.EventuallyAsync(() => harness.Writer.Lines.Count >= 1);
        await harness.FinishAsync();

        Assert.Equal(["8"], harness.AnswerIds());
    }

    [Fact]
    public async Task ShouldNeverStartTheCall_WhenTheClientCancelsARequestStillWaitingForItsTurn()
    {
        var tool = new MockGateTool("slow");
        await using var harness = new McpServerStdioHarness(new McpServerOptions { MaxConcurrency = 1 }, tool);

        harness.Reader.Send(McpServerStdioHarness.Call(1, "slow"));
        await harness.EventuallyAsync(() => tool.Entered == 1);
        harness.Reader.Send(McpServerStdioHarness.Call(2, "slow"));
        harness.Reader.Send(McpServerStdioHarness.Cancel(2));
        harness.Reader.Send(McpServerStdioHarness.Call(3, "slow"));
        harness.Reader.Send(McpServerStdioHarness.Cancel("unknown"));
        // The server has read the cancel of 2 before the first call may end and free its turn.
        await harness.EventuallyAsync(() => harness.Reader.Pending == 0);
        tool.Release();
        await harness.EventuallyAsync(() => harness.Writer.Lines.Count == 2);
        await harness.FinishAsync();

        Assert.Equal(["1", "3"], harness.AnswerIds().Order().ToList());
        Assert.Equal(2, tool.Entered);
    }

    [Fact]
    public async Task ShouldMatchTheRequestIdByItsJsonType_WhenTheIdIsAString()
    {
        var tool = new MockGateTool("slow");
        await using var harness = new McpServerStdioHarness(new McpServerOptions(), tool);

        harness.Reader.Send(McpServerStdioHarness.Call("abc", "slow"));
        harness.Reader.Send(McpServerStdioHarness.Call(5, "slow"));
        await harness.EventuallyAsync(() => tool.Entered == 2);
        harness.Reader.Send(McpServerStdioHarness.Cancel("abc"));
        await harness.EventuallyAsync(() => tool.Cancelled == 1);
        tool.Release();
        await harness.EventuallyAsync(() => harness.Writer.Lines.Count == 1);
        await harness.FinishAsync();

        Assert.Equal(["5"], harness.AnswerIds());
    }
}
