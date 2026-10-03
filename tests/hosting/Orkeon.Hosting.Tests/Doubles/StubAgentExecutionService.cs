using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IAgentExecutionService"/> double: every task succeeds with a fixed
/// answer and no LLM is asked anything, so a runner test drives a real crew offline.
/// </summary>
public sealed class StubAgentExecutionService : IAgentExecutionService
{
    public Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TaskResult(true, "a draft", null, [], TimeSpan.Zero));

    public Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class =>
        throw new NotSupportedException();
}
