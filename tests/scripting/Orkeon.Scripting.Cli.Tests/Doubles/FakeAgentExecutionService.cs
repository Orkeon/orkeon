using System.Collections.Concurrent;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IAgentExecutionService"/>: every task answers <c>out:&lt;task key&gt;</c>, the
/// calls are kept per task key, and the tasks named in <see cref="FailOn"/> fail.
/// </summary>
public sealed class FakeAgentExecutionService : IAgentExecutionService
{
    private readonly ConcurrentQueue<string> _calls = new();

    /// <summary>The keys of the tasks that fail.</summary>
    public HashSet<string> FailOn { get; } = [];

    /// <summary>The key of every task run, in call order.</summary>
    public IReadOnlyCollection<string> Calls => [.. _calls];

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        var key = task.Key ?? task.TaskId.ToString();
        _calls.Enqueue(key);
        return System.Threading.Tasks.Task.FromResult(FailOn.Contains(key)
            ? new TaskResult(false, "", null, [], TimeSpan.Zero, $"{key} failed")
            : new TaskResult(true, $"out:{key}", null, [], TimeSpan.Zero));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class =>
        throw new NotSupportedException();
}
