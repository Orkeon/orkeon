using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>An <see cref="IAgentExecutionService"/> whose only task does what the test says, inside the run.</summary>
internal sealed class ScriptedAgentExecutionService(Func<CancellationToken, System.Threading.Tasks.Task<string>> work) : IAgentExecutionService
{
    /// <inheritdoc />
    public async System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default) =>
        new(true, await work(cancellationToken).ConfigureAwait(false), null, [], TimeSpan.Zero);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class =>
        throw new NotSupportedException();
}
