using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Crew;

/// <summary>
/// Gives a task the attempts its <c>retry:</c> grants (FWK-15). It sits between the checkpoint decorator
/// and the execution core: the strategies never retry, so the six modes share one rule, and only the
/// attempt that ended the task reaches the checkpoint.
/// </summary>
/// <remarks>
/// <para>
/// A new attempt runs the whole task again, tool calls included; an author whose tools write is
/// responsible for their idempotence. The wait before an attempt comes from the task's backoff on the
/// injected <see cref="TimeProvider"/> and ends at once when the run is cancelled; each retry is announced
/// by a <c>task.started</c> carrying its attempt number (the first attempt is announced by the strategy).
/// </para>
/// <para>
/// Never retried: cancellation, a refusal of the guardian, an exhausted budget. A failure that is not
/// one of these, a failed result or an exception, is retried until the attempts are used up; the last
/// result is then returned, or the last exception rethrown, as if the task had run once. Output
/// validation retries live inside an attempt.
/// </para>
/// </remarks>
public sealed partial class RetryingAgentExecutionService : IAgentExecutionService
{
    private readonly IAgentExecutionService _inner;
    private readonly ILogger<RetryingAgentExecutionService> _logger;
    private readonly TimeProvider _time;
    private readonly CrewHookDispatcher _hooks;

    /// <summary>Initializes a new instance of <see cref="RetryingAgentExecutionService"/>.</summary>
    /// <param name="inner">The execution service that runs one attempt.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="time">The clock the backoff waits on; the system clock when null.</param>
    /// <param name="hook">Told of each retry as a task start; nobody when null.</param>
    public RetryingAgentExecutionService(
        IAgentExecutionService inner,
        ILogger<RetryingAgentExecutionService> logger,
        TimeProvider? time = null,
        ICrewExecutionHook? hook = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(logger);
        _inner = inner;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _hooks = new CrewHookDispatcher(hook, logger);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        return task.Retry is not { Attempts: > 1 } policy
            ? _inner.ExecuteTaskAsync(agent, task, context, cancellationToken)
            : RunAsync(
                agent, task, policy,
                () => _inner.ExecuteTaskAsync(agent, task, context, cancellationToken),
                r => (r.Success, r.ExitReason),
                (r, retries) => r with { RetryCount = retries },
                cancellationToken);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        return task.Retry is not { Attempts: > 1 } policy
            ? _inner.ExecuteTaskAsync<TOutput>(agent, task, context, cancellationToken)
            : RunAsync(
                agent, task, policy,
                () => _inner.ExecuteTaskAsync<TOutput>(agent, task, context, cancellationToken),
                r => (r.Success, r.ExitReason),
                (r, retries) => r with { RetryCount = retries },
                cancellationToken);
    }

    private async System.Threading.Tasks.Task<TResult> RunAsync<TResult>(
        DomainAgent agent,
        ICrewTask task,
        TaskRetryPolicy policy,
        Func<System.Threading.Tasks.Task<TResult>> attemptOnce,
        Func<TResult, (bool Success, AgentExitReason Reason)> read,
        Func<TResult, int, TResult> stamp,
        CancellationToken ct)
    {
        var key = task.Key ?? task.TaskId.Value.ToString();
        for (var attempt = 1; ; attempt++)
        {
            if (attempt > 1)
            {
                await WaitAsync(policy.DelayBefore(attempt), ct).ConfigureAwait(false);
                await _hooks.TaskStartedAsync(
                    new TaskStartSnapshot
                    {
                        TaskId = task.TaskId.Value.ToString(),
                        AgentRole = agent.Role.Value,
                        StartedAt = _time.GetUtcNow(),
                        Attempt = attempt,
                    },
                    ct).ConfigureAwait(false);
            }

            // A retry replays the task from its start: its questions to a human are numbered afresh (FWK-23).
            Orkeon.Infrastructure.Checkpointing.TaskInputScope.Current?.BeginAttempt();

            TResult result;
            try
            {
                result = await attemptOnce().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < policy.Attempts && IsRetryable(ex) && !ct.IsCancellationRequested)
            {
                LogAttemptThrew(ex, key, attempt, policy.Attempts);
                continue;
            }
            catch (Exception) when (attempt > 1)
            {
                // The last attempt threw: the task ends here, and its end is reported with this attempt.
                NoteAttempt(task, attempt);
                throw;
            }

            var (success, reason) = read(result);
            if (success || attempt >= policy.Attempts || ct.IsCancellationRequested || IsFinal(reason) || RunIsSuspended())
            {
                NoteAttempt(task, attempt);
                return stamp(result, attempt - 1);
            }

            LogAttemptFailed(key, attempt, policy.Attempts);
        }
    }

    // Tells the run which attempt ended the task, for the events that report its end (task.completed.attempt).
    private static void NoteAttempt(ICrewTask task, int attempt)
    {
        if (attempt > 1 && Orkeon.Infrastructure.Orchestration.AsyncLocalRunScope.Ambient is { } run)
            run.TaskAttempts[task.TaskId.Value.ToString()] = attempt;
    }

    private async System.Threading.Tasks.Task WaitAsync(TimeSpan delay, CancellationToken ct)
    {
        if (delay > TimeSpan.Zero)
            await System.Threading.Tasks.Task.Delay(delay, _time, ct).ConfigureAwait(false);
        else
            ct.ThrowIfCancellationRequested();
    }

    // A refusal or a spent budget does not go away by asking again; neither does a cancellation.
    private static bool IsFinal(AgentExitReason reason) =>
        reason is AgentExitReason.Cancelled or AgentExitReason.BudgetExhausted or AgentExitReason.GuardianBlocked;

    // A run stopped on a question to a human (FWK-23): asking the task again would ask the human again.
    private static bool RunIsSuspended() =>
        Orkeon.Infrastructure.Orchestration.AsyncLocalRunScope.Ambient?.Suspension is not null;

    private static bool IsRetryable(Exception ex) =>
        ex is not OperationCanceledException
        && ex is not Orkeon.Application.Interfaces.Services.RunSuspendedException
#pragma warning disable ORKEXP002 // the exception is experimental; a retry must still recognise it as final
        && ex is not Orkeon.Domain.Autonomous.BudgetExhaustedException;
#pragma warning restore ORKEXP002

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {Key} failed on attempt {Attempt} of {Attempts}: it runs again")]
    private partial void LogAttemptFailed(string key, int attempt, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {Key} threw on attempt {Attempt} of {Attempts}: it runs again")]
    private partial void LogAttemptThrew(Exception ex, string key, int attempt, int attempts);
}
