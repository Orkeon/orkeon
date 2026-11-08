using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Orkeon.Application.Runs;

/// <summary>Reads back the runs a history sink recorded.</summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IRunHistoryReader
{
    /// <summary>The recorded runs, newest first.</summary>
    System.Threading.Tasks.Task<IReadOnlyList<RunSummary>> ListAsync(RunQuery query, CancellationToken ct = default);

    /// <summary>One run's summary, or null when no such run is recorded.</summary>
    System.Threading.Tasks.Task<RunSummary?> GetAsync(string runId, CancellationToken ct = default);

    /// <summary>The events of a run in <see cref="RunEvent.Seq"/> order, from <paramref name="fromSeq"/> on (inclusive); empty for an unknown run.</summary>
    System.Threading.Tasks.Task<IReadOnlyList<RunEvent>> ReadEventsAsync(string runId, int fromSeq = 1, CancellationToken ct = default);

    /// <summary>The first <paramref name="limit"/> events of a run from <paramref name="fromSeq"/> on: a caller that needs only the first one does not read them all.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="fromSeq">The first sequence number to return.</param>
    /// <param name="limit">The most events to return.</param>
    /// <param name="ct">Cancellation token.</param>
    System.Threading.Tasks.Task<IReadOnlyList<RunEvent>> ReadEventsAsync(string runId, int fromSeq, int limit, CancellationToken ct = default);
}

/// <summary>Which runs <see cref="IRunHistoryReader.ListAsync"/> returns.</summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record RunQuery
{
    /// <summary>Only the runs of this crew; null for every crew.</summary>
    [JsonPropertyName("crew_key")]
    public string? CrewKey { get; init; }

    /// <summary>At most this many runs, newest first.</summary>
    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 50;
}

/// <summary>One recorded run, as the history index holds it.</summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record RunSummary
{
    /// <summary>The run identity.</summary>
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    /// <summary>The crew (or script) the run executed.</summary>
    [JsonPropertyName("crew_key")]
    public string? CrewKey { get; init; }

    /// <summary>Where the run stands.</summary>
    [JsonPropertyName("status")]
    public RunStatus Status { get; init; }

    /// <summary>When <c>run.started</c> was recorded.</summary>
    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When <c>run.finished</c> was recorded; null while the run is in flight.</summary>
    [JsonPropertyName("finished_at")]
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>The id of the process that ran it, recorded with <c>run.started</c>; null when unknown.</summary>
    [JsonPropertyName("pid")]
    public int? Pid { get; init; }

    /// <summary>The machine that ran it; a run can only be called orphaned by its own host.</summary>
    [JsonPropertyName("host")]
    public string? Host { get; init; }

    /// <summary>When that process started, to tell it from a later one that reuses the id.</summary>
    [JsonPropertyName("process_started_at")]
    public DateTimeOffset? ProcessStartedAt { get; init; }

    /// <summary>The message of the run's last <c>error</c> event, when it failed.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
