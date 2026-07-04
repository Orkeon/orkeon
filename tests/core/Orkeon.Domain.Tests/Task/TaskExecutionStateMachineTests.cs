using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Task;

namespace Orkeon.Domain.Tests.Task;

/// <summary>
/// Tests for the TaskExecutionStateMachine specialization.
/// Validates the full task lifecycle, guard conditions, and circuit breaker integration.
/// </summary>
public class TaskExecutionStateMachineTests
{
    private static readonly TaskExecutionGuardContext DefaultCtx = new()
    {
        RetryCount = 0,
        MaxRetries = 3,
        ToolCallCount = 0,
        MaxToolCallsPerRound = 10,
        ValidationAttempts = 0,
        MaxValidationRetries = 3,
        IsToolRegistered = true
    };

    [Fact]
    public void HappyPath_AssignedToCompleted()
    {
        var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);

        Assert.Equal(TaskExecutionState.Assigned, sm.CurrentState);

        sm.Fire(TaskExecutionEvent.StartPlanning);
        Assert.Equal(TaskExecutionState.Planning, sm.CurrentState);

        sm.Fire(TaskExecutionEvent.BeginExecution);
        Assert.Equal(TaskExecutionState.Executing, sm.CurrentState);

        sm.Fire(TaskExecutionEvent.SubmitForValidation);
        Assert.Equal(TaskExecutionState.Validating, sm.CurrentState);

