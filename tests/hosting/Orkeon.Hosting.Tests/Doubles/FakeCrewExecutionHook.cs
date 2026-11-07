using System.Collections.Concurrent;
using Orkeon.Application.Crew;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ICrewExecutionHook"/> that keeps what it is told: the snapshot of every task
/// that completed, in order, and how each crew ended.
/// </summary>
public sealed class FakeCrewExecutionHook : ICrewExecutionHook
{
    private readonly ConcurrentQueue<TaskExecutionSnapshot> _completed = new();

    /// <summary>Every task completion reported, in order.</summary>
    public IReadOnlyCollection<TaskExecutionSnapshot> Completed => [.. _completed];

    /// <summary>The crew-level snapshots reported at the end of a crew that completed.</summary>
    public ConcurrentQueue<CrewExecutionSnapshot> CrewCompleted { get; } = new();

    /// <inheritdoc />
    public Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
    {
        _completed.Enqueue(snapshot);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct)
    {
        CrewCompleted.Enqueue(snapshot);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnCrewFailedAsync(CrewExecutionSnapshot isPartial, Exception? ex, CancellationToken ct) => Task.CompletedTask;
}
