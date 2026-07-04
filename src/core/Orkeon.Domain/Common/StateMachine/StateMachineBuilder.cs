namespace Orkeon.Domain.Common.StateMachine;

/// <summary>
/// Fluent builder for constructing a <see cref="StateMachine{TState, TEvent}"/>.
/// </summary>
/// <typeparam name="TState">State type.</typeparam>
/// <typeparam name="TEvent">Event type.</typeparam>
public sealed class StateMachineBuilder<TState, TEvent>
    where TState : notnull
    where TEvent : notnull
{
    private TState? _initialState;
    private readonly List<TransitionDefinition<TState, TEvent>> _definitions = [];
    private readonly HashSet<string> _terminalStates = [];
    private TState? _degradedState;
    private CircuitBreakerPolicy _circuitPolicy = CircuitBreakerPolicy.Default;
    private Func<TState, string>? _stateKeyFunc;
    private Func<TEvent, string>? _eventKeyFunc;

    /// <summary>Sets the initial state of the machine.</summary>
    public StateMachineBuilder<TState, TEvent> WithInitialState(TState state)
    {
        _initialState = state;
        return this;
    }

    /// <summary>Marks one or more states as terminal (no outgoing transitions).</summary>
    public StateMachineBuilder<TState, TEvent> WithTerminalStates(params TState[] states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var keyFunc = _stateKeyFunc ?? (s => s.ToString()!);
        foreach (var s in states)
            _terminalStates.Add(keyFunc(s));
        return this;
    }

    /// <summary>
    /// Sets the degraded state to transition to when the circuit breaker trips.
    /// Requires <see cref="CircuitBreakerPolicy.UseDegradedMode"/> = true.
    /// </summary>
    public StateMachineBuilder<TState, TEvent> WithDegradedState(TState state)
    {
        _degradedState = state;
        return this;
    }

    /// <summary>Sets the circuit breaker policy.</summary>
    public StateMachineBuilder<TState, TEvent> WithCircuitBreaker(CircuitBreakerPolicy policy)
    {
        _circuitPolicy = policy;
        return this;
    }

    /// <summary>
    /// Custom key extraction for states (useful for value objects where ToString isn't the key).
    /// </summary>
    public StateMachineBuilder<TState, TEvent> WithStateKey(Func<TState, string> keyFunc)
    {
        _stateKeyFunc = keyFunc;
        return this;
    }

    /// <summary>
    /// Custom key extraction for events.
    /// </summary>
    public StateMachineBuilder<TState, TEvent> WithEventKey(Func<TEvent, string> keyFunc)
    {
        _eventKeyFunc = keyFunc;
        return this;
    }

    /// <summary>
    /// Begins defining a transition from a source state on a given event.
    /// </summary>
    public StateMachineTransitionBuilder<TState, TEvent> When(TState from, TEvent trigger)
        => new(this, from, trigger);

    /// <summary>Registers a fully-built transition definition (used by the transition sub-builder).</summary>
    internal void AddDefinition(TransitionDefinition<TState, TEvent> definition)
        => _definitions.Add(definition);

    /// <summary>
    /// Adds a simple transition without guards or actions.
    /// </summary>
    public StateMachineBuilder<TState, TEvent> AddTransition(TState from, TEvent trigger, TState to)
    {
        _definitions.Add(new TransitionDefinition<TState, TEvent>
        {
            From = from,
            Trigger = trigger,
            To = to
        });
        return this;
    }

    /// <summary>
    /// Builds the state machine. Validates the configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">When the initial state is not set.</exception>
    public StateMachine<TState, TEvent> Build()
    {
        if (_initialState == null)
            throw new InvalidOperationException("Initial state must be set via WithInitialState().");

        // Group transitions by (stateKey, eventKey)
        var stateKeyFunc = _stateKeyFunc ?? (s => s.ToString()!);
        var eventKeyFunc = _eventKeyFunc ?? (e => e.ToString()!);

        var grouped = new Dictionary<(string, string), List<TransitionDefinition<TState, TEvent>>>();
        foreach (var def in _definitions)
        {
            var key = (stateKeyFunc(def.From), eventKeyFunc(def.Trigger));
            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped[key] = list;
            }
            list.Add(def);
        }

        return new StateMachine<TState, TEvent>(
            _initialState,
            grouped,
            _terminalStates,
            _degradedState,
            _circuitPolicy,
            _stateKeyFunc,
            _eventKeyFunc);
    }

}

/// <summary>
/// Fluent sub-builder for a single transition definition.
/// </summary>
/// <typeparam name="TState">State type.</typeparam>
/// <typeparam name="TEvent">Event type.</typeparam>
public sealed class StateMachineTransitionBuilder<TState, TEvent>
    where TState : notnull
    where TEvent : notnull
{
    private readonly StateMachineBuilder<TState, TEvent> _parent;
    private readonly TState _from;
    private readonly TEvent _trigger;
    private TState? _to;
    private Func<object?, bool>? _guard;
    private string? _guardDescription;
    private Action<TState, TState, object?>? _onTransition;

    internal StateMachineTransitionBuilder(StateMachineBuilder<TState, TEvent> parent, TState from, TEvent trigger)
    {
        _parent = parent;
        _from = from;
        _trigger = trigger;
    }

    /// <summary>Sets the target state.</summary>
    public StateMachineTransitionBuilder<TState, TEvent> TransitionTo(TState to)
    {
        _to = to;
        return this;
    }

    /// <summary>Adds a guard predicate (no context).</summary>
    public StateMachineTransitionBuilder<TState, TEvent> WithGuard(Func<bool> guard, string? description = null)
    {
        _guard = _ => guard();
        _guardDescription = description;
        return this;
    }

    /// <summary>Adds a typed guard predicate that receives the context.</summary>
    public StateMachineTransitionBuilder<TState, TEvent> WithGuard<TContext>(Func<TContext, bool> guard, string? description = null)
    {
        _guard = ctx => ctx is TContext typed && guard(typed);
        _guardDescription = description;
        return this;
    }

    /// <summary>Adds an action to execute on transition.</summary>
    public StateMachineTransitionBuilder<TState, TEvent> WithAction(Action action)
    {
        _onTransition = (_, _, _) => action();
        return this;
    }

    /// <summary>Adds a typed action to execute on transition.</summary>
    public StateMachineTransitionBuilder<TState, TEvent> WithAction(Action<TState, TState> action)
    {
        _onTransition = (from, to, _) => action(from, to);
        return this;
    }

    /// <summary>Completes this transition definition and returns to the parent builder.</summary>
    public StateMachineBuilder<TState, TEvent> Done()
    {
        if (_to == null)
            throw new InvalidOperationException("Target state must be set via TransitionTo().");

        _parent.AddDefinition(new TransitionDefinition<TState, TEvent>
        {
            From = _from,
            Trigger = _trigger,
            To = _to,
            Guard = _guard,
            GuardDescription = _guardDescription,
            OnTransition = _onTransition
        });

        return _parent;
    }
}
