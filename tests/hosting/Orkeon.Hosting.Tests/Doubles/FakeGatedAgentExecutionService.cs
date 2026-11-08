using Orkeon.Application.Context;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// An <see cref="IAgentExecutionService"/> whose tasks wait at a gate until <see cref="Release"/> (or a cancelled token), then answer
/// with the task's description as the run interpolated it. It counts the tasks that got in, the most inside at once, and the
/// tasks that saw their token cancelled: a test sequences calls on these, never on a delay.
/// </summary>
public sealed class FakeGatedAgentExecutionService : IAgentExecutionService
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _entered;
    private int _inside;
    private int _maxInside;
    private int _cancelled;

    /// <summary>Gets how many tasks got in.</summary>
    public int Entered => Volatile.Read(ref _entered);

    /// <summary>Gets the most tasks inside at the same moment.</summary>
    public int MaxInside => Volatile.Read(ref _maxInside);

    /// <summary>Gets how many tasks saw their token cancelled.</summary>
    public int Cancelled => Volatile.Read(ref _cancelled);

    /// <summary>Opens the gate for every task, those waiting and those to come.</summary>
    public void Release() => _gate.TrySetResult();

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _entered);
        var inside = Interlocked.Increment(ref _inside);
        int seen;
        while (inside > (seen = Volatile.Read(ref _maxInside)))
            Interlocked.CompareExchange(ref _maxInside, inside, seen);

        try
        {
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelled);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _inside);
        }

        return new TaskResult(true, TaskTextInterpolation.Interpolate(task.Description.Value, context.Variables), null, [], TimeSpan.Zero);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class =>
        throw new NotSupportedException();
}
