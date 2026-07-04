using Orkeon.Domain.Common.StateMachine;

namespace Orkeon.Domain.Tests.Common.StateMachine;

/// <summary>
/// Tests for the generic StateMachine framework.
/// Uses a simple traffic light model for clarity.
/// </summary>
public class StateMachineTests
{
    private enum Light { Red, Yellow, Green }
    private enum Signal { Next, Emergency, Reset }

    private static StateMachine<Light, Signal> CreateTrafficLight()
    {
        return new StateMachineBuilder<Light, Signal>()
            .WithInitialState(Light.Red)
            .WithCircuitBreaker(CircuitBreakerPolicy.Permissive)
            .AddTransition(Light.Red, Signal.Next, Light.Green)
            .AddTransition(Light.Green, Signal.Next, Light.Yellow)
            .AddTransition(Light.Yellow, Signal.Next, Light.Red)
            .AddTransition(Light.Red, Signal.Emergency, Light.Red) // self-loop
            .AddTransition(Light.Green, Signal.Emergency, Light.Red)
            .AddTransition(Light.Yellow, Signal.Emergency, Light.Red)
            .Build();
    }

    [Fact]
    public void InitialState_IsSet()
    {
        var sm = CreateTrafficLight();
        Assert.Equal(Light.Red, sm.CurrentState);
        Assert.Equal(0, sm.TransitionCount);
        Assert.False(sm.IsCircuitBroken);
    }

    [Fact]
    public void Fire_ValidTransition_ChangesState()
    {
        var sm = CreateTrafficLight();
        var result = sm.Fire(Signal.Next);

        Assert.Equal(Light.Red, result.FromState);
        Assert.Equal(Light.Green, result.ToState);
        Assert.True(result.StateChanged);
        Assert.Equal(1, result.TransitionOrdinal);
        Assert.Equal(Light.Green, sm.CurrentState);
    }

    [Fact]
    public void Fire_FullCycle_ReturnsToInitial()
    {
        var sm = CreateTrafficLight();
        sm.Fire(Signal.Next); // Red → Green
        sm.Fire(Signal.Next); // Green → Yellow
        sm.Fire(Signal.Next); // Yellow → Red

        Assert.Equal(Light.Red, sm.CurrentState);
        Assert.Equal(3, sm.TransitionCount);
    }

    [Fact]
    public void Fire_InvalidTransition_Throws()
    {
        var sm = new StateMachineBuilder<Light, Signal>()
            .WithInitialState(Light.Red)
            .WithCircuitBreaker(CircuitBreakerPolicy.Permissive)
            .AddTransition(Light.Red, Signal.Next, Light.Green)
            .Build();

        sm.Fire(Signal.Next); // Red → Green
        Assert.Throws<InvalidTransitionException<Light, Signal>>(() => sm.Fire(Signal.Reset));
    }

    [Fact]
    public void TryFire_InvalidTransition_ReturnsFalse()
    {
        var sm = new StateMachineBuilder<Light, Signal>()
            .WithInitialState(Light.Red)
            .WithCircuitBreaker(CircuitBreakerPolicy.Permissive)
            .AddTransition(Light.Red, Signal.Next, Light.Green)
            .Build();

        sm.Fire(Signal.Next);
        var success = sm.TryFire(Signal.Reset, out var result);

        Assert.False(success);
        Assert.Null(result);
        Assert.Equal(Light.Green, sm.CurrentState); // unchanged
    }

    [Fact]
    public void TerminalState_BlocksTransitions()
    {
        var sm = new StateMachineBuilder<Light, Signal>()
            .WithInitialState(Light.Red)
            .WithTerminalStates(Light.Green)
            .WithCircuitBreaker(CircuitBreakerPolicy.Permissive)
            .AddTransition(Light.Red, Signal.Next, Light.Green)
            .AddTransition(Light.Green, Signal.Next, Light.Yellow)
            .Build();

        sm.Fire(Signal.Next); // Red → Green (terminal)
        Assert.True(sm.IsTerminal);
        Assert.Throws<InvalidTransitionException<Light, Signal>>(() => sm.Fire(Signal.Next));
    }

    [Fact]
    public void CanFire_ReturnsCorrectly()
    {
        var sm = CreateTrafficLight();

        Assert.True(sm.CanFire(Signal.Next));
        Assert.True(sm.CanFire(Signal.Emergency));
        Assert.False(sm.CanFire(Signal.Reset)); // no reset transition defined
    }

    [Fact]
    public void GetPermittedEvents_ReturnsAvailable()
    {
        var sm = CreateTrafficLight();
        var permitted = sm.GetPermittedEvents();

        Assert.Contains(Signal.Next, permitted);
        Assert.Contains(Signal.Emergency, permitted);
    }

    [Fact]
    public void OnTransition_EventRaised()
    {
        var sm = CreateTrafficLight();
        TransitionResult<Light, Signal>? captured = null;
        sm.OnTransition += (_, result) => captured = result;

        sm.Fire(Signal.Next);

        Assert.NotNull(captured);
        Assert.Equal(Light.Red, captured.FromState);
        Assert.Equal(Light.Green, captured.ToState);
    }
}