        sm.Fire(TaskExecutionEvent.ValidationPassed);
        Assert.Equal(TaskExecutionState.Completed, sm.CurrentState);
        Assert.True(sm.IsTerminal);
    }

    [Fact]
    public void SkipPlanning_DirectExecution()
    {
        var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);

        sm.Fire(TaskExecutionEvent.BeginExecution);
        Assert.Equal(TaskExecutionState.Executing, sm.CurrentState);
    }

    [Fact]
    public void ToolCallCycle_WithinBudget()
    {
        var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
        sm.Fire(TaskExecutionEvent.BeginExecution);

        // Tool call round-trip
        sm.Fire(TaskExecutionEvent.RequestToolCall, DefaultCtx);
        Assert.Equal(TaskExecutionState.ToolCalling, sm.CurrentState);

        sm.Fire(TaskExecutionEvent.ToolCallCompleted);
        Assert.Equal(TaskExecutionState.Executing, sm.CurrentState);

        // Second tool call
        sm.Fire(TaskExecutionEvent.RequestToolCall, DefaultCtx);
        sm.Fire(TaskExecutionEvent.ToolCallCompleted);
        Assert.Equal(TaskExecutionState.Executing, sm.CurrentState);
    }

    [Fact]
    public void ToolCall_OverBudget_Blocked()
    {
        var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
        sm.Fire(TaskExecutionEvent.BeginExecution);

        var overBudgetCtx = DefaultCtx with { ToolCallCount = 10, MaxToolCallsPerRound = 10 };

        Assert.Throws<InvalidTransitionException<TaskExecutionState, TaskExecutionEvent>>(
            () => sm.Fire(TaskExecutionEvent.RequestToolCall, overBudgetCtx));
    }

    [Fact]
    public void ToolCall_UnregisteredTool_Blocked()
    {
        var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
        sm.Fire(TaskExecutionEvent.BeginExecution);

        var unregisteredCtx = DefaultCtx with { IsToolRegistered = false };

        Assert.Throws<InvalidTransitionException<TaskExecutionState, TaskExecutionEvent>>(
            () => sm.Fire(TaskExecutionEvent.RequestToolCall, unregisteredCtx));
    }

    [Fact]
    public void ValidationFailed_RetryWithinLimit()
    {
        var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
        sm.Fire(TaskExecutionEvent.BeginExecution);
        sm.Fire(TaskExecutionEvent.SubmitForValidation);

        var ctx = DefaultCtx with { ValidationAttempts = 1 };
        sm.Fire(TaskExecutionEvent.ValidationFailed, ctx);
        Assert.Equal(TaskExecutionState.Executing, sm.CurrentState);
    }

    [Fact]
    public void ValidationFailed_OverLimit_Blocked()
    {
        var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
        sm.Fire(TaskExecutionEvent.BeginExecution);
        sm.Fire(TaskExecutionEvent.SubmitForValidation);

        var overLimitCtx = DefaultCtx with { ValidationAttempts = 3, MaxValidationRetries = 3 };

        Assert.Throws<InvalidTransitionException<TaskExecutionState, TaskExecutionEvent>>(
            () => sm.Fire(TaskExecutionEvent.ValidationFailed, overLimitCtx));
    }

    [Fact]
    public void FailAndRetry_WithinLimit()
    {
        var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
        sm.Fire(TaskExecutionEvent.BeginExecution);
        sm.Fire(TaskExecutionEvent.Fail);
        Assert.Equal(TaskExecutionState.Failed, sm.CurrentState);

        var retryCtx = DefaultCtx with { RetryCount = 1, MaxRetries = 3 };
        sm.Fire(TaskExecutionEvent.Retry, retryCtx);
        Assert.Equal(TaskExecutionState.Executing, sm.CurrentState);
    }

    [Fact]
    public void FailAndRetry_OverLimit_Blocked()
    {
        var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
        sm.Fire(TaskExecutionEvent.BeginExecution);
        sm.Fire(TaskExecutionEvent.Fail);

        var overLimitCtx = DefaultCtx with { RetryCount = 3, MaxRetries = 3 };

        Assert.Throws<InvalidTransitionException<TaskExecutionState, TaskExecutionEvent>>(
            () => sm.Fire(TaskExecutionEvent.Retry, overLimitCtx));
    }

    [Fact]
    public void HumanInputCycle()
    {
        var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
        sm.Fire(TaskExecutionEvent.BeginExecution);
        sm.Fire(TaskExecutionEvent.RequestHumanInput);
        Assert.Equal(TaskExecutionState.WaitingForHumanInput, sm.CurrentState);

        sm.Fire(TaskExecutionEvent.HumanInputReceived);
        Assert.Equal(TaskExecutionState.Executing, sm.CurrentState);
    }

    [Fact]
    public void Cancel_FromAnyNonTerminalState()
    {
        var states = new[]
        {
            (TaskExecutionEvent.Cancel, TaskExecutionState.Assigned),           // from Assigned
        };

        foreach (var (cancelEvent, _) in states)
        {
            var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
            sm.Fire(cancelEvent);
            Assert.Equal(TaskExecutionState.Cancelled, sm.CurrentState);
            Assert.True(sm.IsTerminal);
        }

        // Cancel from Executing
        var sm2 = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
        sm2.Fire(TaskExecutionEvent.BeginExecution);
        sm2.Fire(TaskExecutionEvent.Cancel);
        Assert.Equal(TaskExecutionState.Cancelled, sm2.CurrentState);

        // Cancel from Failed
        var sm3 = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
        sm3.Fire(TaskExecutionEvent.BeginExecution);
        sm3.Fire(TaskExecutionEvent.Fail);
        sm3.Fire(TaskExecutionEvent.Cancel);
        Assert.Equal(TaskExecutionState.Cancelled, sm3.CurrentState);
    }

    [Fact]
    public void CircuitBreaker_TripsOnExcessiveRetryLoop()
    {
        // Simulate: Execute → Fail → Retry loop with strict circuit breaker
        var sm = TaskExecutionStateMachine.Create(new CircuitBreakerPolicy
        {
            MaxTransitions = 10,
            StateTimeout = TimeSpan.Zero,
            MaxStateVisits = 4, // Executing visited max 4 times
            MaxTotalDuration = TimeSpan.Zero,
            UseDegradedMode = true
        });

        sm.Fire(TaskExecutionEvent.BeginExecution);  // Assigned→Executing (visit 1)
        sm.Fire(TaskExecutionEvent.Fail);             // Executing→Failed
        sm.Fire(TaskExecutionEvent.Retry, DefaultCtx);// Failed→Executing (visit 2)
        sm.Fire(TaskExecutionEvent.Fail);             // Executing→Failed
        sm.Fire(TaskExecutionEvent.Retry, DefaultCtx with { RetryCount = 1 }); // Failed→Executing (visit 3)
        sm.Fire(TaskExecutionEvent.Fail);             // Executing→Failed
        sm.Fire(TaskExecutionEvent.Retry, DefaultCtx with { RetryCount = 2 }); // Failed→Executing (visit 4)
        sm.Fire(TaskExecutionEvent.Fail);             // Executing→Failed

        // Next retry would make Executing visit 5 > 4. But circuit checks current state (Failed).
        // Failed has been visited 4 times too. Check: visits > MaxStateVisits.
        // Failed: 4 visits, max 4 → not exceeded (> not >=)
        sm.Fire(TaskExecutionEvent.Retry, DefaultCtx with { RetryCount = 2 }); // Failed→Executing (visit 5)

        // Now Executing has 5 visits > 4. Next fire from Executing should trip.
        var result = sm.Fire(TaskExecutionEvent.Fail); // Should go to Degraded
        Assert.Equal(TaskExecutionState.Degraded, result.ToState);
        Assert.True(sm.IsCircuitBroken);
        Assert.True(sm.IsTerminal);
    }

    [Fact]
    public void ToolCallFailed_ReturnsToExecuting()
    {
        var sm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Permissive);
        sm.Fire(TaskExecutionEvent.BeginExecution);
        sm.Fire(TaskExecutionEvent.RequestToolCall, DefaultCtx);
        sm.Fire(TaskExecutionEvent.ToolCallFailed);
        Assert.Equal(TaskExecutionState.Executing, sm.CurrentState);
    }
}
