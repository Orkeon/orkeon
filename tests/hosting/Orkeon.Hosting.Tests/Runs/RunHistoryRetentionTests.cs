using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Hosting.Runs;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Hosting.Tests.Runs;

/// <summary>FWK-08: <c>RetentionDays</c> and <c>MaxRuns</c>, applied when a run finishes and when the host starts.</summary>
public sealed class RunHistoryRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-runs-retention-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();
    private readonly DiskBackedFileSystemService _fs;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RunHistoryRetentionTests()
    {
        Directory.CreateDirectory(_root);
        _fs = new DiskBackedFileSystemService(_root, "/runs");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private void Record(JsonLinesFileRunEventSink sink, string id, bool finish = true)
    {
        var run = new RunEventPublisher([sink], _time, runId: id);
        run.Emit(RunEventKinds.RunStarted, new { target = "crew" });
        if (finish)
            run.Emit(RunEventKinds.RunFinished, new { success = true });
    }

    private bool HasFile(string id) => File.Exists(Path.Combine(_root, id + ".jsonl"));

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldPurgeRunsOlderThanRetentionDays_WhenARunFinishes()
    {
        var options = new RunHistoryOptions { Enabled = true, RetentionDays = 30, MaxRuns = 0 };
        var sink = new JsonLinesFileRunEventSink(_fs, options, _time);
        Record(sink, "old");
        _time.Advance(TimeSpan.FromDays(31));

        Record(sink, "new");

        var runs = await new JsonLinesRunHistoryReader(_fs, options).ListAsync(new RunQuery(), Ct);
        Assert.Equal(["new"], runs.Select(r => r.RunId));
        Assert.False(HasFile("old"));
        Assert.True(HasFile("new"));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldKeepOnlyTheNewestMaxRuns_DroppingTheOldestFirst()
    {
        var options = new RunHistoryOptions { Enabled = true, RetentionDays = 0, MaxRuns = 2 };
        var sink = new JsonLinesFileRunEventSink(_fs, options, _time);
        foreach (var id in new[] { "r1", "r2", "r3", "r4" })
        {
            Record(sink, id);
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        var runs = await new JsonLinesRunHistoryReader(_fs, options).ListAsync(new RunQuery(), Ct);

        Assert.Equal(["r4", "r3"], runs.Select(r => r.RunId));
        Assert.False(HasFile("r1"));
        Assert.False(HasFile("r2"));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldNeverPurgeARunStillInFlight_AndPurgeAtStartWhatExpired()
    {
        var options = new RunHistoryOptions { Enabled = true, RetentionDays = 1, MaxRuns = 0 };
        var sink = new JsonLinesFileRunEventSink(_fs, options, _time);
        Record(sink, "in-flight", finish: false);
        Record(sink, "done");
        _time.Advance(TimeSpan.FromDays(2));

        await sink.PurgeAsync(Ct);

        var runs = await new JsonLinesRunHistoryReader(_fs, options).ListAsync(new RunQuery(), Ct);
        Assert.Equal(["in-flight"], runs.Select(r => r.RunId));
        Assert.True(HasFile("in-flight"));
        Assert.False(HasFile("done"));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public void ShouldSelectNothing_WhenBothLimitsAreZero()
    {
        var runs = new[]
        {
            new RunSummary { RunId = "a", Status = RunStatus.Succeeded, StartedAt = _time.Start.AddYears(-3) },
        };

        Assert.Empty(RunHistoryRetention.Select(runs, new RunHistoryOptions { RetentionDays = 0, MaxRuns = 0 }, _time.GetUtcNow()));
    }
}
