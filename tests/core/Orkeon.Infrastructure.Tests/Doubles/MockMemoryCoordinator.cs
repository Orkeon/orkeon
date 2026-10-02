using System.Collections.Concurrent;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IMemoryCoordinator"/> (GAP-20): records every task result it is asked
/// to store — the agent, the task, the output and the context — and stores nothing.
/// </summary>
public sealed class MockMemoryCoordinator : IMemoryCoordinator
{
    private readonly ConcurrentQueue<StoredTaskResult> _stored = new();

    /// <summary>Every <see cref="StoreTaskResultAsync"/> call, in arrival order.</summary>
    public IReadOnlyList<StoredTaskResult> Stored => [.. _stored];

    /// <summary>When set, <see cref="StoreTaskResultAsync"/> throws it (a store that is down).</summary>
    public Exception? Failure { get; set; }

    public System.Threading.Tasks.Task StoreTaskResultAsync(
        DomainAgent agent, CrewTask task, string output, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
            throw Failure;

        _stored.Enqueue(new StoredTaskResult(agent, task, output, context));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<IEnumerable<MemoryItem>> RetrieveRelevantMemoriesAsync(
        DomainAgent agent, CrewTask task, SimpleExecutionContext context, int maxResults = 10, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(Enumerable.Empty<MemoryItem>());

    public System.Threading.Tasks.Task StoreAgentExperienceAsync(
        DomainAgent agent, string experience, double importance, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.CompletedTask;

    public System.Threading.Tasks.Task UpdateWorkingMemoryAsync(
        DomainAgent agent, string key, string value, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.CompletedTask;

    /// <summary>One recorded <see cref="StoreTaskResultAsync"/> call.</summary>
    public sealed record StoredTaskResult(DomainAgent Agent, CrewTask Task, string Output, SimpleExecutionContext Context);
}
