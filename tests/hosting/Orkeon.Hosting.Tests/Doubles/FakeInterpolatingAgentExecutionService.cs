using Orkeon.Application.Context;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// An <see cref="IAgentExecutionService"/> whose model answers with the task's description as the run
/// interpolated it: what a test reads to see which variables reached the tasks. The tasks named in
/// <see cref="FailOn"/> fail.
/// </summary>
public sealed class FakeInterpolatingAgentExecutionService : IAgentExecutionService
{
    /// <summary>The keys of the tasks that fail.</summary>
    public HashSet<string> FailOn { get; } = [];

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        var key = task.Key ?? task.TaskId.ToString();
        if (FailOn.Contains(key))
            return System.Threading.Tasks.Task.FromResult(new TaskResult(false, "", null, [], TimeSpan.Zero, $"{key} failed"));

        var text = TaskTextInterpolation.Interpolate(task.Description.Value, context.Variables);
        return System.Threading.Tasks.Task.FromResult(new TaskResult(true, text, null, [], TimeSpan.Zero));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class =>
        throw new NotSupportedException();
}
