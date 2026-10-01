using System.Collections.Concurrent;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IAgentExecutionService"/> whose execution is an async delegate, so a
/// test can make the agent answer, fail, throw or wait on the cancellation token (GAP-10: the
/// A2A router runs the matched agent through this port). Every call is recorded.
/// </summary>
public sealed class FakeAgentExecutionService : IAgentExecutionService
{
    private readonly Func<DomainAgent, ICrewTask, SimpleExecutionContext, CancellationToken, System.Threading.Tasks.Task<TaskResult>> _execute;

    /// <summary>Signalled when the first execution starts — lets a test act while the agent works.</summary>
    public System.Threading.Tasks.TaskCompletionSource Started { get; } =
        new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Every (agent, task) pair the service was asked to run, in call order.</summary>
    public ConcurrentQueue<(DomainAgent Agent, ICrewTask Task)> Calls { get; } = new();

    public FakeAgentExecutionService(
        Func<DomainAgent, ICrewTask, SimpleExecutionContext, CancellationToken, System.Threading.Tasks.Task<TaskResult>> execute)
    {
        _execute = execute;
    }

    /// <summary>An agent that answers <paramref name="output"/>.</summary>
    public static FakeAgentExecutionService Answering(string output)
        => new((_, _, _, _) => System.Threading.Tasks.Task.FromResult(Success(output)));

    /// <summary>An agent whose execution reports a failure with <paramref name="error"/>.</summary>
    public static FakeAgentExecutionService Failing(string error)
        => new((_, _, _, _) => System.Threading.Tasks.Task.FromResult(
            new TaskResult(false, "", null, Array.Empty<ToolUsage>(), TimeSpan.Zero, error)));

    /// <summary>An agent that works until its cancellation token fires, then throws.</summary>
    public static FakeAgentExecutionService WaitingForCancellation()
        => new(async (_, _, _, ct) =>
        {
            await System.Threading.Tasks.Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return Success("never");
        });

    public static TaskResult Success(string output)
        => new(true, output, null, Array.Empty<ToolUsage>(), TimeSpan.Zero);

    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent,
        ICrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        Calls.Enqueue((agent, task));
        Started.TrySetResult();
        return _execute(agent, task, context, cancellationToken);
    }

    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent,
        ICrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
        where TOutput : class
        => throw new NotSupportedException("The A2A router runs untyped tasks only.");

    public System.Threading.Tasks.Task<TaskExecutionPlan> PlanTaskExecutionAsync(
        DomainAgent agent,
        ICrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(new TaskExecutionPlan(
            agent.Id, Array.Empty<PlannedStep>(), TimeSpan.Zero, 1.0));

    public System.Threading.Tasks.Task<bool> CanExecuteTaskAsync(
        DomainAgent agent,
        ICrewTask task,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(true);
}
