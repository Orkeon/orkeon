using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Tools;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>RT-07: the scheduler that runs the read calls of one model turn together, between barriers.</summary>
public class ToolCallSchedulerTests
{
    private static bool IsRead(string call) => call.StartsWith('r');

    private static readonly string[] ReleaseOrder = ["r3", "r2", "r1"];
    private static readonly string[] HoldNames = ["r1", "r2", "r3"];

    [Fact]
    [Trait("BR", "RT-07")]
    public void ShouldGroupReadsBetweenBarriers_WhenTurnIsReadWriteRead()
    {
        var groups = ToolCallScheduler.Partition(["r1", "r2", "w1", "w2", "r3"], IsRead);

        Assert.Equal(4, groups.Count);
        Assert.True(groups[0].IsParallel);
        Assert.Equal(["r1", "r2"], groups[0].Calls);
        Assert.False(groups[1].IsParallel);
        Assert.Equal(["w1"], groups[1].Calls);
        Assert.False(groups[2].IsParallel);
        Assert.Equal(["w2"], groups[2].Calls);
        Assert.True(groups[3].IsParallel);
        Assert.Equal(["r3"], groups[3].Calls);
    }

    [Fact]
    [Trait("BR", "RT-07")]
    public async System.Threading.Tasks.Task ShouldHoldReadAfterWriteUntilWriteEnds_WhenTurnIsReadWriteRead()
    {
        var probe = new ConcurrencyProbe(expected: 2);

        await ToolCallScheduler.RunAsync<string, string>(
            ["r1", "r2", "w1", "r3"], IsRead,
            async (call, ct) =>
            {
                probe.Log($"start:{call}");
                if (call is "r1" or "r2") await probe.EnterAsync();
                await System.Threading.Tasks.Task.Yield();
                probe.Log($"end:{call}");
                return call;
            },
            maxParallel: 4, TestContext.Current.CancellationToken);

        var events = probe.Events();
        Assert.Equal(2, probe.Max);
        Assert.True(events.IndexOf("end:r1") < events.IndexOf("start:w1"));
        Assert.True(events.IndexOf("end:r2") < events.IndexOf("start:w1"));
        Assert.True(events.IndexOf("end:w1") < events.IndexOf("start:r3"));
    }

    [Fact]
    [Trait("BR", "RT-07")]
    public async System.Threading.Tasks.Task ShouldReturnOutcomesInCallOrder_WhenReadsCompleteOutOfOrder()
    {
        var probe = new ConcurrencyProbe(expected: 3);
        var holds = HoldNames.ToDictionary(c => c, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

        var run = ToolCallScheduler.RunAsync<string, string>(
            ["r1", "r2", "r3"], IsRead,
            async (call, ct) =>
            {
                await probe.EnterAsync();
                await holds[call].Task.WaitAsync(ct);
                return $"out:{call}";
            },
            maxParallel: 4, TestContext.Current.CancellationToken);

        try
        {
            Assert.Same(probe.AllEntered, await System.Threading.Tasks.Task.WhenAny(probe.AllEntered, System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)));
            foreach (var call in ReleaseOrder)
            {
                holds[call].TrySetResult(true);
                await System.Threading.Tasks.Task.Yield();
            }
        }
        finally
        {
            foreach (var hold in holds.Values) hold.TrySetResult(true);
        }

        Assert.Equal(["out:r1", "out:r2", "out:r3"], await run);
    }

    [Fact]
    [Trait("BR", "RT-07")]
    public async System.Threading.Tasks.Task ShouldCompleteSiblingsAndRethrowFirstFault_WhenOneReadFaults()
    {
        var bFaulted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new List<string>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ToolCallScheduler.RunAsync<string, string>(
            ["r1", "r2", "r3"], IsRead,
            async (call, ct) =>
            {
                if (call == "r2")
                {
                    bFaulted.TrySetResult(true);
                    throw new InvalidOperationException("r2 failed");
                }

                await bFaulted.Task.WaitAsync(ct);
                await System.Threading.Tasks.Task.Yield();
                if (call == "r3") throw new InvalidOperationException("r3 failed");
                lock (completed) completed.Add(call);
                return call;
            },
            maxParallel: 4, TestContext.Current.CancellationToken));

        Assert.Equal("r2 failed", ex.Message);
        Assert.Equal(["r1"], completed);
    }

    [Fact]
    [Trait("BR", "RT-07")]
    public async System.Threading.Tasks.Task ShouldCapFanOutAtBound_WhenMoreReadsThanMaxParallel()
    {
        var probe = new ConcurrencyProbe(expected: 2);

        var outcomes = await ToolCallScheduler.RunAsync<string, string>(
            ["r1", "r2", "r3", "r4", "r5", "r6"], IsRead,
            async (call, ct) =>
            {
                await probe.EnterAsync();
                await System.Threading.Tasks.Task.Delay(20, ct);
                probe.Leave();
                return call;
            },
            maxParallel: 2, TestContext.Current.CancellationToken);

        Assert.Equal(["r1", "r2", "r3", "r4", "r5", "r6"], outcomes);
        Assert.Equal(2, probe.Max);
    }

    [Theory]
    [Trait("BR", "RT-07")]
    [InlineData("file_read", ToolAccess.Read, true)]
    [InlineData("directory_read", ToolAccess.Read, true)]
    [InlineData("directory_search", ToolAccess.Read, true)]
    [InlineData("count_pattern", ToolAccess.Read, true)]
    [InlineData("glob", ToolAccess.Read, true)]
    [InlineData("grep", ToolAccess.Read, true)]
    [InlineData("rag_search", ToolAccess.Read, true)]
    [InlineData("codebase_search", ToolAccess.Read, true)]
    [InlineData("symbol_references", ToolAccess.Read, true)]
    [InlineData("index_status", ToolAccess.Read, true)]
    [InlineData("node_outline", ToolAccess.Read, true)]
    [InlineData("node_read", ToolAccess.Read, true)]
    [InlineData("session_stats", ToolAccess.Read, false)]
    [InlineData("shell_command", ToolAccess.Read, false)]
    [InlineData("shell_command", ToolAccess.Execute, false)]
    [InlineData("file_write", ToolAccess.Edit, false)]
    [InlineData("file_read", ToolAccess.Unspecified, false)]
    [InlineData("codebase_index", ToolAccess.Edit, false)]
    public void ShouldTreatOnlyAllowListedReadToolsAsParallel_WhenAccessIsRead(string name, ToolAccess access, bool expected) =>
        Assert.Equal(expected, ToolCallScheduler.IsParallelRead(name, access));
}
