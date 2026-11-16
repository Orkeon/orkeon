using System.Text.Json;
using Orkeon.Application.Crew;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>Bench that feeds the run observer the completions of its trial, as the crew's hook would.</summary>
internal sealed class FakeFeedingTestBench(ForgeRunObserver observer, IReadOnlyList<TaskExecutionSnapshot> completions) : IForgeTestBench
{
    /// <inheritdoc />
    public async Task<ForgeTestRun> ExecuteAsync(ForgeSession session, int runNumber, CancellationToken cancellationToken)
    {
        foreach (var snapshot in completions)
            await observer.OnTaskCompletedAsync(snapshot, cancellationToken);

        return new ForgeTestRun { Run = runNumber, Success = true, Output = "ok", TaskCount = completions.Count, DurationMs = 40 };
    }
}

/// <summary>Bench that feeds the observer, then crashes with a non-cancellation exception.</summary>
internal sealed class FakeCrashingTestBench(ForgeRunObserver observer, IReadOnlyList<TaskExecutionSnapshot> completions) : IForgeTestBench
{
    /// <inheritdoc />
    public async Task<ForgeTestRun> ExecuteAsync(ForgeSession session, int runNumber, CancellationToken cancellationToken)
    {
        foreach (var snapshot in completions)
            await observer.OnTaskCompletedAsync(snapshot, cancellationToken);

        throw new InvalidOperationException("bench exploded");
    }
}

