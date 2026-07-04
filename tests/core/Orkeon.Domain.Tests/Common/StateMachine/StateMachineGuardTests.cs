using Orkeon.Domain.Common.StateMachine;

namespace Orkeon.Domain.Tests.Common.StateMachine;

/// <summary>
/// Tests for guard-based transitions.
/// </summary>
public class StateMachineGuardTests
{
    private enum State { Idle, Processing, Done }
    private enum Event { Process, Complete }
    private record ProcessContext(bool IsAuthorized, int Priority);

    [Fact]
    public void Guard_WhenTrue_AllowsTransition()
    {
        var sm = new StateMachineBuilder<State, Event>()
            .WithInitialState(State.Idle)
            .WithCircuitBreaker(CircuitBreakerPolicy.Permissive)
            .When(State.Idle, Event.Process)
                .TransitionTo(State.Processing)
                .WithGuard<ProcessContext>(ctx => ctx.IsAuthorized, "Must be authorized")
                .Done()
            .Build();

        var result = sm.Fire(Event.Process, new ProcessContext(IsAuthorized: true, Priority: 1));
        Assert.Equal(State.Processing, result.ToState);
    }

    [Fact]
    public void Guard_WhenFalse_Throws()
    {
        var sm = new StateMachineBuilder<State, Event>()
            .WithInitialState(State.Idle)
            .WithCircuitBreaker(CircuitBreakerPolicy.Permissive)
            .When(State.Idle, Event.Process)
                .TransitionTo(State.Processing)
                .WithGuard<ProcessContext>(ctx => ctx.IsAuthorized, "Must be authorized")
                .Done()
            .Build();

        Assert.Throws<InvalidTransitionException<State, Event>>(
            () => sm.Fire(Event.Process, new ProcessContext(IsAuthorized: false, Priority: 1)));
        Assert.Equal(State.Idle, sm.CurrentState); // unchanged
    }

    [Fact]
    public void Guard_MultipleTransitions_PicksFirstMatch()
    {
        // Two transitions from Idle on Process, with different guards
        var sm = new StateMachineBuilder<State, Event>()
            .WithInitialState(State.Idle)
            .WithCircuitBreaker(CircuitBreakerPolicy.Permissive)
            .When(State.Idle, Event.Process)
                .TransitionTo(State.Done) // high priority goes directly to Done
                .WithGuard<ProcessContext>(ctx => ctx.Priority > 5, "High priority")
                .Done()
            .When(State.Idle, Event.Process)
                .TransitionTo(State.Processing) // normal priority goes to Processing
                .WithGuard<ProcessContext>(ctx => ctx.IsAuthorized, "Authorized")
                .Done()
            .Build();

        // High priority → Done
        var result1 = sm.Fire(Event.Process, new ProcessContext(IsAuthorized: true, Priority: 10));
        Assert.Equal(State.Done, result1.ToState);
    }

    [Fact]
    public void Action_ExecutedOnTransition()
    {
        var actionCalled = false;
        State? fromCapture = null, toCapture = null;

        var sm = new StateMachineBuilder<State, Event>()
            .WithInitialState(State.Idle)
            .WithCircuitBreaker(CircuitBreakerPolicy.Permissive)
            .When(State.Idle, Event.Process)
                .TransitionTo(State.Processing)
                .WithAction((from, to) =>
                {
                    actionCalled = true;
                    fromCapture = from;
                    toCapture = to;
                })
                .Done()
            .Build();

        sm.Fire(Event.Process);

        Assert.True(actionCalled);
        Assert.Equal(State.Idle, fromCapture);
        Assert.Equal(State.Processing, toCapture);
    }
}
