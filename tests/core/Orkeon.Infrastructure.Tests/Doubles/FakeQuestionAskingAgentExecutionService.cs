using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Checkpointing;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="IAgentExecutionService"/> whose every call draws the id of the next question of the task in flight (the
/// way a durable asker does), records it, and fails the first <see cref="FailFirst"/> calls.
/// </summary>
public sealed class FakeQuestionAskingAgentExecutionService : IAgentExecutionService
{
    /// <summary>How many of the first calls return an unsuccessful result.</summary>
    public int FailFirst { get; set; }

    /// <summary>The question id drawn by each call, in order.</summary>
    public List<string> RequestIds { get; } = [];

    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        RequestIds.Add(TaskInputScope.Current!.NextRequestId());
        return System.Threading.Tasks.Task.FromResult(RequestIds.Count <= FailFirst
            ? new TaskResult(false, "", null, [], TimeSpan.Zero, "failed")
            : new TaskResult(true, "ok", null, [], TimeSpan.Zero));
    }

    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class =>
        throw new NotSupportedException();
}
