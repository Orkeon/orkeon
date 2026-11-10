using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Persistence.Sqlite;

namespace Orkeon.Infrastructure.Runs;

/// <summary>
/// The Sqlite history of runs (WAL): table <c>runs</c> holds one row per run, table <c>run_events</c> one row per
/// event. The columns the sheet names hold what queries filter on; the envelope identity that has no column of its
/// own (<c>crewId</c>, <c>correlationId</c>, <c>causationId</c>) travels inside <c>payload_json</c>, so a stored
/// event reads back as the very line the stream carried. The database lives under the <c>/runs</c> mount and is
/// opened through the virtual file system.
/// </summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class SqliteRunEventSink : IRunEventSink, IRunHistoryReader, IDisposable
{
    /// <summary>The database file inside <see cref="RunHistoryOptions.Path"/>.</summary>
    public const string DatabaseFile = "runs.db";

    private static readonly string[] IdentityNames = ["crewId", "correlationId", "causationId"];

    private readonly SqliteConnection _connection;
    private readonly RunHistoryOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RunProcessIdentity _identity;
    private readonly IRunProcessProbe _probe;
    private int _purgedAtFirstWrite;
    private readonly Dictionary<string, int> _seqOffsets = new(StringComparer.Ordinal);

    /// <summary>Opens (and creates) the database under <paramref name="options"/>'s path, through <paramref name="fileSystem"/>.</summary>
    public SqliteRunEventSink(IFileSystemService fileSystem, RunHistoryOptions options, TimeProvider time, RunProcessIdentity? identity = null, IRunProcessProbe? probe = null)
    {
        _identity = identity ?? RunProcessIdentity.Current;
        _probe = probe ?? new SystemRunProcessProbe();
        ArgumentNullException.ThrowIfNull(fileSystem);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));

        var folder = options.Path.TrimEnd('/');
