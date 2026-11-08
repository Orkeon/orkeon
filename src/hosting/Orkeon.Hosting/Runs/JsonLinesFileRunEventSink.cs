using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting.Runs;

/// <summary>
/// The JSONL history of runs, under the <c>/runs</c> mount: one file <c>&lt;run id&gt;.jsonl</c> per run holding the
/// lines of the stdout stream, and <c>index.jsonl</c> holding one summary line when a run starts and another when it
/// ends (the last line of a run wins), plus a tombstone line for each run a purge removed. Everything goes through the
/// virtual file system. Several processes write the same folder (two <c>orkeon run</c>, a daemon): the index is therefore
/// <b>only ever appended to</b>, never rewritten (a rewrite from one process loses the line another appended meanwhile, and a
/// reader could see it cut), and a purge decides from the run files and the index together. The writes of one run are
/// serialized, because a daemon runs several crews at once and an append is not atomic across tasks.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The semaphores are never waited on through a handle (no AvailableWaitHandle), so they hold no native resource; the sink lives as long as the host.")]
internal sealed class JsonLinesFileRunEventSink : IRunEventSink
{
    internal const string IndexFile = "index.jsonl";

    private static readonly JsonSerializerOptions IndexOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFileSystemService _fs;
    private readonly RunHistoryOptions _options;
    private readonly TimeProvider _time;
    private readonly RunProcessIdentity _identity;
    private readonly IRunProcessProbe _probe;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _runGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RunSummary> _open = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private int _folderReady;
    private int _purgedAtFirstWrite;
    private readonly ConcurrentDictionary<string, int> _seqOffsets = new(StringComparer.Ordinal);

    /// <summary>Records into the folder <paramref name="options"/> names, through <paramref name="fs"/> (the privileged view: <c>/runs</c> is an internal mount).</summary>
    /// <param name="fs">The file system.</param>
    /// <param name="options">The history settings.</param>
    /// <param name="time">The clock retention is judged by.</param>
    /// <param name="identity">The process that writes: recorded with each run it starts. The current process when null.</param>
    /// <param name="probe">Tells whether another run's process lives, to purge the runs a kill left behind. The system's when null.</param>
    public JsonLinesFileRunEventSink(
        IFileSystemService fs,
        RunHistoryOptions options,
        TimeProvider time,
        RunProcessIdentity? identity = null,
        IRunProcessProbe? probe = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _identity = identity ?? RunProcessIdentity.Current;
        _probe = probe ?? new SystemRunProcessProbe();
    }

    internal static JsonSerializerOptions SummaryOptions => IndexOptions;

