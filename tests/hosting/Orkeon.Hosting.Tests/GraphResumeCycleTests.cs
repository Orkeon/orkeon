using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-07 and FWK-15: a graph run checkpoints a task under its key and the ordinal of the invocation
/// (<c>second#1</c>). The graph no longer runs a task twice by itself - a task's attempts are its
/// <c>retry:</c>, inside one invocation -, so the ordinal stays 1 and the checkpoint is that of the
/// attempt that succeeded; a resumed run takes up after it.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class GraphResumeCycleTests
{
    private const string Retrying = "    retry: { attempts: 2 }";

    [Fact]
    [Trait("BR", "FWK-07")]
    [Trait("BR", "FWK-15")]
    public async Task ShouldCheckpointARetriedTaskOnceUnderItsOrdinal_WithItsRetryCount()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "graph", secondExtra: Retrying);
        var service = new FakeAgentExecutionService();
        service.FailTimes["second"] = 1;

        var run = await harness.RunAsync(crew, service, ct: TestContext.Current.CancellationToken);

        Assert.True(run.Exit == 0, $"exit={run.Exit} stderr={run.Stderr}");
        Assert.Equal(2, service.Calls.Count(c => c == "second"));
        var state = await StateOfLatestRunAsync(harness);
        Assert.Equal(CheckpointStatus.Completed, state.TaskCheckpoints["first#1"].Status);
        Assert.Equal(CheckpointStatus.Completed, state.TaskCheckpoints["second#1"].Status);
        Assert.Equal(1, state.TaskCheckpoints["second#1"].RetryCount);
        Assert.DoesNotContain("second#2", state.TaskCheckpoints.Keys);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldTakeUpAfterARetriedTask_WhenTheRunIsResumed()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "graph", secondExtra: Retrying);
        var killed = new FakeAgentExecutionService();
        killed.FailTimes["second"] = 1;
        killed.FailOn.Add("third");
        var first = await harness.RunAsync(crew, killed, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, first.Exit);

        var resumedService = new FakeAgentExecutionService();
        var resumed = await harness.RunAsync(crew, resumedService, resume: "latest", ct: TestContext.Current.CancellationToken);

        Assert.True(resumed.Exit == 0, $"exit={resumed.Exit} stderr={resumed.Stderr}");
        Assert.DoesNotContain("first", resumedService.Calls);
        Assert.DoesNotContain("second", resumedService.Calls);
        Assert.Equal(["third"], resumedService.Calls);
    }

    private static async Task<SessionState> StateOfLatestRunAsync(ResumeRunHarness harness)
    {
        var store = harness.Store();
        var latest = (await store.ListAsync(TestContext.Current.CancellationToken))
            .Where(s => s.CrewKey.StartsWith(harness.CrewName + "@", StringComparison.Ordinal))
            .OrderByDescending(s => s.UpdatedAt)
            .First();
        return (await store.GetAsync(latest.SessionId, TestContext.Current.CancellationToken))!;
    }
}
