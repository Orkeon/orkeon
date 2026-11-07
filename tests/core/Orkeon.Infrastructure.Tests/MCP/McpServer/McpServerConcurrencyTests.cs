using System.Text.Json;
using Orkeon.Application.Services.Security;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// FWK-18: the stdio loop runs the requests of a client at once, up to <c>MaxConcurrency</c>, and
/// writes the answers one line at a time. No test here depends on a duration: the tool is a gate,
/// or a barrier that opens only when the calls overlap.
/// </summary>
public sealed class McpServerConcurrencyTests
{
    [Fact]
    public async Task ShouldOverlapRequests_WhenTwoToolCallsAreSlow()
    {
        var tool = new MockGateTool("slow", releaseWhenEntered: 2);
        await using var harness = new McpServerStdioHarness(new McpServerOptions(), tool);

        harness.Reader.Send(McpServerStdioHarness.Call(1, "slow"));
        harness.Reader.Send(McpServerStdioHarness.Call(2, "slow"));
        await harness.EventuallyAsync(() => harness.Writer.Lines.Count == 2);
        await harness.FinishAsync();

        Assert.Equal(2, tool.MaxInFlight);
        Assert.Equal(["1", "2"], harness.AnswerIds().Order().ToList());
    }

    [Fact]
    public async Task ShouldRunFourRequestsAtOnceAndMakeTheFifthWait_WhenTheDefaultBoundApplies()
    {
        var tool = new MockGateTool("slow");
        await using var harness = new McpServerStdioHarness(new McpServerOptions(), tool);

        for (var id = 1; id <= 5; id++)
            harness.Reader.Send(McpServerStdioHarness.Call(id, "slow"));
        await harness.EventuallyAsync(() => tool.Entered == 4);
        tool.Release();
        await harness.EventuallyAsync(() => harness.Writer.Lines.Count == 5);
        await harness.FinishAsync();

        Assert.Equal(5, tool.Entered);
        Assert.Equal(4, tool.MaxInFlight);
    }

    [Fact]
    public async Task ShouldHonourTheConfiguredBound_WhenMaxConcurrencyIsTwo()
    {
        var tool = new MockGateTool("slow");
        await using var harness = new McpServerStdioHarness(new McpServerOptions { MaxConcurrency = 2 }, tool);

        for (var id = 1; id <= 3; id++)
            harness.Reader.Send(McpServerStdioHarness.Call(id, "slow"));
        await harness.EventuallyAsync(() => tool.Entered == 2);
        tool.Release();
        await harness.EventuallyAsync(() => harness.Writer.Lines.Count == 3);
        await harness.FinishAsync();

        Assert.Equal(3, tool.Entered);
        Assert.Equal(2, tool.MaxInFlight);
    }

    [Fact]
    public async Task ShouldNotInterleaveAnswers_WhenFourRequestsFinishTogether()
    {
        var tool = new MockGateTool("barrier", releaseWhenEntered: 4);
        await using var harness = new McpServerStdioHarness(new McpServerOptions(), tool);

        for (var id = 1; id <= 4; id++)
            harness.Reader.Send(McpServerStdioHarness.Call(id, "barrier"));
        await harness.EventuallyAsync(() => harness.Writer.Lines.Count == 4);
        await harness.FinishAsync();

        Assert.Equal(1, harness.Writer.MaxConcurrentWrites);
        Assert.All(harness.Writer.Lines, line =>
            Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(line).RootElement.ValueKind));
        Assert.Equal(["1", "2", "3", "4"], harness.AnswerIds().Order().ToList());
    }

    [Fact]
    public async Task ShouldAnswerEveryInFlightRequest_WhenStdinEndsBeforeTheyFinish()
    {
        var tool = new MockGateTool("barrier", releaseWhenEntered: 2);
        await using var harness = new McpServerStdioHarness(new McpServerOptions(), tool);

        harness.Reader.Send(McpServerStdioHarness.Call(1, "barrier"));
        harness.Reader.Send(McpServerStdioHarness.Call(2, "barrier"));
        await harness.FinishAsync();

        Assert.Equal(["1", "2"], harness.AnswerIds().Order().ToList());
    }

    [Fact]
    public void ShouldRefuseTheServer_WhenMaxConcurrencyIsBelowOne()
    {
        var registry = new MockToolRegistry();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new McpServer(registry, ToolInvocationPipeline.Unguarded, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(), new McpServerOptions { MaxConcurrency = 0 }));

        Assert.Contains("MaxConcurrency", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheSecondRequest_WhenAnIdIsAlreadyInFlight()
    {
        var tool = new MockGateTool("slow");
        await using var harness = new McpServerStdioHarness(new McpServerOptions(), tool);

        harness.Reader.Send(McpServerStdioHarness.Call(1, "slow"));
        await harness.EventuallyAsync(() => tool.Entered == 1);
        harness.Reader.Send(McpServerStdioHarness.Call(1, "slow"));
        await harness.EventuallyAsync(() => harness.Writer.Lines.Count == 1);
        tool.Release();
        await harness.EventuallyAsync(() => harness.Writer.Lines.Count == 2);
        await harness.FinishAsync();

        using var refused = JsonDocument.Parse(harness.Writer.Lines[0]);
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, refused.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        using var answered = JsonDocument.Parse(harness.Writer.Lines[1]);
        Assert.True(answered.RootElement.TryGetProperty("result", out _), harness.Writer.Lines[1]);
        Assert.Equal(1, tool.Entered);
    }

    [Fact]
    public async Task ShouldAcceptTheSameId_WhenTheClientReusesItAsSoonAsItSawTheAnswer()
    {
        await using var harness = new McpServerStdioHarness(new McpServerOptions());

        for (var round = 1; round <= 300; round++)
        {
            harness.Reader.Send(McpServerStdioHarness.Ping(1));
            var expected = round;
            await harness.EventuallyAsync(() => harness.Writer.Lines.Count == expected);
        }

        await harness.FinishAsync();
        Assert.All(harness.Writer.Lines, line =>
        {
            using var answer = JsonDocument.Parse(line);
            Assert.True(
                !answer.RootElement.TryGetProperty("error", out var error) || error.ValueKind == JsonValueKind.Null,
                line);
        });
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025", Justification = "The test awaits the run before the reader and the writer are disposed.")]
    public async Task ShouldStopServingAndSurfaceTheFault_WhenTheWriterFails()
    {
        var registry = new MockToolRegistry();
        var server = new McpServer(registry, ToolInvocationPipeline.Unguarded, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(), new McpServerOptions());
        using var reader = new FakeLineReader();
        using var writer = new ThrowingLineWriter();
        var run = server.RunStdioAsync(reader, writer, TestContext.Current.CancellationToken);

        reader.Send(McpServerStdioHarness.Ping(1));
        await McpServerStdioHarness.WaitAsync(() => run.IsCompleted, () => "the server kept serving after its writer failed", "run.IsCompleted");

        await Assert.ThrowsAsync<IOException>(() => run);
    }

    private sealed class ThrowingLineWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("the pipe is closed");
    }
}
