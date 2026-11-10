using Orkeon.Application.Crew;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-07: <c>orkeon run --resume</c> continues a failed run instead of starting another: the tasks it
/// completed are answered from the checkpoint, in every process mode, and the others run. The runs go
/// through <see cref="RunnerExecution.RunOneShotAsync"/>, which loads the crew from its file each time,
/// so the key a task is checkpointed under has to survive a reload (the task ids do not).
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class ResumeSkipsCompletedTasksTests
{
    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    [InlineData("graph")]
    [InlineData("hierarchical")]
    [InlineData("autonomous")]
    [InlineData("consensual")]
    [Trait("BR", "FWK-07")]
    public async Task ShouldNotRunTheCompletedTasksAgain_WhenTheRunIsResumed(string process)
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", process);
        var clean = await harness.RunAsync(crew, new FakeAgentExecutionService(), ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, clean.Exit);
        var firstPass = new FakeAgentExecutionService();
        firstPass.FailOn.Add("second");

        var killed = await harness.RunAsync(crew, firstPass, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, killed.Exit);
        Assert.Contains("first", firstPass.Calls);
        var runId = ResumeRunHarness.ResumeIdIn(killed.Stderr);

        var secondPass = new FakeAgentExecutionService();
        var hook = new FakeCrewExecutionHook();
        var resumed = await harness.RunAsync(crew, secondPass, resume: runId, hook: hook, ct: TestContext.Current.CancellationToken);

        Assert.True(resumed.Exit == 0, $"exit={resumed.Exit} stderr={resumed.Stderr}");
        Assert.False(secondPass.Calls.Contains("first"), $"the second pass ran: {string.Join(", ", secondPass.Calls)}; first pass ran: {string.Join(", ", firstPass.Calls)}");
        Assert.Contains("second", secondPass.Calls);
        Assert.Contains("third", secondPass.Calls);
        Assert.Contains("out:first", secondPass.PreviousOutputsOf("second"), StringComparison.Ordinal);
        Assert.Contains("out:second", secondPass.PreviousOutputsOf("third"), StringComparison.Ordinal);

        // What the run reports: the tasks it took from the checkpoint say so, the others do not, and the
        // crew ends as an uninterrupted run does, with the same output.
        var completed = hook.Completed.Where(c => !c.Skipped).ToList();
        Assert.Equal(3, completed.Count);
        Assert.Equal(1, completed.Count(c => c.Resumed));
        Assert.All(completed.Where(c => c.Resumed), c => Assert.True(c.Success));
        Assert.Equal(CrewHookStatus.Completed, Assert.Single(hook.CrewCompleted).Status);
        Assert.Equal(Outcome(clean.Stdout), Outcome(resumed.Stdout));
    }

    // The crew output as printed, from its banner on (the host logs before it, with timestamps) and without the
    // timings that differ from one run to the next.
    private static string Outcome(string stdout)
    {
        var banner = stdout.IndexOf("=== Crew Output ===", StringComparison.Ordinal);
        Assert.True(banner >= 0, $"no crew output in: {stdout}");
        return string.Join(
            '\n',
            stdout[banner..].Split('\n').Select(l => l.TrimEnd('\r'))
                .Where(l => !l.Contains("ms", StringComparison.Ordinal) && !l.Contains("Duration", StringComparison.OrdinalIgnoreCase) && !l.Contains("Token", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldResumeTheLatestUnfinishedRun_WhenTheIdIsLatest()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var firstPass = new FakeAgentExecutionService();
        firstPass.FailOn.Add("third");
        await harness.RunAsync(crew, firstPass, ct: TestContext.Current.CancellationToken);

        var secondPass = new FakeAgentExecutionService();
        var resumed = await harness.RunAsync(crew, secondPass, resume: "latest", ct: TestContext.Current.CancellationToken);

        Assert.True(resumed.Exit == 0, $"exit={resumed.Exit} stderr={resumed.Stderr}");
        Assert.Equal(["third"], secondPass.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldResumeTheOlderFailedRun_WhenANewerRunCompleted()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var failing = new FakeAgentExecutionService();
        failing.FailOn.Add("third");
        await harness.RunAsync(crew, failing, ct: TestContext.Current.CancellationToken);
        var newer = await harness.RunAsync(crew, new FakeAgentExecutionService(), ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, newer.Exit);

        var service = new FakeAgentExecutionService();
        var resumed = await harness.RunAsync(crew, service, resume: "latest", ct: TestContext.Current.CancellationToken);

        Assert.True(resumed.Exit == 0, $"exit={resumed.Exit} stderr={resumed.Stderr}");
        Assert.Equal(["third"], service.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRefuseLatest_WhenTheLatestRunIsStillMarkedRunning_AndResumeItWhenNamed()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var failing = new FakeAgentExecutionService();
        failing.FailOn.Add("third");
        await harness.RunAsync(crew, failing, ct: TestContext.Current.CancellationToken);
        var orphan = await harness.SeedRunningSessionAsync(crew, TestContext.Current.CancellationToken);

        var refused = new FakeAgentExecutionService();
        var latest = await harness.RunAsync(crew, refused, resume: "latest", ct: TestContext.Current.CancellationToken);
        var named = new FakeAgentExecutionService();
        var byId = await harness.RunAsync(crew, named, resume: orphan, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, latest.Exit);
        Assert.Empty(refused.Calls);
        Assert.Contains("still marked Running", latest.Stderr, StringComparison.Ordinal);
        Assert.Contains($"--resume {orphan}", latest.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, byId.Exit);
        Assert.Equal(["first", "second", "third"], named.Calls.ToArray());
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRunAnEditedTaskAndTheOnesAfterIt_WhenTheCrewFileChangedBetweenTheRuns()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var failing = new FakeAgentExecutionService();
        failing.FailOn.Add("third");
        var killed = await harness.RunAsync(crew, failing, ct: TestContext.Current.CancellationToken);
        var runId = ResumeRunHarness.ResumeIdIn(killed.Stderr);
        await File.WriteAllTextAsync(crew, (await File.ReadAllTextAsync(crew, TestContext.Current.CancellationToken)).Replace("Second.", "Second, reworded.", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        var service = new FakeAgentExecutionService();
        var resumed = await harness.RunAsync(crew, service, resume: runId, ct: TestContext.Current.CancellationToken);

        Assert.True(resumed.Exit == 0, $"exit={resumed.Exit} stderr={resumed.Stderr}");
        Assert.DoesNotContain("first", service.Calls);
        Assert.Contains("second", service.Calls);
        Assert.Contains("third", service.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRunEveryTaskAfterAnEditedOne_EvenWhenTheyAreUnchanged()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var failing = new FakeAgentExecutionService();
        failing.FailOn.Add("third");
        var runId = ResumeRunHarness.ResumeIdIn((await harness.RunAsync(crew, failing, ct: TestContext.Current.CancellationToken)).Stderr);
        // Only the first task is edited: the second and third are as they were, but read its output.
        await File.WriteAllTextAsync(crew, (await File.ReadAllTextAsync(crew, TestContext.Current.CancellationToken)).Replace("First.", "First, reworded.", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        var service = new FakeAgentExecutionService();
        var resumed = await harness.RunAsync(crew, service, resume: runId, ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, resumed.Exit);
        Assert.Equal(["first", "second", "third"], service.Calls.ToArray());
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldNotResumeARunThatCompleted()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var done = await harness.RunAsync(crew, new FakeAgentExecutionService(), ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, done.Exit);

        var service = new FakeAgentExecutionService();
        var resumed = await harness.RunAsync(crew, service, resume: "latest", ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, resumed.Exit);
        Assert.Empty(service.Calls);
        Assert.Contains("nothing to resume", resumed.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldKeepTheRunId_SoAResumedRunIsTheSameRunAndCanBeResumedAgain()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var pass1 = new FakeAgentExecutionService();
        pass1.FailOn.Add("second");
        var runId = ResumeRunHarness.ResumeIdIn((await harness.RunAsync(crew, pass1, ct: TestContext.Current.CancellationToken)).Stderr);

        var pass2 = new FakeAgentExecutionService();
        pass2.FailOn.Add("third");
        var second = await harness.RunAsync(crew, pass2, resume: runId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(runId, ResumeRunHarness.ResumeIdIn(second.Stderr));

        var pass3 = new FakeAgentExecutionService();
        var third = await harness.RunAsync(crew, pass3, resume: runId, ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, third.Exit);
        Assert.Equal(["third"], pass3.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ShouldRefuseAnUnknownRunId()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential");
        var service = new FakeAgentExecutionService();

        var result = await harness.RunAsync(crew, service, resume: "01ARZ3NDEKTSV4RRFFQ69G5FAV", ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Exit);
        Assert.Empty(service.Calls);
        Assert.Contains("01ARZ3NDEKTSV4RRFFQ69G5FAV", result.Stderr, StringComparison.Ordinal);
    }
}
