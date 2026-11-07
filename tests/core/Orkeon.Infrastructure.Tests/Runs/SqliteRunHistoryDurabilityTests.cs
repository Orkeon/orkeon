using Microsoft.Data.Sqlite;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Infrastructure.Runs;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.Runs;

/// <summary>FWK-08, review: the Sqlite history across processes (a killed run, two writers) and the masking of secrets.</summary>
public sealed class SqliteRunHistoryDurabilityTests : IDisposable
{
    private const string Host = "build-box";

    private static readonly string VendorKey = "s" + "k-" + new string('c', 32);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-runs-sqlite-durable-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();
    private readonly FakeRunProcessProbe _probe = new();
    private readonly DiskBackedFileSystemService _fs;
    private readonly List<SqliteRunEventSink> _sinks = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SqliteRunHistoryDurabilityTests()
    {
        Directory.CreateDirectory(_root);
        _fs = new DiskBackedFileSystemService(_root, "/runs");
    }

    public void Dispose()
    {
        foreach (var sink in _sinks)
            sink.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private SqliteRunEventSink Open(int pid, RunHistoryOptions? options = null, string host = Host)
    {
        var sink = new SqliteRunEventSink(
            _fs,
            options ?? new RunHistoryOptions { Enabled = true, Store = RunHistoryStore.Sqlite },
            _time,
            new RunProcessIdentity(pid, host, null),
            _probe);
        _sinks.Add(sink);
        return sink;
    }

    private RunEvent Event(string runId, int seq, string kind, object? payload = null) =>
        new()
        {
            RunId = runId,
            Seq = seq,
            Timestamp = _time.GetUtcNow(),
            Kind = kind,
            CorrelationId = runId,
            Payload = RunEventWire.ToPayload(payload),
        };

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldReportARunWhoseProcessVanishedAsOrphaned_AndKeepOneOnAnotherHostRunning()
    {
        var sink = Open(4242);
        await sink.WriteAsync(Event("killed", 1, RunEventKinds.RunStarted, new { target = "crew" }), Ct);
        var elsewhere = Open(9, host: "other-box");
        await elsewhere.WriteAsync(Event("remote", 1, RunEventKinds.RunStarted), Ct);

        var killed = await sink.GetAsync("killed", Ct);
        var remote = await sink.GetAsync("remote", Ct);

        Assert.Equal(RunStatus.Orphaned, killed?.Status);
        Assert.Equal(4242, killed?.Pid);
        Assert.Equal(RunStatus.Running, remote?.Status);
        _probe.Alive.Add(4242);
        Assert.Equal(RunStatus.Running, (await sink.GetAsync("killed", Ct))?.Status);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldPurgeAnOrphan_ButNeverARunWhoseProcessLives()
    {
        var options = new RunHistoryOptions { Enabled = true, Store = RunHistoryStore.Sqlite, RetentionDays = 1 };
        var sink = Open(1, options);
        await sink.WriteAsync(Event("killed", 1, RunEventKinds.RunStarted), Ct);
        var live = Open(2, options);
        await live.WriteAsync(Event("alive", 1, RunEventKinds.RunStarted), Ct);
        _probe.Alive.Add(2);
        _time.Advance(TimeSpan.FromDays(2));

        await sink.PurgeAsync(Ct);

        Assert.Equal(["alive"], (await sink.ListAsync(new RunQuery(), Ct)).Select(r => r.RunId));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldLetTwoProcessesWriteTheSameDatabaseAtOnce_WithoutALockError()
    {
        var first = Open(1);
        var second = Open(2);

        await Task.WhenAll(
            Task.Run(async () => { for (var i = 1; i <= 40; i++) await first.WriteAsync(Event("a", i, RunEventKinds.CostUpdated, new { tokens = i }), Ct); }, Ct),
            Task.Run(async () => { for (var i = 1; i <= 40; i++) await second.WriteAsync(Event("b", i, RunEventKinds.CostUpdated, new { tokens = i }), Ct); }, Ct));

        Assert.Equal(40, (await first.ReadEventsAsync("a", 1, Ct)).Count);
        Assert.Equal(40, (await first.ReadEventsAsync("b", 1, Ct)).Count);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldKeepTheJournalInWalMode_SoASecondProcessOpensItThatWay()
    {
        Open(1);
        using var check = new SqliteConnection($"Data Source={Path.Combine(_root, SqliteRunEventSink.DatabaseFile)}");
        await check.OpenAsync(Ct);
        using var journal = check.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode";
        var mode = (string?)await journal.ExecuteScalarAsync(Ct);

        // The mode is stored in the file, so a second process opens the database in WAL mode too: its reads do not block the
        // writer and its writes queue behind the writer's lock for the busy timeout instead of failing at once.
        Assert.Equal("wal", mode);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldContinueTheSequence_WhenARunIsResumedUnderItsId()
    {
        var first = Open(1);
        await first.WriteAsync(Event("resumed-run", 1, RunEventKinds.RunStarted), Ct);
        await first.WriteAsync(Event("resumed-run", 2, RunEventKinds.TaskStarted, new { taskId = "t1" }), Ct);
        // The resume is another process whose own stream restarts at 1.
        var second = Open(2);
        await second.WriteAsync(Event("resumed-run", 1, RunEventKinds.RunStarted), Ct);
        await second.WriteAsync(Event("resumed-run", 2, RunEventKinds.TaskCompleted, new { taskId = "t1" }), Ct);
        await second.WriteAsync(Event("resumed-run", 3, RunEventKinds.RunFinished, new { success = true }), Ct);

        var events = await second.ReadEventsAsync("resumed-run", 1, Ct);

        Assert.Equal([1, 2, 3, 4, 5], events.Select(e => e.Seq));
        Assert.True(events[2].Payload?["resumed"]?.GetValue<bool>());
        Assert.Null(events[0].Payload?["resumed"]);
        Assert.Equal([4, 5], (await second.ReadEventsAsync("resumed-run", 4, Ct)).Select(e => e.Seq));
        Assert.Equal(RunStatus.Succeeded, (await second.GetAsync("resumed-run", Ct))?.Status);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldMaskSecretsBeforeTheSqliteSinkWritesThem()
    {
        var sink = Open(3);
        var redacting = new RedactingRunEventSink(sink);

        await redacting.WriteAsync(Event("r", 1, RunEventKinds.RunStarted), Ct);
        await redacting.WriteAsync(Event("r", 2, RunEventKinds.Error, new { message = "call failed with api" + "_key=" + VendorKey }), Ct);

        var stored = await sink.ReadEventsAsync("r", 1, Ct);
        var line = RunEventWire.Serialize(stored[1]);
        Assert.DoesNotContain(VendorKey, line, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", line, StringComparison.Ordinal);
        Assert.DoesNotContain(VendorKey, (await sink.GetAsync("r", Ct))?.Error ?? string.Empty, StringComparison.Ordinal);
    }
}
