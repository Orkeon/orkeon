using Microsoft.Data.Sqlite;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Infrastructure.Runs;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.Runs;

/// <summary>FWK-08: the Sqlite history of runs, opened through the virtual file system over a real folder.</summary>
public sealed class SqliteRunEventSinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-runs-sqlite-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();
    private readonly DiskBackedFileSystemService _fs;
    private readonly List<SqliteRunEventSink> _sinks = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SqliteRunEventSinkTests()
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

    private SqliteRunEventSink Open(RunHistoryOptions? options = null)
    {
        var sink = new SqliteRunEventSink(_fs, options ?? new RunHistoryOptions { Enabled = true, Store = RunHistoryStore.Sqlite }, _time);
        _sinks.Add(sink);
        return sink;
    }

    private static RunEvent Event(string runId, int seq, string kind, DateTimeOffset at, object? payload = null, string? crewId = null, string? agentId = null) =>
        new()
        {
            RunId = runId,
            Seq = seq,
            Timestamp = at,
            Kind = kind,
            CrewId = crewId,
            AgentId = agentId,
            CorrelationId = runId,
            Payload = RunEventWire.ToPayload(payload),
        };

    private async Task RecordAsync(SqliteRunEventSink sink, string runId, string crew, bool success = true)
    {
        await sink.WriteAsync(Event(runId, 1, RunEventKinds.RunStarted, _time.GetUtcNow(), new { target = crew }), Ct);
        await sink.WriteAsync(Event(runId, 2, RunEventKinds.TaskStarted, _time.GetUtcNow(), new { taskId = "t1" }, agentId: "writer"), Ct);
        await sink.WriteAsync(Event(runId, 3, RunEventKinds.RunFinished, _time.GetUtcNow(), new { success }), Ct);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldStoreARunAndReadItBackLineForLine_IdentityIncluded()
    {
        var sink = Open();
        await sink.WriteAsync(Event("r1", 1, RunEventKinds.RunStarted, _time.GetUtcNow(), new { target = "crew.yaml" }), Ct);
        await sink.WriteAsync(Event("r1", 2, RunEventKinds.CostUpdated, _time.GetUtcNow(), new { tokens = 15 }, crewId: "c-1", agentId: "a-1"), Ct);
        await sink.WriteAsync(Event("r1", 3, RunEventKinds.RunFinished, _time.GetUtcNow(), new { success = true }), Ct);

        var events = await sink.ReadEventsAsync("r1", 1, Ct);

        Assert.Equal([1, 2, 3], events.Select(e => e.Seq));
        Assert.Equal(
            "{\"v\":2,\"seq\":2,\"ts\":\"2026-10-01T09:00:00Z\",\"kind\":\"cost.updated\",\"crewId\":\"c-1\",\"agentId\":\"a-1\",\"correlationId\":\"r1\",\"tokens\":15}",
            RunEventWire.Serialize(events[1]));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldSummariseARun_FromStartToFinish_WithItsError()
    {
        var sink = Open();
        await sink.WriteAsync(Event("r2", 1, RunEventKinds.RunStarted, _time.GetUtcNow(), new { target = "support" }), Ct);
        var running = await sink.GetAsync("r2", Ct);
        _time.Advance(TimeSpan.FromSeconds(3));
        await sink.WriteAsync(Event("r2", 2, RunEventKinds.Error, _time.GetUtcNow(), new { message = "boom" }), Ct);
        await sink.WriteAsync(Event("r2", 3, RunEventKinds.RunFinished, _time.GetUtcNow(), new { success = false }), Ct);

        var finished = await sink.GetAsync("r2", Ct);

        Assert.Equal(RunStatus.Running, running?.Status);
        Assert.Equal(RunStatus.Failed, finished?.Status);
        Assert.Equal("support", finished?.CrewKey);
        Assert.Equal("boom", finished?.Error);
        Assert.Equal(_time.GetUtcNow(), finished?.FinishedAt);
        Assert.Null(await sink.GetAsync("nope", Ct));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldListNewestFirst_FilterByCrew_AndReadFromASeq()
    {
        var sink = Open();
        foreach (var (id, crew) in new[] { ("a", "alpha"), ("b", "beta"), ("c", "alpha") })
        {
            await RecordAsync(sink, id, crew);
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(["c", "b", "a"], (await sink.ListAsync(new RunQuery(), Ct)).Select(r => r.RunId));
        Assert.Equal(["c", "a"], (await sink.ListAsync(new RunQuery { CrewKey = "alpha" }, Ct)).Select(r => r.RunId));
        Assert.Equal(["c"], (await sink.ListAsync(new RunQuery { Limit = 1 }, Ct)).Select(r => r.RunId));
        Assert.Equal([2, 3], (await sink.ReadEventsAsync("a", 2, Ct)).Select(e => e.Seq));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldKeepDeltasOffUnlessAsked_AndSkipEventsOutsideARun()
    {
        var sink = Open();

        await sink.WriteAsync(Event("r3", 1, RunEventKinds.RunStarted, _time.GetUtcNow()), Ct);
        await sink.WriteAsync(Event("r3", 2, RunEventKinds.LlmDelta, _time.GetUtcNow(), new { text = "x" }), Ct);
        await sink.WriteAsync(new RunEvent { Kind = RunEventKinds.Error, Seq = 1, Timestamp = _time.GetUtcNow() }, Ct);

        Assert.Equal([RunEventKinds.RunStarted], (await sink.ReadEventsAsync("r3", 1, Ct)).Select(e => e.Kind));
        Assert.Single(await sink.ListAsync(new RunQuery(), Ct));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldPurgeByRetentionDaysAndMaxRuns_NeverARunInFlight()
    {
        var sink = Open(new RunHistoryOptions { Enabled = true, Store = RunHistoryStore.Sqlite, RetentionDays = 10, MaxRuns = 2 });
        await RecordAsync(sink, "old", "crew");
        await sink.WriteAsync(Event("live", 1, RunEventKinds.RunStarted, _time.GetUtcNow()), Ct);
        _time.Advance(TimeSpan.FromDays(11));
        foreach (var id in new[] { "n1", "n2", "n3" })
        {
            await RecordAsync(sink, id, "crew");
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        var kept = (await sink.ListAsync(new RunQuery(), Ct)).Select(r => r.RunId).Order(StringComparer.Ordinal).ToList();

        // MaxRuns counts the run in flight, which is never purged: the two oldest finished ones make room for it.
        Assert.Equal(["live", "n3"], kept);
        Assert.Empty(await sink.ReadEventsAsync("old", 1, Ct));
        Assert.Empty(await sink.ReadEventsAsync("n1", 1, Ct));
        Assert.Empty(await sink.ReadEventsAsync("n2", 1, Ct));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldRunInWalMode_AndSurviveAReopen()
    {
        var sink = Open();
        await RecordAsync(sink, "r4", "crew");
        sink.Dispose();
        SqliteConnection.ClearAllPools();

        using var check = new SqliteConnection($"Data Source={Path.Combine(_root, SqliteRunEventSink.DatabaseFile)}");
        await check.OpenAsync(Ct);
        using var pragma = check.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode";
        var mode = (string?)await pragma.ExecuteScalarAsync(Ct);
        var reopened = Open();

        Assert.Equal("wal", mode);
        Assert.Equal(["r4"], (await reopened.ListAsync(new RunQuery(), Ct)).Select(r => r.RunId));
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldKeepStartedAtAndInputs_WhenAResumedRunStartsAgain()
    {
        var sink = Open();
        var first = _time.GetUtcNow();
        await sink.WriteAsync(Event("r1", 1, RunEventKinds.RunStarted, first, new { target = "crew", inputs = new { topic = "x" } }), Ct);
        await sink.WriteAsync(Event("r1", 2, RunEventKinds.RunFinished, first, new { success = false, exitCode = 75, status = "waiting_for_input" }), Ct);
        _time.Advance(TimeSpan.FromHours(1));

        await sink.WriteAsync(Event("r1", 3, RunEventKinds.RunStarted, _time.GetUtcNow(), new { target = "crew" }), Ct);

        var run = await sink.GetAsync("r1", Ct);
        Assert.Equal(first, run?.StartedAt);
        Assert.Equal(RunStatus.Running, run?.Status);
        Assert.Null(run?.FinishedAt);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldReadOnlyTheFirstEvents_WhenALimitIsGiven()
    {
        var sink = Open();
        await RecordAsync(sink, "r1", "crew");

        var events = await sink.ReadEventsAsync("r1", 1, 1, Ct);

        Assert.Equal([RunEventKinds.RunStarted], events.Select(e => e.Kind));
    }
}
