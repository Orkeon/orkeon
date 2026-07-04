using Orkeon.Domain.Common.StateMachine;

namespace Orkeon.Domain.Task;

/// <summary>
/// Context passed to guards during task execution transitions.
/// </summary>
public sealed record TaskExecutionGuardContext
{
    /// <summary>Number of retries attempted so far.</summary>
    public int RetryCount { get; init; }

    /// <summary>Maximum retries allowed.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Number of tool calls made in the current execution round.</summary>
    public int ToolCallCount { get; init; }

    /// <summary>Maximum tool calls per execution round.</summary>
    public int MaxToolCallsPerRound { get; init; } = 10;

    /// <summary>Number of validation attempts.</summary>
    public int ValidationAttempts { get; init; }

    /// <summary>Maximum validation retry loops.</summary>
    public int MaxValidationRetries { get; init; } = 3;

    /// <summary>Whether the tool name is in the agent's registered tool list.</summary>
    public bool IsToolRegistered { get; init; } = true;

    /// <summary>Whether prerequisites are met for retry.</summary>
    public bool CanRetry => RetryCount < MaxRetries;

    /// <summary>Whether tool calls are within budget.</summary>
    public bool CanCallTool => ToolCallCount < MaxToolCallsPerRound && IsToolRegistered;

    /// <summary>Whether validation can be retried.</summary>
    public bool CanRetryValidation => ValidationAttempts < MaxValidationRetries;
}

/// <summary>
/// Pre-configured FSM for task execution lifecycle.
/// Models the runtime execution of a task by an agent, with circuit breaker
/// protection against infinite retry loops and tool call hallucinations.
/// </summary>
public static class TaskExecutionStateMachine
{
    /// <summary>
    /// Creates a new task execution FSM with the default circuit breaker policy.
    /// </summary>
    public static StateMachine<TaskExecutionState, TaskExecutionEvent> Create(
        CircuitBreakerPolicy? circuitPolicy = null)
    {
        return CreateBuilder(circuitPolicy ?? CircuitBreakerPolicy.Strict).Build();
    }

    /// <summary>
    /// Creates the builder for customization before building.
    /// </summary>
    public static StateMachineBuilder<TaskExecutionState, TaskExecutionEvent> CreateBuilder(
        CircuitBreakerPolicy? circuitPolicy = null)
    {
        var builder = new StateMachineBuilder<TaskExecutionState, TaskExecutionEvent>()
            .WithInitialState(TaskExecutionState.Assigned)
            .WithTerminalStates(
                TaskExecutionState.Completed,
                TaskExecutionState.Cancelled,
                TaskExecutionState.Degraded)
            .WithDegradedState(TaskExecutionState.Degraded)
            .WithCircuitBreaker(circuitPolicy ?? CircuitBreakerPolicy.Strict);

        // === Assigned ===
        builder
            .When(TaskExecutionState.Assigned, TaskExecutionEvent.StartPlanning)
                .TransitionTo(TaskExecutionState.Planning)
                .Done()
            .When(TaskExecutionState.Assigned, TaskExecutionEvent.BeginExecution)
                .TransitionTo(TaskExecutionState.Executing)
                .Done()
            .When(TaskExecutionState.Assigned, TaskExecutionEvent.Cancel)
                .TransitionTo(TaskExecutionState.Cancelled)
                .Done();

        // === Planning ===
        builder
            .When(TaskExecutionState.Planning, TaskExecutionEvent.BeginExecution)
                .TransitionTo(TaskExecutionState.Executing)
                .Done()
            .When(TaskExecutionState.Planning, TaskExecutionEvent.Cancel)
                .TransitionTo(TaskExecutionState.Cancelled)
                .Done();

        // === Executing ===
        builder
            .When(TaskExecutionState.Executing, TaskExecutionEvent.RequestToolCall)
                .TransitionTo(TaskExecutionState.ToolCalling)
                .WithGuard<TaskExecutionGuardContext>(
                    ctx => ctx.CanCallTool,
                    "Tool call within budget and tool is registered")
                .Done()
            .When(TaskExecutionState.Executing, TaskExecutionEvent.SubmitForValidation)
                .TransitionTo(TaskExecutionState.Validating)
                .Done()
            .When(TaskExecutionState.Executing, TaskExecutionEvent.RequestHumanInput)
                .TransitionTo(TaskExecutionState.WaitingForHumanInput)
                .Done()
            .When(TaskExecutionState.Executing, TaskExecutionEvent.Fail)
                .TransitionTo(TaskExecutionState.Failed)
                .Done()
            .When(TaskExecutionState.Executing, TaskExecutionEvent.Cancel)
                .TransitionTo(TaskExecutionState.Cancelled)
                .Done();

        // === ToolCalling ===
        builder
            .When(TaskExecutionState.ToolCalling, TaskExecutionEvent.ToolCallCompleted)
                .TransitionTo(TaskExecutionState.Executing)
                .Done()
            .When(TaskExecutionState.ToolCalling, TaskExecutionEvent.ToolCallFailed)
                .TransitionTo(TaskExecutionState.Executing)
                .Done()
            .When(TaskExecutionState.ToolCalling, TaskExecutionEvent.Cancel)
                .TransitionTo(TaskExecutionState.Cancelled)
                .Done();

        // === Validating ===
        builder
            .When(TaskExecutionState.Validating, TaskExecutionEvent.ValidationPassed)
                .TransitionTo(TaskExecutionState.Completed)
                .Done()
            .When(TaskExecutionState.Validating, TaskExecutionEvent.ValidationFailed)
                .TransitionTo(TaskExecutionState.Executing)
                .WithGuard<TaskExecutionGuardContext>(
                    ctx => ctx.CanRetryValidation,
                    "Validation retries within limit")
                .Done()
            .When(TaskExecutionState.Validating, TaskExecutionEvent.Fail)
                .TransitionTo(TaskExecutionState.Failed)
                .Done()
            .When(TaskExecutionState.Validating, TaskExecutionEvent.Cancel)
                .TransitionTo(TaskExecutionState.Cancelled)
                .Done();

        // === WaitingForHumanInput ===
        builder
            .When(TaskExecutionState.WaitingForHumanInput, TaskExecutionEvent.HumanInputReceived)
                .TransitionTo(TaskExecutionState.Executing)
                .Done()
            .When(TaskExecutionState.WaitingForHumanInput, TaskExecutionEvent.Cancel)
                .TransitionTo(TaskExecutionState.Cancelled)
                .Done();

        // === Failed ===
        builder
            .When(TaskExecutionState.Failed, TaskExecutionEvent.Retry)
                .TransitionTo(TaskExecutionState.Executing)
                .WithGuard<TaskExecutionGuardContext>(
                    ctx => ctx.CanRetry,
                    "Retry count within limit")
                .Done()
            .When(TaskExecutionState.Failed, TaskExecutionEvent.Cancel)
                .TransitionTo(TaskExecutionState.Cancelled)
                .Done();

        return builder;
    }
}
