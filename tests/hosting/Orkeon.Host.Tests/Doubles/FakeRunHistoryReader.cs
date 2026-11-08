using System.Text.Json.Nodes;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>A history of runs held in memory, whose reads are counted: each run has a <c>run.started</c> carrying a key, then many events.</summary>
internal sealed class FakeRunHistoryReader(IReadOnlyList<RunSummary> runs, int eventsPerRun) : IRunHistoryReader
{
    /// <summary>The limit of each event read, in order; <see cref="int.MaxValue"/> for a read with none.</summary>
    public List<int> Limits { get; } = [];

    public Task<IReadOnlyList<RunSummary>> ListAsync(RunQuery query, CancellationToken ct = default) => Task.FromResult(runs);

    public Task<RunSummary?> GetAsync(string runId, CancellationToken ct = default) =>
        Task.FromResult(runs.FirstOrDefault(r => r.RunId == runId));

    public Task<IReadOnlyList<RunEvent>> ReadEventsAsync(string runId, int fromSeq = 1, CancellationToken ct = default) =>
        ReadEventsAsync(runId, fromSeq, int.MaxValue, ct);

    public Task<IReadOnlyList<RunEvent>> ReadEventsAsync(string runId, int fromSeq, int limit, CancellationToken ct = default)
    {
        Limits.Add(limit);
        var events = new List<RunEvent>
        {
            new()
            {
                RunId = runId,
                Seq = 1,
                Timestamp = DateTimeOffset.UnixEpoch,
                Kind = RunEventKinds.RunStarted,
                Payload = new JsonObject { ["idempotencyKey"] = "key-" + runId, ["requestHash"] = "hash" },
            },
        };
        for (var i = 2; i <= eventsPerRun; i++)
            events.Add(new RunEvent { RunId = runId, Seq = i, Timestamp = DateTimeOffset.UnixEpoch, Kind = RunEventKinds.TaskStarted });

        return Task.FromResult<IReadOnlyList<RunEvent>>([.. events.Skip(fromSeq - 1).Take(limit)]);
    }
}
