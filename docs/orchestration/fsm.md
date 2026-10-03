> 🇫🇷 [Version française](../fr/orchestration/fsm.md)

> **See also**: [ProcessTypes comparison guide](./process-types.md) · [YAML schema](../architecture/yaml-schema.md) · [Back to index](../INDEX.md)

# Finite state machine (FSM) engine

## Overview

Orkeon provides a generic finite state machine framework (`StateMachine<TState, TEvent>`) in the Domain layer, with a built-in circuit breaker (`CircuitBreakerPolicy`) that stops a runaway loop. It is a building block for your own code and for three shipped consumers:

- **`orkeon forge`** runs its session cycle (brief → blueprint → render → validate → test → diagnose → verdict) on a `StateMachine<ForgeState, ForgeTrigger>` (`ForgeStateMachineFactory`).
- **The [Graph mode](./graph.md)** bounds its `StateGraph` runs with a `CircuitBreakerPolicy` built from the crew's `graphConfig`.
- **The corrective RAG graph** (`CorrectiveRagPipeline`) bounds its retrieve → evaluate → regenerate loop with its own `CircuitBreakerPolicy`.

> **No task state machine.** A crew's tasks do not run through an FSM. What bounds a task is its agent loop: the agent's `maxIter`, a stop after 3 consecutive identical tool errors, and the output-validation retries. The former task specialization (`TaskExecutionStateMachine`) and the YAML `circuitBreaker:` blocks that configured it were never run and have been removed; a crew that still writes `circuitBreaker:` is refused at load. In YAML, the only circuit-breaker setting is a Graph crew's `graphConfig` (see [YAML schema](../architecture/yaml-schema.md)).

## Architecture

The framework components live in `Orkeon.Domain.Common.StateMachine`:

| Class | Role |
|--------|------|
| `StateMachine<TState, TEvent>` | Thread-safe FSM engine (`lock`) with built-in circuit breaker |
| `StateMachineBuilder<TState, TEvent>` | Fluent API to declare the state graph |
| `CircuitBreakerPolicy` | Protection threshold configuration, presets `Strict` / `Default` / `Permissive` |
| `CircuitBreakerStatus` | Snapshot of the breaker (same file as the policy) |
| `TransitionResult<TState, TEvent>` | Result of a transition (states, trigger, ordinal, timestamp) |
| `IStateMachine<TState, TEvent>` | Read-only interface for observation |
| `IMutableStateMachine<TState, TEvent>` | Interface with `Fire()`, `TryFire()`, `ResetCircuitBreaker()`, events |
| `CircuitBrokenException`, `InvalidTransitionException<TState, TEvent>` | Tripped breaker; no transition for this state + event |

`CircuitBreakerPolicyFactory` (`Orkeon.Infrastructure.Configuration`) turns a Graph crew's `graphConfig` into a policy (`ResolveGraph`).

## Circuit breaker

The circuit breaker is built directly into `StateMachine<TState, TEvent>` and checks four conditions before each transition:

### Protection mechanisms

| Mechanism | Parameter | Description |
|-----------|-----------|-------------|
| Max transitions | `MaxTransitions` | Total number of allowed transitions |
| Per-state timeout | `StateTimeout` | Maximum time since the last transition (`TimeSpan.Zero` disables it) |
| Cycle detection | `MaxStateVisits` | Maximum number of visits to the same state (0 disables it) |
| Total duration | `MaxTotalDuration` | Maximum lifetime since the first transition (`TimeSpan.Zero` disables it) |

When a condition is violated, two behaviors are possible depending on `UseDegradedMode`:

- `false`: a `CircuitBrokenException` is thrown with a detailed `CircuitBreakerStatus`
- `true`: the machine transitions to the degraded state declared with `WithDegradedState(...)`; without a declared degraded state it throws as above

`TryFire()` returns `false` instead of throwing, for a tripped breaker as for a missing transition. A guard that rejects every candidate transition surfaces as an `InvalidTransitionException`. `ResetCircuitBreaker()` clears the trip and the counters.

### Presets

Three presets are provided via `CircuitBreakerPolicy`:

| Preset | MaxTransitions | StateTimeout | MaxStateVisits | MaxTotalDuration | DegradedMode |
|--------|---------------|-------------|----------------|------------------|--------------|
| `Strict` | 50 | 2 min | 5 | 10 min | true |
| `Default` | 100 | 5 min | 10 | 30 min | false |
| `Permissive` | 1000 | 30 min | 50 | 2 h | false |

