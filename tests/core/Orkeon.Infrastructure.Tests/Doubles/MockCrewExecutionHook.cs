using Orkeon.Application.Crew;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of <see cref="ICrewExecutionHook"/>: records every callback,
/// so a test reads which terminal event a run ended on and with which reason. Thread-safe —
/// the parallel and consensual modes report tasks from several threads.
/// </summary>
public sealed class MockCrewExecutionHook : ICrewExecutionHook
{
    private readonly object _gate = new();
    private readonly List<TaskStartSnapshot> _startedTasks = [];
    private readonly List<TaskExecutionSnapshot> _completedTasks = [];
    private readonly List<CrewExecutionSnapshot> _completions = [];
    private readonly List<CrewExecutionSnapshot> _failures = [];

    // --- Tracking ---
    public IReadOnlyList<TaskStartSnapshot> StartedTasks { get { lock (_gate) return [.. _startedTasks]; } }
    public IReadOnlyList<TaskExecutionSnapshot> CompletedTasks { get { lock (_gate) return [.. _completedTasks]; } }
    public IReadOnlyList<CrewExecutionSnapshot> Completions { get { lock (_gate) return [.. _completions]; } }
    public IReadOnlyList<CrewExecutionSnapshot> Failures { get { lock (_gate) return [.. _failures]; } }

    // --- ICrewExecutionHook ---
    public Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct)
    {
        lock (_gate) _startedTasks.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
    {
        lock (_gate) _completedTasks.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct)
    {
        lock (_gate) _completions.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task OnCrewFailedAsync(CrewExecutionSnapshot isPartial, Exception? ex, CancellationToken ct)
    {
        lock (_gate) _failures.Add(isPartial);
        return Task.CompletedTask;
    }
}
