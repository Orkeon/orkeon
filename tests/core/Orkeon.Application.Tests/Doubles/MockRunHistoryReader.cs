using System.Diagnostics.CodeAnalysis;
using Orkeon.Application.Runs;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>An <see cref="IRunHistoryReader"/> that knows one run, and says whether that run's process is alive.</summary>
[SuppressMessage("Usage", "ORKEXP007", Justification = "The run history is experimental; the test stands in for it.")]
#pragma warning disable ORKEXP007
public sealed class MockRunHistoryReader : IRunHistoryReader
{
    /// <summary>The run the history holds, or null.</summary>
    public RunSummary? Summary { get; set; }

    /// <summary>What the process probe will answer for that run.</summary>
    public bool ProcessAlive { get; set; }

    /// <summary>A run recorded as running, by this host.</summary>
    public static RunSummary Running(string id) => new()
    {
        RunId = id,
        Status = RunStatus.Running,
        Pid = 4242,
        Host = Environment.MachineName,
    };

    public System.Threading.Tasks.Task<IReadOnlyList<RunSummary>> ListAsync(RunQuery query, CancellationToken ct = default) =>
        System.Threading.Tasks.Task.FromResult<IReadOnlyList<RunSummary>>(Summary is null ? [] : [Summary]);

    public System.Threading.Tasks.Task<RunSummary?> GetAsync(string runId, CancellationToken ct = default) =>
        System.Threading.Tasks.Task.FromResult(Summary is { } s && s.RunId == runId ? s : null);

    public System.Threading.Tasks.Task<IReadOnlyList<RunEvent>> ReadEventsAsync(string runId, int fromSeq = 1, CancellationToken ct = default) =>
        System.Threading.Tasks.Task.FromResult<IReadOnlyList<RunEvent>>([]);
}

/// <summary>An <see cref="IRunProcessProbe"/> whose answer is a field.</summary>
public sealed class StubRunProcessProbe : IRunProcessProbe
{
    /// <summary>Whether every probed process is alive.</summary>
    public bool Alive { get; set; }

    public bool IsAlive(int pid, DateTimeOffset? startedAt) => Alive;
}
#pragma warning restore ORKEXP007