`new CircuitBreakerPolicy()` equals `Default`. `Strict` is the fallback of `StateGraph` and of `CircuitBreakerPolicyFactory.ResolveGraph` when `graphConfig` names no preset (or an unknown one).

### Observability

The machine exposes two events:

- `OnTransition`: raised after each successful transition, with a `TransitionResult` (`FromState`, `ToState`, `Trigger`, `TransitionOrdinal`, `Timestamp`, `StateChanged`)
- `OnCircuitBroken`: raised when the circuit trips, with a `CircuitBreakerStatus` including a per-state visit histogram

`CircuitBreakerStatus` provides a complete snapshot: `IsBroken`, `BrokenReason`, `TotalTransitions`, `TimeInCurrentState`, `CurrentStateVisitCount`, `TotalElapsed`, `StateVisitHistogram`. The read-only interface also offers `CurrentState`, `IsTerminal`, `TransitionCount`, `GetPermittedEvents()` and `CanFire(event)`.

## Typed guards

A transition can carry a guard over a typed context — `WithGuard<TContext>(predicate, description)` — and an action. `Fire(event, context)` evaluates the guards of the candidate transitions in declaration order and takes the first that passes; when every guard rejects, the call raises an `InvalidTransitionException` (`TryFire` returns `false`).

## Usage in C# code

### Building a state machine

The framework lets you define any state graph:

```csharp
var fsm = new StateMachineBuilder<MyState, MyEvent>()
    .WithInitialState(MyState.Idle)
    .WithTerminalStates(MyState.Done, MyState.Error)
    .WithDegradedState(MyState.Error)
    .WithCircuitBreaker(new CircuitBreakerPolicy
    {
        MaxTransitions = 50,
        StateTimeout = TimeSpan.FromMinutes(2),
        MaxStateVisits = 5,
        UseDegradedMode = true
    })
    .When(MyState.Idle, MyEvent.Start)
        .TransitionTo(MyState.Processing)
        .WithGuard<MyContext>(ctx => ctx.IsReady, "Must be ready")
        .WithAction((from, to) => Log($"{from} -> {to}"))
        .Done()
    .When(MyState.Processing, MyEvent.Complete)
        .TransitionTo(MyState.Done)
        .Done()
    .Build();
```

`AddTransition(from, trigger, to)` is the shortcut for a transition without guard or action; `WithStateKey` / `WithEventKey` supply the key functions when `TState` / `TEvent` are not enums.

> The scripting DSL has its own `stateMachine()` literal (`fsm.d.ts`), a separate JavaScript implementation without the circuit breaker — see [Scripting](../architecture/scripting.md).

## Relationship with the existing code

### Lifecycle states

The lifecycle states of agents, tasks and crews (`AgentStatus`, `TaskStatus`, `CrewStatus`: Pending → InProgress → Completed…) are guarded by the aggregates themselves — a task that is not `Pending` refuses to start — and a run moves them as it goes ([Events](../architecture/domain-events.md#dispatch)). They do not use this engine.

### Process strategies

The `IProcessStrategy` implementations execute a task through `IAgentExecutionService` and its agent loop; none of them builds a state machine. The Graph mode is the one strategy that applies a `CircuitBreakerPolicy`, to its `StateGraph` runner.

## Tests

| Test file | Coverage |
|-----------------|-----------|
| `Orkeon.Domain.Tests/Common/StateMachine/StateMachineTests.cs` | Valid/invalid transitions, terminal states, complete cycle |
| `Orkeon.Domain.Tests/Common/StateMachine/StateMachineGuardTests.cs` | Typed guards, guard priority, on-transition actions |
| `Orkeon.Domain.Tests/Common/StateMachine/CircuitBreakerTests.cs` | Max transitions, cycle detection, degraded mode, reset, histogram |
| `Orkeon.Infrastructure.Tests/Configuration/CircuitBreakerPolicyFactoryGraphTests.cs` | `graphConfig` → policy: presets, overrides, fallback |
| `Orkeon.Scripting.Cli.Tests/Forge/ForgeStateMachineTests.cs` | The forge cycle's legal moves |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --filter "FullyQualifiedName~StateMachine"
```