/// <summary>CH-43: the <c>run.finished</c> line of the forge trial carries <c>cappedTasks</c>.</summary>
public sealed class ForgeRunStagesCappedTasksTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-capped-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private static TaskExecutionSnapshot Completed(string taskId, int attempt, bool capped, bool missing = false, bool success = true) => new()
    {
        TaskId = taskId,
        AgentRole = "Helper",
        Success = success,
        Duration = TimeSpan.FromMilliseconds(5),
        CompletedAt = DateTimeOffset.UnixEpoch,
        Attempt = attempt,
        EndedOnIterationCap = capped,
        DeliverableMissing = missing,
    };

    private async Task<JsonElement> RunFinishedAsync(string slug, params TaskExecutionSnapshot[] completions)
    {
        using var output = new StringWriter();
        var session = ForgeSession.Create(_workspace, slug);
        var observer = new ForgeRunObserver();
        var assistant = new ScriptedAssistant()
            .SubmitsBrief(ForgeDocuments.ValidBrief)
            .SubmitsBlueprint(ForgeDocuments.ValidBlueprint);
        var channel = new ScriptedUserChannel().Decides("accept");
        IForgeStageRunner[] runners =
        [
            new BriefStage(assistant, channel, null),
            new BlueprintStage(assistant),
            new RenderStage(),
            new ValidateStage(ForgeDocuments.KnownTools),
            new TestStage(new FakeFeedingTestBench(observer, completions), observer),
            new DiagnoseStage(new FakeJudge().Approves()),
            new VerdictStage(false, channel, ForgeDocuments.KnownTools),
        ];
        var clock = new FakeOrkeonClock();

        await new ForgeEngine(session, new ForgeEventWriter(output, clock), runners, clock)
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        return output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonElement.Parse(l))
            .Single(e => e.GetProperty("kind").GetString() == "run.finished");
    }

    [Fact]
    [Trait("BR", "CH-43")]
    public async Task ShouldCountTheCappedTasks_InTheForgeTrialRunFinished()
    {
        // a: capped then recovered (not counted); b: fine then capped (counted); c: capped (counted).
        var finished = await RunFinishedAsync(
            "capped",
            Completed("a", 1, capped: true),
            Completed("a", 2, capped: false),
            Completed("b", 1, capped: false),
            Completed("b", 2, capped: true),
            Completed("c", 1, capped: true));

        Assert.True(finished.TryGetProperty("cappedTasks", out var capped));
        Assert.Equal(2, capped.GetInt32());

        var none = await RunFinishedAsync("plain", Completed("a", 1, capped: false));
        Assert.True(none.TryGetProperty("cappedTasks", out var zero));
        Assert.Equal(0, zero.GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-49")]
    public async Task ShouldWriteRunFinished_WhenTheTrialBenchCrashes()
    {
        using var output = new StringWriter();
        var session = ForgeSession.Create(_workspace, "crash");
        var observer = new ForgeRunObserver();
        var events = new ForgeEventWriter(output, new FakeOrkeonClock());
        var stage = new TestStage(
            new FakeCrashingTestBench(observer, [Completed("a", 1, capped: true), Completed("b", 1, capped: true)]),
            observer);

        var outcome = await stage.RunAsync(session, events, TestContext.Current.CancellationToken);

        Assert.Equal(ForgeTrigger.Fail, outcome.Trigger);
        Assert.Equal("The test bench crashed: bench exploded", outcome.Detail);
        var finished = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonElement.Parse(l))
            .Single(e => e.GetProperty("kind").GetString() == "run.finished");
        Assert.False(finished.GetProperty("success").GetBoolean());
        Assert.Equal(2, finished.GetProperty("cappedTasks").GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async Task ShouldCountTheMissingDeliverables_WhenTheTrialBenchCrashes()
    {
        using var output = new StringWriter();
        var session = ForgeSession.Create(_workspace, "crash-missing");
        var observer = new ForgeRunObserver();
        var events = new ForgeEventWriter(output, new FakeOrkeonClock());
        var stage = new TestStage(
            new FakeCrashingTestBench(observer, [Completed("a", 1, capped: false, missing: true), Completed("b", 1, capped: false)]),
            observer);

        await stage.RunAsync(session, events, TestContext.Current.CancellationToken);

        var finished = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonElement.Parse(l))
            .Single(e => e.GetProperty("kind").GetString() == "run.finished");
        Assert.Equal(1, finished.GetProperty("missingDeliverables").GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async Task ShouldNotCountTheMissingDeliverable_WhenTheTasksLastAttemptFailed()
    {
        var finished = await RunFinishedAsync(
            "failed-missing",
            Completed("a", 1, capped: false, missing: true),
            Completed("a", 2, capped: false, missing: true, success: false));

        Assert.Equal(0, finished.GetProperty("missingDeliverables").GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-43")]
    public async Task ShouldScopeTheCappedTasksToTheTrial_WhenTwoTrialsShareTheObserver()
    {
        using var output = new StringWriter();
        var session = ForgeSession.Create(_workspace, "two-trials");
        var observer = new ForgeRunObserver();
        var clock = new FakeOrkeonClock();
        var events = new ForgeEventWriter(output, clock);
        var first = new TestStage(
            new FakeFeedingTestBench(observer, [Completed("a", 1, capped: true)]), observer);
        var second = new TestStage(
            new FakeFeedingTestBench(observer, [Completed("b", 1, capped: false)]), observer);

        await first.RunAsync(session, events, TestContext.Current.CancellationToken);
        await second.RunAsync(session, events, TestContext.Current.CancellationToken);

        var finished = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonElement.Parse(l))
            .Where(e => e.GetProperty("kind").GetString() == "run.finished")
            .ToList();
        Assert.Equal(2, finished.Count);
        Assert.Equal(1, finished[0].GetProperty("cappedTasks").GetInt32());
        Assert.Equal(0, finished[1].GetProperty("cappedTasks").GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async Task ShouldCountTheMissingDeliverables_InTheForgeTrialRunFinished()
    {
        // a: missing then written (not counted); b: written then missing (counted); c: missing (counted).
        var finished = await RunFinishedAsync(
            "missing",
            Completed("a", 1, capped: false, missing: true),
            Completed("a", 2, capped: false, missing: false),
            Completed("b", 1, capped: false, missing: false),
            Completed("b", 2, capped: false, missing: true),
            Completed("c", 1, capped: false, missing: true));

        Assert.True(finished.TryGetProperty("missingDeliverables", out var missing));
        Assert.Equal(2, missing.GetInt32());

        var none = await RunFinishedAsync("written", Completed("a", 1, capped: false));
        Assert.True(none.TryGetProperty("missingDeliverables", out var zero));
        Assert.Equal(0, zero.GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async Task ShouldScopeTheMissingDeliverablesToTheTrial_WhenTwoTrialsShareTheObserver()
    {
        using var output = new StringWriter();
        var session = ForgeSession.Create(_workspace, "two-trials-missing");
        var observer = new ForgeRunObserver();
        var events = new ForgeEventWriter(output, new FakeOrkeonClock());
        var first = new TestStage(
            new FakeFeedingTestBench(observer, [Completed("a", 1, capped: false, missing: true)]), observer);
        var second = new TestStage(
            new FakeFeedingTestBench(observer, [Completed("b", 1, capped: false)]), observer);

        await first.RunAsync(session, events, TestContext.Current.CancellationToken);
        await second.RunAsync(session, events, TestContext.Current.CancellationToken);

        var finished = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonElement.Parse(l))
            .Where(e => e.GetProperty("kind").GetString() == "run.finished")
            .ToList();
        Assert.Equal(2, finished.Count);
        Assert.Equal(1, finished[0].GetProperty("missingDeliverables").GetInt32());
        Assert.Equal(0, finished[1].GetProperty("missingDeliverables").GetInt32());
    }
}