    /// <inheritdoc />
    public async System.Threading.Tasks.Task WriteAsync(RunEvent runEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        // An event outside a run has no file to go to; the deltas are verbose and kept on the stream only.
        if (string.IsNullOrEmpty(runEvent.RunId)
            || (runEvent.Kind == RunEventKinds.LlmDelta && !_options.IncludeDeltas))
        {
            return;
        }

        await EnsureFolderAsync(ct).ConfigureAwait(false);

        // The writing side purges, when it first writes: the readers never do.
        if (Interlocked.Exchange(ref _purgedAtFirstWrite, 1) == 0)
            await PurgeAsync(ct).ConfigureAwait(false);

        var gate = _runGates.GetOrAdd(runEvent.RunId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var stored = await ContinueSequenceAsync(runEvent, ct).ConfigureAwait(false);
            await AppendLineAsync(RunFile(runEvent.RunId), RunEventWire.Serialize(stored), ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }

        await IndexAsync(runEvent, ct).ConfigureAwait(false);
    }

    // A run resumed under its own id is another process whose stream restarts at 1: its events continue the sequence the file
    // holds for the run, so that numbers stay strictly increasing within one run id, and its run.started says it resumed.
    private async System.Threading.Tasks.Task<RunEvent> ContinueSequenceAsync(RunEvent runEvent, CancellationToken ct)
    {
        var runId = runEvent.RunId!;
        if (!_seqOffsets.TryGetValue(runId, out var offset))
        {
            var text = await _fs.TryReadAllTextAsync(RunFile(runId), ct).ConfigureAwait(false);
            offset = text is null ? 0 : JsonLinesRunHistoryReader.LastSeq(text);
            _seqOffsets[runId] = offset;
        }

        if (offset == 0)
            return runEvent;

        var payload = runEvent.Payload;
        if (runEvent.Kind == RunEventKinds.RunStarted)
        {
            payload = runEvent.Payload?.DeepClone() as System.Text.Json.Nodes.JsonObject ?? [];
            payload["resumed"] = true;
        }

        return runEvent with { Seq = runEvent.Seq + offset, Payload = payload };
    }

    internal string RunFile(string runId) => $"{_options.Path.TrimEnd('/')}/{runId}.jsonl";

    internal string IndexPath => $"{_options.Path.TrimEnd('/')}/{IndexFile}";

    // A process killed mid-write leaves a last line without its newline: the next line would be glued to it and both lost
    // (FWK final review H15). A file that does not end with a newline gets one first.
    private async System.Threading.Tasks.Task AppendLineAsync(string path, string line, CancellationToken ct)
    {
        var prefix = string.Empty;
        if (await _fs.ExistsAsync(path, ct).ConfigureAwait(false))
        {
            var stream = await _fs.OpenReadStreamAsync(path, ct).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                if (stream.CanSeek && stream.Length > 0)
                {
                    stream.Seek(-1, SeekOrigin.End);
                    var last = new byte[1];
                    if (await stream.ReadAsync(last, ct).ConfigureAwait(false) == 1 && last[0] != (byte)'\n')
                        prefix = "\n";
                }
            }
        }

        await _fs.AppendAllTextAsync(path, prefix + line + "\n", ct).ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task EnsureFolderAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _folderReady, 1) == 0)
            await _fs.CreateDirectoryAsync(_options.Path, ct).ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task IndexAsync(RunEvent runEvent, CancellationToken ct)
    {
        var runId = runEvent.RunId!;
        RunSummary? line = null;
        switch (runEvent.Kind)
        {
            case RunEventKinds.RunStarted:
                line = new RunSummary
                {
                    RunId = runId,
                    CrewKey = CrewKeyOf(runEvent),
                    Status = RunStatus.Running,
                    StartedAt = runEvent.Timestamp,
                    Pid = _identity.Pid,
                    Host = _identity.Host,
                    ProcessStartedAt = _identity.StartedAt,
                };
                _open[runId] = line;
                break;

            case RunEventKinds.Error when _open.TryGetValue(runId, out var open):
                _open[runId] = open with { Error = Message(runEvent) ?? open.Error };
                break;

            case RunEventKinds.RunFinished:
                var started = _open.TryRemove(runId, out var begun)
                    ? begun
                    : new RunSummary { RunId = runId, StartedAt = runEvent.Timestamp };
                var success = runEvent.Payload?["success"]?.GetValue<bool>() == true;
                line = started with
                {
                    Status = runEvent.Payload?["status"]?.ToString() == RunFinishedStatuses.WaitingForInput
                        ? RunStatus.WaitingForInput
                        : success ? RunStatus.Succeeded : RunStatus.Failed,
                    FinishedAt = runEvent.Timestamp,
                };
                break;
        }

        if (line is null)
            return;

        await _indexGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AppendLineAsync(IndexPath, JsonSerializer.Serialize(line, IndexOptions), ct).ConfigureAwait(false);
            if (runEvent.Kind == RunEventKinds.RunFinished)
            {
                _runGates.TryRemove(runId, out _);
                await PurgeCoreAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _indexGate.Release();
        }
    }

    /// <summary>Removes the runs the retention settings no longer keep. Called when this sink first writes and when a run finishes.</summary>
    public async System.Threading.Tasks.Task PurgeAsync(CancellationToken ct = default)
    {
        await _indexGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await PurgeCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _indexGate.Release();
        }
    }

    private async System.Threading.Tasks.Task PurgeCoreAsync(CancellationToken ct)
    {
        // What the folder holds, files and index together, with the runs whose process is gone told apart from the live ones.
        var runs = await JsonLinesRunHistoryReader.ReadAllAsync(_fs, _options.Path.TrimEnd('/'), _probe, _identity.Host, ct).ConfigureAwait(false);
        foreach (var runId in RunHistoryRetention.Select(runs, _options, _time.GetUtcNow()))
        {
            await _fs.DeleteAsync(RunFile(runId), recursive: false, ct).ConfigureAwait(false);

            // A tombstone, not a rewrite of the index: another process may be appending to it right now.
            await AppendLineAsync(IndexPath, JsonSerializer.Serialize(new { run_id = runId, purged = true }), ct).ConfigureAwait(false);
        }
    }

    // The crew a run executed: the crew id when the event names one, else what run.started says it targets.
    private static string? CrewKeyOf(RunEvent runEvent) =>
        runEvent.CrewId
        ?? runEvent.Payload?["crewKey"]?.GetValue<string>()
        ?? runEvent.Payload?["target"]?.GetValue<string>();

    private static string? Message(RunEvent runEvent) =>
        runEvent.Payload?["message"] is { } node && node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;
}

