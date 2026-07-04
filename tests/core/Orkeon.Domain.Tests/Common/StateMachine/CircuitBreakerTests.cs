using Orkeon.Domain.Common.StateMachine;

namespace Orkeon.Domain.Tests.Common.StateMachine;

/// <summary>
/// Tests for circuit breaker protection:
/// - Max transitions
/// - Cycle detection (max state visits)
/// - Degraded mode
/// - Circuit reset
/// </summary>
public class CircuitBreakerTests
{
    private enum State { A, B, C, Degraded }
    private enum Event { Next, Loop }

    [Fact]
    public void MaxTransitions_TripsCircuit()
    {
        var sm = new StateMachineBuilder<State, Event>()
            .WithInitialState(State.A)
            .WithCircuitBreaker(new CircuitBreakerPolicy
            {
                MaxTransitions = 3,
                StateTimeout = TimeSpan.Zero,
                MaxStateVisits = 0,
                MaxTotalDuration = TimeSpan.Zero
            })
            .AddTransition(State.A, Event.Next, State.B)
            .AddTransition(State.B, Event.Next, State.A)
            .Build();

        sm.Fire(Event.Next); // A→B (1)
        sm.Fire(Event.Next); // B→A (2)
        sm.Fire(Event.Next); // A→B (3, hits limit)

        var ex = Assert.Throws<CircuitBrokenException>(() => sm.Fire(Event.Next));
        Assert.Contains("Max transitions exceeded", ex.Status.BrokenReason);
        Assert.True(sm.IsCircuitBroken);
    }

    [Fact]
    public void CycleDetection_TripsOnRepeatedStateVisits()
    {
        var sm = new StateMachineBuilder<State, Event>()
            .WithInitialState(State.A)
            .WithCircuitBreaker(new CircuitBreakerPolicy
            {
                MaxTransitions = 100,
                StateTimeout = TimeSpan.Zero,
                MaxStateVisits = 3, // State A can be visited 3 times max
                MaxTotalDuration = TimeSpan.Zero
            })
            .AddTransition(State.A, Event.Next, State.B)
            .AddTransition(State.B, Event.Next, State.A)
            .Build();

        // A(1) → B → A(2) → B → A(3) → B → A(4, exceeds 3)
        sm.Fire(Event.Next); // A→B
        sm.Fire(Event.Next); // B→A (visit 2)
        sm.Fire(Event.Next); // A→B
        sm.Fire(Event.Next); // B→A (visit 3)
        sm.Fire(Event.Next); // A→B

        // Now at B, next would go to A (visit 4). But we check circuit on FIRE from current.
        // The check happens when state A has been visited > 3 times.
        // Actually at this point A has 3 visits, B has 3 visits.
        // Next fire from B→A would add visit 4 to A. But check runs BEFORE transition on current state.
        // Let's verify: currently at B with 3 visits. B has 3 visits which equals MaxStateVisits=3, not exceeding.
        sm.Fire(Event.Next); // B→A (visit 4 for A)

        // Now at A with 4 visits > 3. Next fire should trip.
        var ex = Assert.Throws<CircuitBrokenException>(() => sm.Fire(Event.Next));
        Assert.Contains("Cycle detected", ex.Status.BrokenReason);
    }

    [Fact]
    public void DegradedMode_TransitionsToDegradedState()
    {
        var sm = new StateMachineBuilder<State, Event>()
            .WithInitialState(State.A)
            .WithDegradedState(State.Degraded)
            .WithTerminalStates(State.Degraded)
            .WithCircuitBreaker(new CircuitBreakerPolicy
            {
                MaxTransitions = 2,
                StateTimeout = TimeSpan.Zero,
                MaxStateVisits = 0,
                MaxTotalDuration = TimeSpan.Zero,
                UseDegradedMode = true
            })
            .AddTransition(State.A, Event.Next, State.B)
            .AddTransition(State.B, Event.Next, State.A)
            .Build();

        sm.Fire(Event.Next); // A→B (1)
        sm.Fire(Event.Next); // B→A (2, hits limit)

        // Next fire should go to Degraded instead of throwing
        var result = sm.Fire(Event.Next);
        Assert.Equal(State.Degraded, result.ToState);
        Assert.True(sm.IsTerminal);
        Assert.True(sm.IsCircuitBroken);
    }

    [Fact]
    public void OnCircuitBroken_EventRaised()
    {
        CircuitBreakerStatus? capturedStatus = null;

        var sm = new StateMachineBuilder<State, Event>()
            .WithInitialState(State.A)
            .WithCircuitBreaker(new CircuitBreakerPolicy
            {
                MaxTransitions = 1,
                StateTimeout = TimeSpan.Zero,
                MaxStateVisits = 0,
                MaxTotalDuration = TimeSpan.Zero
            })
            .AddTransition(State.A, Event.Next, State.B)
            .AddTransition(State.B, Event.Next, State.A)
            .Build();

        sm.OnCircuitBroken += (_, status) => capturedStatus = status;

        sm.Fire(Event.Next); // A→B (1, hits limit)

        try { sm.Fire(Event.Next); } catch (CircuitBrokenException) { }

        Assert.NotNull(capturedStatus);
        Assert.True(capturedStatus.IsBroken);
    }

    [Fact]
    public void ResetCircuitBreaker_AllowsNewTransitions()
    {
        var sm = new StateMachineBuilder<State, Event>()
            .WithInitialState(State.A)
            .WithCircuitBreaker(new CircuitBreakerPolicy
            {
                MaxTransitions = 2,
                StateTimeout = TimeSpan.Zero,
                MaxStateVisits = 0,
                MaxTotalDuration = TimeSpan.Zero
            })
            .AddTransition(State.A, Event.Next, State.B)
            .AddTransition(State.B, Event.Next, State.A)
            .Build();

        sm.Fire(Event.Next); // A→B (1)
        sm.Fire(Event.Next); // B→A (2, hits limit)

        Assert.Throws<CircuitBrokenException>(() => sm.Fire(Event.Next));

        sm.ResetCircuitBreaker();

        Assert.False(sm.IsCircuitBroken);
        Assert.Equal(0, sm.TransitionCount);

        // Can fire again
        var result = sm.Fire(Event.Next);
        Assert.Equal(State.B, result.ToState);
    }

    [Fact]
    public void CircuitStatus_ReportsHistogram()
    {
        var sm = new StateMachineBuilder<State, Event>()
            .WithInitialState(State.A)
            .WithCircuitBreaker(CircuitBreakerPolicy.Permissive)
            .AddTransition(State.A, Event.Next, State.B)
            .AddTransition(State.B, Event.Next, State.C)
            .AddTransition(State.C, Event.Next, State.A)
            .Build();

        sm.Fire(Event.Next); // A→B
        sm.Fire(Event.Next); // B→C
        sm.Fire(Event.Next); // C→A

        var status = sm.CircuitStatus;
        Assert.NotNull(status.StateVisitHistogram);
        Assert.Equal(2, status.StateVisitHistogram["A"]); // initial + revisit
        Assert.Equal(1, status.StateVisitHistogram["B"]);
        Assert.Equal(1, status.StateVisitHistogram["C"]);
        Assert.Equal(3, status.TotalTransitions);
    }
}
