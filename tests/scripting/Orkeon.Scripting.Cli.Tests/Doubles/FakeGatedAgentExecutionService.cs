using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// An <see cref="IAgentExecutionService"/> whose tasks wait until their token is cancelled: it counts the tasks that got in and the
/// tasks that saw the cancellation, which is what a test sequences on, never a delay.
/// </summary>
public sealed class FakeGatedAgentExecutionService : IAgentExecutionService
{
    private int _entered;
    private int _cancelled;

    /// <summary>Gets how many tasks got in.</summary>
    public int Entered => Volatile.Read(ref _entered);

    /// <summary>Gets how many tasks saw their token cancelled.</summary>
    public int Cancelled => Volatile.Read(ref _cancelled);

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _entered);
        try
        {
            await System.Threading.Tasks.Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelled);
            throw;
        }

        return new TaskResult(true, "unreachable", null, [], TimeSpan.Zero);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class =>
        throw new NotSupportedException();
}
