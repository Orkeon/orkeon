using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.HumanInput;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>
/// An <see cref="IAgentExecutionService"/> whose only task asks a human its <c>prompt</c> through the host's own
/// <see cref="IHumanInputProvider"/> and answers <c>answer:&lt;what the human said&gt;</c>. As the agent loop does, a
/// task that is told to stop on the question ends failed instead of throwing.
/// </summary>
internal sealed class AskingAgentExecutionService(IHumanInputProvider human, string prompt) : IAgentExecutionService
{
    /// <inheritdoc />
    public async System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var answer = await human.GetInputAsync(HumanInputContext.CreateTextInput(agent.Id, task.TaskId, prompt), cancellationToken).ConfigureAwait(false);
            return new TaskResult(true, $"answer:{answer}", null, [], TimeSpan.Zero);
        }
        catch (RunSuspendedException ex)
        {
            return new TaskResult(false, "", null, [], TimeSpan.Zero, ex.Message);
        }
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class =>
        throw new NotSupportedException();
}
