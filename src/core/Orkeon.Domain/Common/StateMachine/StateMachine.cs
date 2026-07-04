using System.Collections.Concurrent;

namespace Orkeon.Domain.Common.StateMachine;

/// <summary>
/// Generic finite state machine with circuit breaker protection.
/// Thread-safe for concurrent Fire() calls.
/// </summary>
/// <typeparam name="TState">State type (enum, string, or value object).</typeparam>
/// <typeparam name="TEvent">Event/trigger type.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724", Justification = "StateMachine<TState,TEvent> is the canonical FSM type; the Orkeon.Domain.Common.StateMachine namespace groups its supporting types. Renaming would break the public API.")]
public sealed class StateMachine<TState, TEvent> : IMutableStateMachine<TState, TEvent>
    where TState : notnull
    where TEvent : notnull
{
    private readonly Dictionary<(string state, string trigger), List<TransitionDefinition<TState, TEvent>>> _transitions;
    private readonly HashSet<string> _terminalStates;
    private readonly TState? _degradedState;
    private readonly CircuitBreakerPolicy _circuitPolicy;
    private readonly Func<TState, string> _stateKey;
    private readonly Func<TEvent, string> _eventKey;

    // Mutable state (protected by lock)
    private readonly object _lock = new();
    private TState _currentState;
    private int _transitionCount;
    private DateTime _lastTransitionTime;
    private DateTime _startTime;
    private bool _started;
    private bool _circuitBroken;
    private string? _circuitBrokenReason;
    private readonly ConcurrentDictionary<string, int> _stateVisitCounts = new();

    /// <inheritdoc />
    public TState CurrentState
    {
        get { lock (_lock) return _currentState; }
    }

    /// <inheritdoc />
    public bool IsTerminal
    {
        get { lock (_lock) return _terminalStates.Contains(_stateKey(_currentState)); }
    }

    /// <inheritdoc />
    public int TransitionCount
    {
        get { lock (_lock) return _transitionCount; }
    }

    /// <inheritdoc />
    public bool IsCircuitBroken
    {
        get { lock (_lock) return _circuitBroken; }
    }

    /// <inheritdoc />
    public CircuitBreakerStatus CircuitStatus
    {
        get
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var stateKey = _stateKey(_currentState);
                _stateVisitCounts.TryGetValue(stateKey, out var visits);

                return new CircuitBreakerStatus
                {
                    IsBroken = _circuitBroken,
                    BrokenReason = _circuitBrokenReason,
                    TotalTransitions = _transitionCount,
                    TimeInCurrentState = now - _lastTransitionTime,
                    CurrentStateVisitCount = visits,
                    TotalElapsed = _started ? now - _startTime : TimeSpan.Zero,
                    StateVisitHistogram = new Dictionary<string, int>(_stateVisitCounts)
                };
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<TransitionResult<TState, TEvent>>? OnTransition;

    /// <inheritdoc />
    public event EventHandler<CircuitBreakerStatus>? OnCircuitBroken;

    internal StateMachine(
        TState initialState,
        Dictionary<(string, string), List<TransitionDefinition<TState, TEvent>>> transitions,
        HashSet<string> terminalStates,
        TState? degradedState,
        CircuitBreakerPolicy circuitPolicy,
        Func<TState, string>? stateKey,
        Func<TEvent, string>? eventKey)
    {
        _currentState = initialState;
        _transitions = transitions;
        _terminalStates = terminalStates;
        _degradedState = degradedState;
        _circuitPolicy = circuitPolicy;
        _stateKey = stateKey ?? (s => s.ToString()!);
        _eventKey = eventKey ?? (e => e.ToString()!);
        _lastTransitionTime = DateTime.UtcNow;

        // Record initial state visit
        var key = _stateKey(initialState);
        _stateVisitCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
    }

    /// <inheritdoc />
    public IReadOnlyList<TEvent> GetPermittedEvents()
    {
        lock (_lock)
        {
            var stateKey = _stateKey(_currentState);
            var permitted = new List<TEvent>();

            foreach (var ((s, _), definitions) in _transitions)
            {
                if (s != stateKey) continue;
                foreach (var def in definitions)
                {
                    if (def.Guard == null || def.Guard(null))
                    {
                        permitted.Add(def.Trigger);
                    }
                }
            }

            return permitted;
        }
    }

    /// <inheritdoc />
    public bool CanFire(TEvent trigger)
    {
        lock (_lock)
        {
            if (_circuitBroken) return false;
            if (_terminalStates.Contains(_stateKey(_currentState))) return false;

            var key = (_stateKey(_currentState), _eventKey(trigger));
            if (!_transitions.TryGetValue(key, out var definitions)) return false;

            return definitions.Exists(d => d.Guard == null || d.Guard(null));
        }
    }

    /// <inheritdoc />
    public TransitionResult<TState, TEvent> Fire(TEvent trigger)
        => FireInternal(trigger, null);

    /// <inheritdoc />
    public TransitionResult<TState, TEvent> Fire<TContext>(TEvent trigger, TContext context)
        => FireInternal(trigger, context);

    /// <inheritdoc />
    public bool TryFire(TEvent trigger, out TransitionResult<TState, TEvent>? result)
        => TryFireInternal(trigger, null, out result);

    /// <inheritdoc />
    public bool TryFire<TContext>(TEvent trigger, TContext context, out TransitionResult<TState, TEvent>? result)
        => TryFireInternal(trigger, context, out result);

    /// <inheritdoc />
    public void ResetCircuitBreaker()
    {
        lock (_lock)
        {
            _circuitBroken = false;
            _circuitBrokenReason = null;
            _transitionCount = 0;
            _stateVisitCounts.Clear();
            var key = _stateKey(_currentState);
            _stateVisitCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
            _lastTransitionTime = DateTime.UtcNow;
            _startTime = DateTime.UtcNow;
        }
    }

    private bool TryFireInternal(TEvent trigger, object? context, out TransitionResult<TState, TEvent>? result)
    {
        try
        {
            result = FireInternal(trigger, context);
            return true;
        }
        catch (CircuitBrokenException)
        {
            result = null;
            return false;
        }
        catch (InvalidTransitionException<TState, TEvent>)
        {
            result = null;
            return false;
        }
    }

    private TransitionResult<TState, TEvent> FireInternal(TEvent trigger, object? context)
    {
        lock (_lock)
        {
            // 1. Check circuit breaker
            var breakReason = CheckCircuitBreaker();
            if (breakReason != null)
            {
                TripCircuit(breakReason);

                if (_circuitPolicy.UseDegradedMode && _degradedState != null)
                {
                    return TransitionToDegraded(trigger);
                }

                throw new CircuitBrokenException(CircuitStatus);
            }

            // 2. Check terminal state
            var stateKey = _stateKey(_currentState);
            if (_terminalStates.Contains(stateKey))
            {
                throw new InvalidTransitionException<TState, TEvent>(_currentState, trigger);
            }

            // 3. Find matching transition
            var lookupKey = (stateKey, _eventKey(trigger));
            if (!_transitions.TryGetValue(lookupKey, out var definitions))
            {
                throw new InvalidTransitionException<TState, TEvent>(_currentState, trigger);
            }

            TransitionDefinition<TState, TEvent>? matchedDef = null;
            foreach (var def in definitions)
            {
                if (def.Guard == null || def.Guard(context))
                {
                    matchedDef = def;
                    break;
                }
            }

            if (matchedDef == null)
            {
                throw new InvalidTransitionException<TState, TEvent>(_currentState, trigger);
            }

            // 4. Execute transition
            var fromState = _currentState;
            _currentState = matchedDef.To;

            if (!_started)
            {
                _startTime = DateTime.UtcNow;
                _started = true;
            }

            _transitionCount++;
            _lastTransitionTime = DateTime.UtcNow;

            // Track state visit
            var newKey = _stateKey(_currentState);
            _stateVisitCounts.AddOrUpdate(newKey, 1, (_, c) => c + 1);

            // 5. Execute action
            matchedDef.OnTransition?.Invoke(fromState, _currentState, context);

            // 6. Build result
            var result = new TransitionResult<TState, TEvent>
            {
                FromState = fromState,
                ToState = _currentState,
                Trigger = trigger,
                TransitionOrdinal = _transitionCount,
                Timestamp = _lastTransitionTime
            };

            // 7. Raise event (outside lock would be better but acceptable for simplicity)
            OnTransition?.Invoke(this, result);

            return result;
        }
    }

    private string? CheckCircuitBreaker()
    {
        if (_circuitBroken)
            return _circuitBrokenReason;

        // Max transitions
        if (_transitionCount >= _circuitPolicy.MaxTransitions)
            return $"Max transitions exceeded ({_transitionCount}/{_circuitPolicy.MaxTransitions})";

        // State timeout
        if (_circuitPolicy.StateTimeout > TimeSpan.Zero)
        {
            var timeInState = DateTime.UtcNow - _lastTransitionTime;
            if (timeInState > _circuitPolicy.StateTimeout)
                return $"State timeout exceeded ({timeInState.TotalSeconds:F1}s > {_circuitPolicy.StateTimeout.TotalSeconds:F1}s in state '{_stateKey(_currentState)}')";
        }

        // Max state visits (cycle detection)
        if (_circuitPolicy.MaxStateVisits > 0)
        {
            var stateKey = _stateKey(_currentState);
            if (_stateVisitCounts.TryGetValue(stateKey, out var visits) && visits > _circuitPolicy.MaxStateVisits)
                return $"Cycle detected: state '{stateKey}' visited {visits} times (max: {_circuitPolicy.MaxStateVisits})";
        }

        // Max total duration
        if (_started && _circuitPolicy.MaxTotalDuration > TimeSpan.Zero)
        {
            var elapsed = DateTime.UtcNow - _startTime;
            if (elapsed > _circuitPolicy.MaxTotalDuration)
                return $"Max total duration exceeded ({elapsed.TotalMinutes:F1}min > {_circuitPolicy.MaxTotalDuration.TotalMinutes:F1}min)";
        }

        return null;
    }

    private void TripCircuit(string reason)
    {
        _circuitBroken = true;
        _circuitBrokenReason = reason;
        OnCircuitBroken?.Invoke(this, CircuitStatus);
    }

    private TransitionResult<TState, TEvent> TransitionToDegraded(TEvent trigger)
    {
        var fromState = _currentState;
        _currentState = _degradedState!;
        _transitionCount++;
        _lastTransitionTime = DateTime.UtcNow;

        var newKey = _stateKey(_currentState);
        _stateVisitCounts.AddOrUpdate(newKey, 1, (_, c) => c + 1);

        return new TransitionResult<TState, TEvent>
        {
            FromState = fromState,
            ToState = _currentState,
            Trigger = trigger,
            TransitionOrdinal = _transitionCount,
            Timestamp = _lastTransitionTime
        };
    }
}
