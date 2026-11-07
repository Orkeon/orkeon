using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Hosting.Runs;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Hosting.Tests.Runs;

/// <summary>
/// FWK-08: the JSONL history of runs, written through the virtual file system over a real folder
/// (<see cref="DiskBackedFileSystemService"/>), the way the daemon writes under its <c>/runs</c> mount.
/// </summary>
public sealed class JsonLinesFileRunEventSinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-runs-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();
    private readonly DiskBackedFileSystemService _fs;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public JsonLinesFileRunEventSinkTests()
    {
        Directory.CreateDirectory(_root);
        _fs = new DiskBackedFileSystemService(_root, "/runs");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static RunHistoryOptions Options(bool includeDeltas = false) => new() { Enabled = true, IncludeDeltas = includeDeltas };

    private RunEventPublisher PublisherOf(string runId, RunHistoryOptions options) =>
        new([new JsonLinesFileRunEventSink(_fs, options, _time)], _time, runId: runId);

    private IRunHistoryReader Reader(RunHistoryOptions options) => new JsonLinesRunHistoryReader(_fs, options);

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldWriteEveryEventOfARunWithAStrictlyIncreasingSeq()
    {
        var options = Options();
        var run = PublisherOf("run-a", options);

        run.Emit(RunEventKinds.RunStarted, new { target = "crew.yaml" });
        run.Emit(RunEventKinds.TaskStarted, new RunEventScope { AgentId = "writer" }, new { taskId = "t1" });
        run.Emit(RunEventKinds.TaskCompleted, new { taskId = "t1", success = true });
        run.Emit(RunEventKinds.RunFinished, new { success = true, exitCode = 0 });

        var events = await Reader(options).ReadEventsAsync("run-a", 1, Ct);

        Assert.Equal(
            [RunEventKinds.RunStarted, RunEventKinds.TaskStarted, RunEventKinds.TaskCompleted, RunEventKinds.RunFinished],
            events.Select(e => e.Kind));
        Assert.Equal([1, 2, 3, 4], events.Select(e => e.Seq));
        Assert.Equal("writer", events[1].AgentId);
        Assert.All(events, e => Assert.Equal("run-a", e.CorrelationId));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldKeepLlmDeltasOffTheHistory_UnlessIncludeDeltasIsOn()
    {
        var quiet = Options();
        var verbose = Options(includeDeltas: true);

        var withoutDeltas = PublisherOf("run-quiet", quiet);
        withoutDeltas.Emit(RunEventKinds.RunStarted);
        withoutDeltas.Emit(RunEventKinds.LlmDelta, new { text = "Bon" });
        withoutDeltas.Emit(RunEventKinds.RunFinished, new { success = true });

        var withDeltas = PublisherOf("run-verbose", verbose);
        withDeltas.Emit(RunEventKinds.RunStarted);
        withDeltas.Emit(RunEventKinds.LlmDelta, new { text = "Bon" });
        withDeltas.Emit(RunEventKinds.RunFinished, new { success = true });

        var kept = await Reader(quiet).ReadEventsAsync("run-quiet", 1, Ct);
        var all = await Reader(verbose).ReadEventsAsync("run-verbose", 1, Ct);

        Assert.DoesNotContain(kept, e => e.Kind == RunEventKinds.LlmDelta);
        // The gap is visible in seq (the delta took number 2) but the order never goes back.
        Assert.Equal([1, 3], kept.Select(e => e.Seq));
        Assert.Contains(all, e => e.Kind == RunEventKinds.LlmDelta);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldIndexARunFromStartToFinish_WithItsCrewStatusAndError()
    {
        var options = Options();
        var run = PublisherOf("run-b", options);

        run.Emit(RunEventKinds.RunStarted, new { target = "crews/support.yaml" });
        var running = await Reader(options).GetAsync("run-b", Ct);
        _time.Advance(TimeSpan.FromSeconds(5));
        run.Emit(RunEventKinds.Error, new { code = "crew_failed", message = "the model refused", recoverable = false });
        run.Emit(RunEventKinds.RunFinished, new { success = false, exitCode = 2 });
        var finished = await Reader(options).GetAsync("run-b", Ct);

        Assert.NotNull(running);
        Assert.Equal(RunStatus.Running, running.Status);
        Assert.Equal("crews/support.yaml", running.CrewKey);
        Assert.NotNull(finished);
        Assert.Equal(RunStatus.Failed, finished.Status);
        Assert.Equal("the model refused", finished.Error);
        Assert.True(finished.FinishedAt > finished.StartedAt);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldListNewestFirst_FilteredByCrewAndLimited()
    {
        var options = Options();
        foreach (var (id, crew) in new[] { ("r1", "alpha"), ("r2", "beta"), ("r3", "alpha") })
        {
            var run = PublisherOf(id, options);
            run.Emit(RunEventKinds.RunStarted, new { target = crew });
            run.Emit(RunEventKinds.RunFinished, new { success = true });
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        var reader = Reader(options);
        var all = await reader.ListAsync(new RunQuery(), Ct);
        var alpha = await reader.ListAsync(new RunQuery { CrewKey = "alpha" }, Ct);
        var one = await reader.ListAsync(new RunQuery { Limit = 1 }, Ct);

        Assert.Equal(["r3", "r2", "r1"], all.Select(r => r.RunId));
        Assert.Equal(["r3", "r1"], alpha.Select(r => r.RunId));
        Assert.Equal(["r3"], one.Select(r => r.RunId));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldKeepConcurrentRunsInTheirOwnFilesWithoutInterleaving()
    {
        var options = Options();
        var sink = new JsonLinesFileRunEventSink(_fs, options, _time);
        var runs = Enumerable.Range(0, 6).Select(i => new RunEventPublisher([sink], _time, runId: $"run-{i}")).ToList();

        await Task.WhenAll(runs.Select((run, i) => Task.Run(
            () =>
            {
                run.Emit(RunEventKinds.RunStarted, new { target = $"crew-{i}" });
                for (var n = 0; n < 25; n++)
                    run.Emit(RunEventKinds.CostUpdated, new { tokens = n });
                run.Emit(RunEventKinds.RunFinished, new { success = true });
            },
            Ct)));

        var reader = Reader(options);
        for (var i = 0; i < runs.Count; i++)
        {
            var events = await reader.ReadEventsAsync($"run-{i}", 1, Ct);
            Assert.Equal(27, events.Count);
            Assert.Equal(Enumerable.Range(1, 27), events.Select(e => e.Seq));
        }
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldNotWriteAnEventOutsideARun()
    {
        var options = Options();
        var outside = new RunEventPublisher([new JsonLinesFileRunEventSink(_fs, options, _time)], _time);

        outside.Emit(RunEventKinds.Error, new { message = "no run here" });

        Assert.Empty(await Reader(options).ListAsync(new RunQuery(), Ct));
        Assert.False(Directory.Exists(_root + "/runs") && Directory.EnumerateFileSystemEntries(_root + "/runs").Any());
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldReadFromAGivenSeq_AndRefuseARunIdThatNamesAPath()
    {
        var options = Options();
        var run = PublisherOf("run-c", options);
        run.Emit(RunEventKinds.RunStarted);
        run.Emit(RunEventKinds.CostUpdated, new { tokens = 1 });
        run.Emit(RunEventKinds.RunFinished, new { success = true });

        var reader = Reader(options);

        Assert.Equal([2, 3], (await reader.ReadEventsAsync("run-c", 2, Ct)).Select(e => e.Seq));
        Assert.Empty(await reader.ReadEventsAsync("../run-c", 1, Ct));
        Assert.Empty(await reader.ReadEventsAsync("unknown", 1, Ct));
    }
}
