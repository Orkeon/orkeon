using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-15: a task with <c>retry: {attempts: 3}</c> that fails twice and then succeeds runs three times in
/// each of the six process modes, and no more in Graph, whose own retry loop is gone. The crews are
/// loaded from YAML and run by <see cref="RunnerExecution.RunOneShotAsync"/>, the entry point of
/// <c>orkeon run</c>, so the decorator chain is the real one.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RetryAllModesTests
{
    private const string Retry3 = "    retry: { attempts: 3 }";

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    [InlineData("graph")]
    [InlineData("hierarchical")]
    [InlineData("autonomous")]
    [InlineData("consensual")]
    [Trait("BR", "FWK-15")]
    public async Task ShouldRunTheTaskThreeTimes_WhenItFailsTwiceThenSucceeds(string process)
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", process, secondExtra: Retry3);
        var service = new FakeAgentExecutionService();
        service.FailTimes["second"] = 2;
        var hook = new FakeCrewExecutionHook();

        var run = await harness.RunAsync(crew, service, hook: hook, ct: TestContext.Current.CancellationToken);

        Assert.True(run.Exit == 0, $"exit={run.Exit} stderr={run.Stderr}");
        Assert.Equal(3, service.Calls.Count(c => c == "second"));
        Assert.Equal(1, service.Calls.Count(c => c == "first"));
        Assert.Equal(1, service.Calls.Count(c => c == "third"));
        Assert.Equal(1, hook.Started.Count(s => s.Attempt == 2));
        Assert.Equal(1, hook.Started.Count(s => s.Attempt == 3));
        Assert.DoesNotContain(hook.Started, s => s.Attempt > 3);

        // The completion says which attempt ended the task: the retried one only is above 1.
        Assert.Equal(1, hook.Completed.Count(c => c.Attempt == 3));
        Assert.DoesNotContain(hook.Completed, c => c.Attempt == 2);
        Assert.Equal(2, hook.Completed.Count(c => c.Attempt == 1));
    }

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    [InlineData("graph")]
    [InlineData("hierarchical")]
    [InlineData("autonomous")]
    [InlineData("consensual")]
    [Trait("BR", "FWK-15")]
    public async Task ShouldRunTheDependantOnlyAfterItsDependencyRecoveredOnRetry(string process)
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", process, secondExtra: Retry3);
        var service = new FakeAgentExecutionService();
        service.FailTimes["second"] = 2;

        var run = await harness.RunAsync(crew, service, ct: TestContext.Current.CancellationToken);

        Assert.True(run.Exit == 0, $"exit={run.Exit} stderr={run.Stderr}");
        Assert.Equal(["first", "second", "second", "second", "third"], service.Calls);
        Assert.Contains("out:second", service.PreviousOutputsOf("third"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    [InlineData("graph")]
    [InlineData("hierarchical")]
    [InlineData("autonomous")]
    [InlineData("consensual")]
    [Trait("BR", "FWK-15")]
    public async Task ShouldRunTheTaskExactlyItsAttempts_WhenEveryAttemptFails(string process)
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", process, secondExtra: Retry3);
        var service = new FakeAgentExecutionService();
        service.FailOn.Add("second");

        var run = await harness.RunAsync(crew, service, ct: TestContext.Current.CancellationToken);

        Assert.NotEqual(0, run.Exit);
        Assert.Equal(3, service.Calls.Count(c => c == "second"));
    }

    [Theory]
    [InlineData("sequential")]
    [InlineData("graph")]
    [Trait("BR", "FWK-15")]
    public async Task ShouldRunAFailingTaskOnce_WhenItHasNoRetry(string process)
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", process);
        var service = new FakeAgentExecutionService();
        service.FailOn.Add("second");

        var run = await harness.RunAsync(crew, service, ct: TestContext.Current.CancellationToken);

        Assert.NotEqual(0, run.Exit);
        Assert.Equal(1, service.Calls.Count(c => c == "second"));
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async Task ShouldCheckpointTheTaskWithItsRetryCount()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential", secondExtra: Retry3);
        var service = new FakeAgentExecutionService();
        service.FailTimes["second"] = 2;

        var run = await harness.RunAsync(crew, service, ct: TestContext.Current.CancellationToken);

        Assert.True(run.Exit == 0, run.Stderr);
        var state = await harness.Store().GetLatestForCrewAsync(harness.CrewKeyOf(crew), TestContext.Current.CancellationToken);
        var counts = state!.TaskCheckpoints.ToDictionary(c => c.Key.Split('#')[0], c => c.Value.RetryCount);
        Assert.Equal(2, counts["second"]);
        Assert.Equal(0, counts["first"]);
    }
}
