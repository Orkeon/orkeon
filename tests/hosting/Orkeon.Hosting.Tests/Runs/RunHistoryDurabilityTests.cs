using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Hosting.Runs;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.Runs;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Hosting.Tests.Runs;

/// <summary>
/// FWK-08, review: what the file history does when processes die or overlap. A run killed before <c>run.finished</c> is
/// <c>orphaned</c>, not <c>running</c> for ever; two writers on one folder lose nothing; the index is only appended to;
/// a run file the index lost is still listed; secrets are masked before they reach the disk; the reading side never purges.
/// </summary>
public sealed class RunHistoryDurabilityTests : IDisposable
{
    private const string Host = "build-box";

    // Built at run time: these are shapes of secrets, never the secrets of anyone.
    private static readonly string VendorKey = "s" + "k-" + new string('a', 32);
    private static readonly string BearerSecret = new('b', 36);
    private const string PasswordValue = "hunter2hunter2";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-runs-durable-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();
    private readonly FakeRunProcessProbe _probe = new();
    private readonly DiskBackedFileSystemService _fs;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RunHistoryDurabilityTests()
    {
        Directory.CreateDirectory(_root);
        _fs = new DiskBackedFileSystemService(_root, "/runs");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static RunProcessIdentity Process(int pid, string host = Host) => new(pid, host, null);

    private JsonLinesFileRunEventSink Sink(RunHistoryOptions options, RunProcessIdentity identity) => new(_fs, options, _time, identity, _probe);

    private IRunHistoryReader Reader(RunHistoryOptions options, string host = Host) => new JsonLinesRunHistoryReader(_fs, options, _probe, host);

    private static RunHistoryOptions Options(int retentionDays = 30, int maxRuns = 1000) =>
        new() { Enabled = true, RetentionDays = retentionDays, MaxRuns = maxRuns };

    private RunEventPublisher Run(IRunEventSink sink, string id) => new([sink], _time, runId: id);

    private static void Start(RunEventPublisher run, string crew = "crew") => run.Emit(RunEventKinds.RunStarted, new { target = crew });

    private static void Finish(RunEventPublisher run, bool success = true) => run.Emit(RunEventKinds.RunFinished, new { success });

    // ---- a killed run

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldReportARunWhoseProcessVanishedOnThisHostAsOrphaned()
    {
        var options = Options();
        Start(Run(Sink(options, Process(4242)), "killed"));

        var summary = await Reader(options).GetAsync("killed", Ct);

        Assert.Equal(RunStatus.Orphaned, summary?.Status);
        Assert.Equal(4242, summary?.Pid);
        Assert.Equal(Host, summary?.Host);
        Assert.Equal(RunStatus.Orphaned, (await Reader(options).ListAsync(new RunQuery(), Ct)).Single().Status);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldKeepARunRunning_WhileItsProcessLives_OrWhenItWasRecordedOnAnotherHost()
    {
        var options = Options();
        Start(Run(Sink(options, Process(10)), "alive"));
        Start(Run(Sink(options, Process(20, "other-box")), "elsewhere"));
        _probe.Alive.Add(10);

        var reader = Reader(options);

        Assert.Equal(RunStatus.Running, (await reader.GetAsync("alive", Ct))?.Status);
        Assert.Equal(RunStatus.Running, (await reader.GetAsync("elsewhere", Ct))?.Status);
        Assert.DoesNotContain(20, _probe.Asked);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldPurgeAnOrphan_AndNotCountItAsInFlight()
    {
        var options = Options(retentionDays: 1);
        var sink = Sink(options, Process(77));
        Start(Run(sink, "killed"));
        _time.Advance(TimeSpan.FromDays(2));

        await sink.PurgeAsync(Ct);

        Assert.Empty(await Reader(options).ListAsync(new RunQuery(), Ct));
        Assert.False(File.Exists(Path.Combine(_root, "killed.jsonl")));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldNotPurgeARunWhoseProcessLives()
    {
        var options = Options(retentionDays: 1);
        var sink = Sink(options, Process(88));
        Start(Run(sink, "long-run"));
        _probe.Alive.Add(88);
        _time.Advance(TimeSpan.FromDays(2));

        await sink.PurgeAsync(Ct);

        Assert.Single(await Reader(options).ListAsync(new RunQuery(), Ct));
    }

    // ---- two writers, one folder

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldLoseNoRun_WhenTwoWritersShareTheFolderAndOnePurgesBetween()
    {
        var options = Options(retentionDays: 10);
        var first = Sink(options, Process(1));
        var second = Sink(options, Process(2));
        var old = Run(first, "old");
        Start(old);
        Finish(old);
        _time.Advance(TimeSpan.FromDays(11));

        var a = Run(first, "a");
        var b = Run(second, "b");
        Start(a);
        Start(b);
        _probe.Alive.Add(1);
        _probe.Alive.Add(2);
        var before = await File.ReadAllTextAsync(Path.Combine(_root, "index.jsonl"), Ct);
        await first.PurgeAsync(Ct);
        Finish(a);
        Finish(b);

        var runs = (await Reader(options).ListAsync(new RunQuery(), Ct)).Select(r => (r.RunId, r.Status)).OrderBy(r => r.RunId, StringComparer.Ordinal).ToList();
        var after = await File.ReadAllTextAsync(Path.Combine(_root, "index.jsonl"), Ct);

        Assert.Equal([("a", RunStatus.Succeeded), ("b", RunStatus.Succeeded)], runs);
        // The index is only ever appended to: what a reader saw once is still there, byte for byte.
        Assert.StartsWith(before, after, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldListARunWhoseLineTheIndexLost()
    {
        var options = Options();
        var sink = Sink(options, Process(5));
        var run = Run(sink, "r1");
        Start(run, "crews/support.yaml");
        run.Emit(RunEventKinds.Error, new { message = "it broke" });
        Finish(run, success: false);
        File.Delete(Path.Combine(_root, "index.jsonl"));

        var summary = (await Reader(options).ListAsync(new RunQuery(), Ct)).Single();

        Assert.Equal("r1", summary.RunId);
        Assert.Equal(RunStatus.Failed, summary.Status);
        Assert.Equal("crews/support.yaml", summary.CrewKey);
        Assert.Equal("it broke", summary.Error);
        Assert.Equal(3, (await Reader(options).ReadEventsAsync("r1", 1, Ct)).Count);
    }

    // ---- a run resumed under its own id

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldContinueTheSequence_WhenARunIsResumedUnderItsId_InTheFile()
    {
        var options = Options();
        // First process: killed after two events. Second process: the resume, whose own stream restarts at 1.
        var killed = Run(Sink(options, Process(1)), "resumed-run");
        Start(killed);
        killed.Emit(RunEventKinds.TaskStarted, new { taskId = "t1" });
        var resume = Run(Sink(options, Process(2)), "resumed-run");
        Start(resume);
        resume.Emit(RunEventKinds.TaskCompleted, new { taskId = "t1" });
        Finish(resume);

        var reader = Reader(options);
        var events = await reader.ReadEventsAsync("resumed-run", 1, Ct);

        Assert.Equal([1, 2, 3, 4, 5], events.Select(e => e.Seq));
        Assert.Equal(RunEventKinds.RunStarted, events[2].Kind);
        Assert.True(events[2].Payload?["resumed"]?.GetValue<bool>());
        Assert.Null(events[0].Payload?["resumed"]);
        var fromFour = await reader.ReadEventsAsync("resumed-run", 4, Ct);
        Assert.Equal([4, 5], fromFour.Select(e => e.Seq));
    }

    // ---- the writing side purges when it first writes

    [Fact]
    [Trait("BR", "FWK-08")]
    public void ShouldPurgeWhatExpired_AtTheFirstWriteOfANewRun()
    {
        var options = Options(retentionDays: 5);
        var gone = Run(Sink(options, Process(1)), "expired");
        Start(gone);
        Finish(gone);
        _time.Advance(TimeSpan.FromDays(6));

        Start(Run(Sink(options, Process(2)), "fresh"));

        Assert.False(File.Exists(Path.Combine(_root, "expired.jsonl")));
        Assert.True(File.Exists(Path.Combine(_root, "fresh.jsonl")));
    }

    // ---- secrets

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldMaskSecretsInAnEvent_BeforeTheFileSinkWritesIt()
    {
        var options = Options();
        var run = Run(new RedactingRunEventSink(Sink(options, Process(6))), "secretive");
        Start(run);
        run.Emit(RunEventKinds.ToolCalled, new { toolName = "http", argsSummary = "Authorization: Bearer " + BearerSecret });
        run.Emit(RunEventKinds.Error, new { code = "crew_failed", message = "call failed with api" + "_key=" + VendorKey + " at the vendor" });
        run.Emit(RunEventKinds.ToolReturned, new { toolName = "login", nested = new { password = PasswordValue, note = "fine" } });
        Finish(run);

        var text = await File.ReadAllTextAsync(Path.Combine(_root, "secretive.jsonl"), Ct);

        Assert.DoesNotContain(BearerSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain(VendorKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain(PasswordValue, text, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", text, StringComparison.Ordinal);
        Assert.Contains("fine", text, StringComparison.Ordinal);
        // What is not secret survives untouched.
        Assert.Contains("\"toolName\":\"http\"", text, StringComparison.Ordinal);
    }
}
