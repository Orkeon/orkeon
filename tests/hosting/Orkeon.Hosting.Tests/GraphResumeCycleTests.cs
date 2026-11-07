using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-07: a graph run goes through a task more than once when it retries it, so a task is
/// checkpointed under its key and the ordinal of the invocation (<c>second#1</c>, <c>second#2</c>):
/// the retry of a task never takes the place of the run it repeats, and a resumed run does not mistake
/// the one for the other.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class GraphResumeCycleTests
{
    private const string Retrying = "graphConfig:\n  maxRetryCycles: 2\n";

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldCheckpointEachInvocationOfARetriedTaskUnderItsOrdinal()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "graph", extra: Retrying);
        var service = new FakeAgentExecutionService();
        service.FailTimes["second"] = 1;

        var run = await harness.RunAsync(crew, service, ct: TestContext.Current.CancellationToken);

        Assert.True(run.Exit == 0, $"exit={run.Exit} stderr={run.Stderr}");
        Assert.Equal(2, service.Calls.Count(c => c == "second"));
        var state = await StateOfLatestRunAsync(harness);
        Assert.Equal(CheckpointStatus.Completed, state.TaskCheckpoints["first#1"].Status);
        Assert.Equal(CheckpointStatus.Completed, state.TaskCheckpoints["second#2"].Status);
        Assert.NotEqual(CheckpointStatus.Completed, state.TaskCheckpoints.GetValueOrDefault("second#1")?.Status ?? CheckpointStatus.Pending);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRunALaterInvocationLive_WhenOnlyTheEarlierOnesWereCompleted()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "graph", extra: Retrying);
        var killed = new FakeAgentExecutionService();
        killed.FailTimes["second"] = 1;
        killed.FailOn.Add("third");
        var first = await harness.RunAsync(crew, killed, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, first.Exit);

        var resumedService = new FakeAgentExecutionService();
        var resumed = await harness.RunAsync(crew, resumedService, resume: "latest", ct: TestContext.Current.CancellationToken);

        Assert.True(resumed.Exit == 0, $"exit={resumed.Exit} stderr={resumed.Stderr}");
        Assert.DoesNotContain("first", resumedService.Calls);
        Assert.Contains("second", resumedService.Calls);
        Assert.Contains("third", resumedService.Calls);
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
