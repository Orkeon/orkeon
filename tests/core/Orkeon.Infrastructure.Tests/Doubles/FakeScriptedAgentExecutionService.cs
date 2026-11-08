using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IAgentExecutionService"/> that plays a script: the n-th call answers the
/// n-th step (the last step answers every call after it). A step returns a result or throws. The
/// calls are counted, and the cancellation token each one received is kept.
/// </summary>
public sealed class FakeScriptedAgentExecutionService : IAgentExecutionService
{
    private readonly List<Func<CancellationToken, TaskResult>> _steps;
    private int _calls;

    /// <summary>Creates a service that plays <paramref name="steps"/>.</summary>
    public FakeScriptedAgentExecutionService(params Func<CancellationToken, TaskResult>[] steps) => _steps = [.. steps];

    /// <summary>How many calls reached the service, typed ones included.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>How many typed calls still fail before one succeeds.</summary>
    public int TypedFailuresLeft { get; set; }

    /// <summary>The reason a failing typed call ends with.</summary>
    public AgentExitReason TypedReason { get; set; } = AgentExitReason.LlmCallFailed;

    /// <summary>A step that fails with <paramref name="error"/>.</summary>
    public static Func<CancellationToken, TaskResult> Fail(string error = "boom", AgentExitReason reason = AgentExitReason.LlmCallFailed) =>
        _ => new TaskResult(false, "", null, [], TimeSpan.Zero, error) { ExitReason = reason };

    /// <summary>A step that succeeds with <paramref name="output"/>.</summary>
    public static Func<CancellationToken, TaskResult> Ok(string output = "done") =>
        _ => new TaskResult(true, output, null, [], TimeSpan.Zero);

    /// <summary>A step that throws <paramref name="exception"/>.</summary>
    public static Func<CancellationToken, TaskResult> Throw(Exception exception) =>
        _ => throw exception;

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _calls);
        var step = _steps[Math.Min(call, _steps.Count) - 1];
        try
        {
            return System.Threading.Tasks.Task.FromResult(step(cancellationToken));
        }
        catch (Exception ex)
        {
            return System.Threading.Tasks.Task.FromException<TaskResult>(ex);
        }
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class
    {
        Interlocked.Increment(ref _calls);
        return System.Threading.Tasks.Task.FromResult(
            TypedFailuresLeft-- > 0
                ? new TaskResult<TOutput>(false, "", null, [], TimeSpan.Zero, "typed boom") { ExitReason = TypedReason }
                : new TaskResult<TOutput>(true, "typed", null, [], TimeSpan.Zero));
    }
}
