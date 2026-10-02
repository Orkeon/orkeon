using System.Collections.Concurrent;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Memory;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IMemoryCoordinator"/> (GAP-20, GAP-30): records every task result it is
/// asked to store — the agent, the task, the output and the context —, every recall and every
/// readiness check, and stores nothing.
/// </summary>
public sealed class MockMemoryCoordinator : IMemoryCoordinator
{
    private readonly ConcurrentQueue<StoredTaskResult> _stored = new();
    private readonly ConcurrentQueue<CrewTask> _recalls = new();
    private readonly ConcurrentQueue<CrewId> _readinessChecks = new();

    /// <summary>Every <see cref="StoreTaskResultAsync"/> call, in arrival order.</summary>
    public IReadOnlyList<StoredTaskResult> Stored => [.. _stored];

    /// <summary>The task of every <see cref="RecallAsync"/> call, in arrival order.</summary>
    public IReadOnlyList<CrewTask> Recalls => [.. _recalls];

    /// <summary>The crew of every <see cref="EnsureReadyAsync"/> call, in arrival order.</summary>
    public IReadOnlyList<CrewId> ReadinessChecks => [.. _readinessChecks];

    /// <summary>When set, <see cref="EnsureReadyAsync"/> throws it (a memory that cannot work).</summary>
    public Exception? NotReady { get; set; }

    public System.Threading.Tasks.Task EnsureReadyAsync(CrewId crewId, CancellationToken cancellationToken = default)
    {
        _readinessChecks.Enqueue(crewId);
        return NotReady is null
            ? System.Threading.Tasks.Task.CompletedTask
            : System.Threading.Tasks.Task.FromException(NotReady);
    }

    public System.Threading.Tasks.Task<IReadOnlyList<RecalledMemory>> RecallAsync(
        DomainAgent agent, CrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        _recalls.Enqueue(task);
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<RecalledMemory>>([]);
    }

    public System.Threading.Tasks.Task StoreTaskResultAsync(
        DomainAgent agent, CrewTask task, string output, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        _stored.Enqueue(new StoredTaskResult(agent, task, output, context));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>One recorded <see cref="StoreTaskResultAsync"/> call.</summary>
    public sealed record StoredTaskResult(DomainAgent Agent, CrewTask Task, string Output, SimpleExecutionContext Context);
}