#pragma warning disable CA2012, VSTHRD002 // The constructor of a store that must exist before its first write.
        fileSystem.CreateDirectoryAsync(folder, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore CA2012, VSTHRD002

        _connection = new SqliteConnection(SqliteDataSourceGovernor.Govern($"Data Source={folder}/{DatabaseFile}", fileSystem));
        _connection.Open();
        Execute("""
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS runs (
                run_id TEXT PRIMARY KEY,
                crew_key TEXT,
                status TEXT NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT,
                error TEXT,
                inputs_json TEXT,
                idempotency_key TEXT UNIQUE,
                request_hash TEXT,
                parent_run_id TEXT,
                pid INTEGER,
                host TEXT,
                process_started_at TEXT
            );
            CREATE TABLE IF NOT EXISTS run_events (
                run_id TEXT NOT NULL,
                seq INTEGER NOT NULL,
                ts TEXT NOT NULL,
                kind TEXT NOT NULL,
                agent_id TEXT,
                payload_json TEXT NOT NULL,
                PRIMARY KEY (run_id, seq)
            );
            CREATE INDEX IF NOT EXISTS idx_runs_started ON runs (started_at DESC);
            """);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task WriteAsync(RunEvent runEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        if (string.IsNullOrEmpty(runEvent.RunId)
            || (runEvent.Kind == RunEventKinds.LlmDelta && !_options.IncludeDeltas))
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // The writing side purges, when it first writes: the readers never do.
            if (Interlocked.Exchange(ref _purgedAtFirstWrite, 1) == 0)
                await PurgeCoreAsync(ct).ConfigureAwait(false);

            runEvent = await ContinueSequenceAsync(runEvent, ct).ConfigureAwait(false);
            await StoreEventAsync(runEvent, ct).ConfigureAwait(false);
            switch (runEvent.Kind)
            {
                case RunEventKinds.RunStarted:
                    await StartRunAsync(runEvent, ct).ConfigureAwait(false);
                    break;
                case RunEventKinds.Error:
                    await Command(
                        "UPDATE runs SET error = $error WHERE run_id = $run",
                        ("$error", runEvent.Payload?["message"]?.ToString()), ("$run", runEvent.RunId)).ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    break;
                case RunEventKinds.RunFinished:
                    var success = runEvent.Payload?["success"]?.GetValue<bool>() == true;
                    var waiting = runEvent.Payload?["status"]?.ToString() == RunFinishedStatuses.WaitingForInput;
                    var finalStatus = waiting ? RunFinishedStatuses.WaitingForInput : (success ? "succeeded" : "failed");
                    await Command(
                        "UPDATE runs SET status = $status, finished_at = $at WHERE run_id = $run",
                        ("$status", finalStatus), ("$at", Iso(runEvent.Timestamp)), ("$run", runEvent.RunId))
                        .ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    await PurgeCoreAsync(ct).ConfigureAwait(false);
                    break;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes the runs the retention settings no longer keep. Called when the host starts and when a run finishes.</summary>
    public async System.Threading.Tasks.Task PurgeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await PurgeCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<IReadOnlyList<RunSummary>> ListAsync(RunQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await SummariesAsync(
                "WHERE ($crew IS NULL OR crew_key = $crew) ORDER BY started_at DESC, run_id DESC LIMIT $limit",
                ct,
                ("$crew", query.CrewKey), ("$limit", query.Limit > 0 ? query.Limit : int.MaxValue)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<RunSummary?> GetAsync(string runId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var found = await SummariesAsync("WHERE run_id = $run", ct, ("$run", runId)).ConfigureAwait(false);
            return found.Count > 0 ? found[0] : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<RunEvent>> ReadEventsAsync(string runId, int fromSeq = 1, CancellationToken ct = default) =>
        ReadEventsAsync(runId, fromSeq, int.MaxValue, ct);

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<IReadOnlyList<RunEvent>> ReadEventsAsync(string runId, int fromSeq, int limit, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var reader = await Command(
                "SELECT seq, ts, kind, agent_id, payload_json FROM run_events WHERE run_id = $run AND seq >= $from ORDER BY seq LIMIT $limit",
                ("$run", runId), ("$from", fromSeq), ("$limit", limit)).ExecuteReaderAsync(ct).ConfigureAwait(false);
            var events = new List<RunEvent>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var stored = JsonNode.Parse(reader.GetString(4)) as JsonObject ?? [];
                var identity = IdentityNames.ToDictionary(name => name, name => Take(stored, name), StringComparer.Ordinal);
                events.Add(new RunEvent
                {
                    RunId = runId,
                    Seq = reader.GetInt32(0),
                    Timestamp = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                    Kind = reader.GetString(2),
                    AgentId = reader.GetValue(3) as string,
                    CrewId = identity["crewId"],
                    CorrelationId = identity["correlationId"],
                    CausationId = identity["causationId"],
                    Payload = stored.Count == 0 ? null : stored,
                });
            }

            return events;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _connection.Dispose();
        _gate.Dispose();
    }

    private static string? Take(JsonObject stored, string name)
    {
        if (!stored.TryGetPropertyValue(name, out var node))
            return null;

        stored.Remove(name);
        return node?.ToString();
    }

    private async System.Threading.Tasks.Task StoreEventAsync(RunEvent runEvent, CancellationToken ct)
    {
        var stored = runEvent.Payload?.DeepClone() as JsonObject ?? [];
        stored["crewId"] = runEvent.CrewId;
        stored["correlationId"] = runEvent.CorrelationId;
        stored["causationId"] = runEvent.CausationId;
        foreach (var name in IdentityNames.Where(name => stored[name] is null))
            stored.Remove(name);

        await Command(
            "INSERT OR REPLACE INTO run_events (run_id, seq, ts, kind, agent_id, payload_json) VALUES ($run, $seq, $ts, $kind, $agent, $payload)",
            ("$run", runEvent.RunId), ("$seq", runEvent.Seq), ("$ts", Iso(runEvent.Timestamp)), ("$kind", runEvent.Kind),
            ("$agent", runEvent.AgentId), ("$payload", stored.ToJsonString())).ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // A run resumed under its own id is another process whose stream restarts at 1: its events continue the sequence the history
    // holds for the run, so that numbers stay strictly increasing within one run id, and its run.started says it resumed.
    private async System.Threading.Tasks.Task<RunEvent> ContinueSequenceAsync(RunEvent runEvent, CancellationToken ct)
    {
        var runId = runEvent.RunId!;
        if (!_seqOffsets.TryGetValue(runId, out var offset))
        {
            var last = await Command("SELECT COALESCE(MAX(seq), 0) FROM run_events WHERE run_id = $run", ("$run", runId))
                .ExecuteScalarAsync(ct).ConfigureAwait(false);
            offset = Convert.ToInt32(last, CultureInfo.InvariantCulture);
            _seqOffsets[runId] = offset;
        }

        if (offset == 0)
            return runEvent;

        var payload = runEvent.Payload;
        if (runEvent.Kind == RunEventKinds.RunStarted)
        {
            payload = runEvent.Payload?.DeepClone() as JsonObject ?? [];
            payload["resumed"] = true;
        }

        return runEvent with { Seq = runEvent.Seq + offset, Payload = payload };
    }

    private async System.Threading.Tasks.Task StartRunAsync(RunEvent runEvent, CancellationToken ct)
    {
        var crew = runEvent.CrewId
            ?? runEvent.Payload?["crewKey"]?.ToString()
            ?? runEvent.Payload?["target"]?.ToString();
        await Command(
            "INSERT INTO runs (run_id, crew_key, status, started_at, inputs_json, pid, host, process_started_at) VALUES ($run, $crew, 'running', $at, $inputs, $pid, $host, $pstart) "
            + "ON CONFLICT(run_id) DO UPDATE SET status = 'running', finished_at = NULL, pid = excluded.pid, host = excluded.host, process_started_at = excluded.process_started_at",
            ("$run", runEvent.RunId), ("$crew", crew), ("$at", Iso(runEvent.Timestamp)), ("$inputs", runEvent.Payload?["inputs"]?.ToJsonString()),
            ("$pid", _identity.Pid), ("$host", _identity.Host), ("$pstart", _identity.StartedAt is { } at ? Iso(at) : null))
            .ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task PurgeCoreAsync(CancellationToken ct)
    {
        var runs = await SummariesAsync("", ct).ConfigureAwait(false);
        foreach (var runId in RunHistoryRetention.Select(runs, _options, _time.GetUtcNow()))
        {
            await Command("DELETE FROM run_events WHERE run_id = $run", ("$run", runId)).ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await Command("DELETE FROM runs WHERE run_id = $run", ("$run", runId)).ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async System.Threading.Tasks.Task<IReadOnlyList<RunSummary>> SummariesAsync(
        string tail, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        using var reader = await Command(
            "SELECT run_id, crew_key, status, started_at, finished_at, error, pid, host, process_started_at FROM runs " + tail, parameters)
            .ExecuteReaderAsync(ct).ConfigureAwait(false);
        var runs = new List<RunSummary>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var summary = new RunSummary
            {
                RunId = reader.GetString(0),
                CrewKey = reader.GetValue(1) as string,
                Status = reader.GetString(2) switch
                {
                    "succeeded" => RunStatus.Succeeded,
                    "failed" => RunStatus.Failed,
                    RunFinishedStatuses.WaitingForInput => RunStatus.WaitingForInput,
                    _ => RunStatus.Running,
                },
                StartedAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                FinishedAt = reader.GetValue(4) is string finished ? DateTimeOffset.Parse(finished, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) : null,
                Error = reader.GetValue(5) as string,
                Pid = reader.GetValue(6) is long pid ? (int)pid : null,
                Host = reader.GetValue(7) as string,
                ProcessStartedAt = reader.GetValue(8) is string pstart ? DateTimeOffset.Parse(pstart, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) : null,
            };
            runs.Add(RunLiveness.Apply(summary, _probe, _identity.Host));
        }

        return runs;
    }

    [SuppressMessage("Security", "CA2100", Justification = "Every statement is a constant of this class; only the parameters carry values.")]
    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [SuppressMessage("Security", "CA2100", Justification = "Every statement is a constant of this class; only the parameters carry values.")]
    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