/// <summary>Reads back what <see cref="JsonLinesFileRunEventSink"/> wrote.</summary>
internal sealed class JsonLinesRunHistoryReader : IRunHistoryReader
{
    private readonly IFileSystemService _fs;
    private readonly string _folder;
    private readonly IRunProcessProbe _probe;
    private readonly string _localHost;

    /// <summary>Reads the folder <paramref name="options"/> names, through <paramref name="fs"/>.</summary>
    /// <param name="fs">The file system.</param>
    /// <param name="options">The history settings.</param>
    /// <param name="probe">Tells whether the process of an unfinished run lives; the system's when null.</param>
    /// <param name="localHost">The machine this reader runs on, the only one whose processes it can tell about; this machine's name when null.</param>
    public JsonLinesRunHistoryReader(IFileSystemService fs, RunHistoryOptions options, IRunProcessProbe? probe = null, string? localHost = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _folder = (options ?? throw new ArgumentNullException(nameof(options))).Path.TrimEnd('/');
        _probe = probe ?? new SystemRunProcessProbe();
        _localHost = localHost ?? Environment.MachineName;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<IReadOnlyList<RunSummary>> ListAsync(RunQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var runs = await ReadAllAsync(_fs, _folder, _probe, _localHost, ct).ConfigureAwait(false);
        return
        [
            .. runs
                .Where(run => query.CrewKey is null || string.Equals(run.CrewKey, query.CrewKey, StringComparison.Ordinal))
                .OrderByDescending(run => run.StartedAt)
                .ThenByDescending(run => run.RunId, StringComparer.Ordinal)
                .Take(query.Limit > 0 ? query.Limit : int.MaxValue),
        ];
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<RunSummary?> GetAsync(string runId, CancellationToken ct = default)
    {
        var runs = await ReadAllAsync(_fs, _folder, _probe, _localHost, ct).ConfigureAwait(false);
        return runs.FirstOrDefault(run => string.Equals(run.RunId, runId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<RunEvent>> ReadEventsAsync(string runId, int fromSeq = 1, CancellationToken ct = default) =>
        ReadEventsAsync(runId, fromSeq, int.MaxValue, ct);

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<IReadOnlyList<RunEvent>> ReadEventsAsync(string runId, int fromSeq, int limit, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        // A run id names a file: anything that could leave the folder is no run.
        if (runId.AsSpan().IndexOfAny('/', '\\') >= 0 || runId.Contains("..", StringComparison.Ordinal))
            return [];

        var text = await _fs.TryReadAllTextAsync($"{_folder}/{runId}.jsonl", ct).ConfigureAwait(false);
        if (text is null)
            return [];

        return [.. ParseEvents(text, runId).Where(e => e.Seq >= fromSeq).Take(limit)];
    }

    /// <summary>
    /// Every run of the folder: the index folded (the last line of a run wins, a tombstone removes it), then the run files the
    /// index does not know (its line was lost) summarised from their own events; a run whose process vanished is orphaned.
    /// </summary>
    internal static async System.Threading.Tasks.Task<IReadOnlyList<RunSummary>> ReadAllAsync(
        IFileSystemService fs, string folder, IRunProcessProbe probe, string localHost, CancellationToken ct)
    {
        var byRun = new Dictionary<string, RunSummary>(StringComparer.Ordinal);
        var purged = new HashSet<string>(StringComparer.Ordinal);

        var index = await fs.TryReadAllTextAsync($"{folder}/{JsonLinesFileRunEventSink.IndexFile}", ct).ConfigureAwait(false);
        foreach (var line in (index ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("run_id", out var id) || id.GetString() is not { } runId)
                    continue;

                if (document.RootElement.TryGetProperty("purged", out _))
                {
                    purged.Add(runId);
                    byRun.Remove(runId);
                }
                else if (JsonSerializer.Deserialize<RunSummary>(line, JsonLinesFileRunEventSink.SummaryOptions) is { } summary)
                {
                    // A run started again under its id (resumed) after a purge is back.
                    purged.Remove(runId);
                    byRun[runId] = summary;
                }
            }
            catch (JsonException)
            {
                // A torn line (a crash mid-append) is skipped, not fatal.
            }
        }

        if (await fs.ExistsAsync(folder, ct).ConfigureAwait(false))
        {
            await foreach (var entry in fs.EnumerateFilesAsync(folder, new VirtualEnumerationOptions(Recursive: false, SearchPattern: "*.jsonl"), ct).ConfigureAwait(false))
            {
                var id = Path.GetFileNameWithoutExtension(entry.VirtualPath);
                if (entry.Kind != VirtualEntryKind.File
                    || string.Equals(id, Path.GetFileNameWithoutExtension(JsonLinesFileRunEventSink.IndexFile), StringComparison.Ordinal)
                    || byRun.ContainsKey(id)
                    || purged.Contains(id))
                {
                    continue;
                }

                var text = await fs.TryReadAllTextAsync(entry.VirtualPath, ct).ConfigureAwait(false);
                if (text is not null && Summarise(id, ParseEvents(text, id), entry.LastModified) is { } found)
                    byRun[id] = found;
            }
        }

        return [.. byRun.Values.Select(run => RunLiveness.Apply(run, probe, localHost))];
    }

    /// <summary>The sequence number of the last event a run file holds, 0 for none.</summary>
    internal static int LastSeq(string text) => ParseEvents(text, string.Empty).Select(e => e.Seq).DefaultIfEmpty(0).Max();

    private static List<RunEvent> ParseEvents(string text, string runId)
    {
        var events = new List<RunEvent>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // A torn last line (a crash mid-append) is skipped, not fatal.
            if (RunEventWire.TryParse(line, out var parsed, runId))
                events.Add(parsed);
        }

        return events;
    }

    // A summary from the events alone, for a run file whose index line is lost. The process is unknown then: an unfinished
    // run stays running.
    private static RunSummary? Summarise(string runId, List<RunEvent> events, DateTimeOffset modified)
    {
        if (events.Count == 0)
            return null;

        var started = events.FirstOrDefault(e => e.Kind == RunEventKinds.RunStarted);
        var finished = events.LastOrDefault(e => e.Kind == RunEventKinds.RunFinished);
        var error = events.LastOrDefault(e => e.Kind == RunEventKinds.Error)?.Payload?["message"]?.ToString();
        return new RunSummary
        {
            RunId = runId,
            CrewKey = started?.CrewId ?? started?.Payload?["crewKey"]?.ToString() ?? started?.Payload?["target"]?.ToString(),
            Status = finished is null
                ? RunStatus.Running
                : finished.Payload?["status"]?.ToString() == RunFinishedStatuses.WaitingForInput ? RunStatus.WaitingForInput
                : finished.Payload?["success"]?.GetValue<bool>() == true ? RunStatus.Succeeded : RunStatus.Failed,
            StartedAt = (started?.Timestamp ?? events[0].Timestamp) is { } first && first != default ? first : modified,
            FinishedAt = finished?.Timestamp,
            Error = error,
        };
    }
}
