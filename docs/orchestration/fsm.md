> 🇫🇷 [Version française](../fr/orchestration/fsm.md)

> **See also**: [ProcessTypes comparison guide](./process-types.md) · [YAML schema](../architecture/yaml-schema.md) · [Back to index](../INDEX.md)

# Finite state machine (FSM) orchestration

## Overview

Orkeon provides a generic finite state machine framework (`StateMachine<TState, TEvent>`) in the Domain layer, with a built-in circuit breaker to prevent runaway recursive loops during multi-agent orchestration.

The FSM is configurable via YAML (`circuitBreaker` field) and is specialized first and foremost for the task execution lifecycle (`TaskExecutionStateMachine`). The framework is generic and extensible to other domains (Crew, Agent, Flow).

## Architecture

### Domain layer — Generic framework

The framework components live in `Orkeon.Domain.Common.StateMachine`:

| Class | Role |
|--------|------|
| `StateMachine<TState, TEvent>` | Thread-safe FSM engine with built-in circuit breaker |
| `StateMachineBuilder<TState, TEvent>` | Fluent API to declare the state graph |
| `CircuitBreakerPolicy` | Protection threshold configuration |
| `TransitionResult<TState, TEvent>` | Result of a transition (states, ordinal, timestamp) |
| `IStateMachine<TState, TEvent>` | Read-only interface for observation |
| `IMutableStateMachine<TState, TEvent>` | Interface with `Fire()`, `TryFire()`, events |

### Domain layer — Task specialization

The components specific to the task lifecycle live in `Orkeon.Domain.Task`:

| Class | Role |
|--------|------|
| `TaskExecutionState` | Enum of execution states (Assigned, Planning, Executing, etc.) |
| `TaskExecutionEvent` | Enum of events (StartPlanning, RequestToolCall, etc.) |
| `TaskExecutionStateMachine` | Pre-configured factory with guards and the complete graph |
| `TaskExecutionGuardContext` | Typed context for the guards (retries, tool calls, validation) |

### Infrastructure layer — YAML integration

| Class | Role |
|--------|------|
| `CircuitBreakerYamlConfig` | YAML model for the `circuitBreaker` section |
| `CircuitBreakerPolicyFactory` | Converts the YAML config into a `CircuitBreakerPolicy` + FSM |

### Domain layer — Configuration

| Class | Role |
|--------|------|
| `CircuitBreakerConfig` | Immutable DTO for the circuit breaker configuration |

## TaskExecutionStateMachine state graph

```
                     StartPlanning         BeginExecution
   [Assigned] ──────────────────► [Planning] ─────────────► [Executing]
        │                                                      │    ▲
        │ BeginExecution                       ToolCallCompleted│    │ RequestToolCall
        └──────────────────────────────► [Executing] ◄─────────┘    │ (guard: budget)
                                            │    │                  ▼
                              SubmitFor     │    │            [ToolCalling]
                              Validation    │    │ Fail            │
                                            ▼    ▼                 │ ToolCallFailed
                                      [Validating]  [Failed] ◄────┘
                                         │    │       │
                           ValidationPassed│    │       │ Retry (guard: maxRetries)
                                         ▼    │       └──────────► [Executing]
                                   [Completed] │ ValidationFailed
                                               │ (guard: maxValidationRetries)
                                               └──────────► [Executing]

   RequestHumanInput                    HumanInputReceived
   [Executing] ──────────► [WaitingForHumanInput] ──────────► [Executing]

   Cancel (from any non-terminal state) ──────────────────► [Cancelled]
   Circuit breaker trip ──────────────────────────────────► [Degraded]
```

Terminal states: `Completed`, `Cancelled`, `Degraded`.

## Circuit breaker

The circuit breaker is built directly into `StateMachine<TState, TEvent>` and checks four conditions before each transition:

### Protection mechanisms

| Mechanism | Parameter | Description |
|-----------|-----------|-------------|
| Max transitions | `MaxTransitions` | Total number of allowed transitions |
| Per-state timeout | `StateTimeout` | Maximum time spent in a single state |
| Cycle detection | `MaxStateVisits` | Maximum number of visits to the same state |
| Total duration | `MaxTotalDuration` | Maximum lifetime of the machine |

When a condition is violated, two behaviors are possible depending on `UseDegradedMode`:

- `false` (default): a `CircuitBrokenException` is thrown with a detailed `CircuitBreakerStatus`
- `true`: the machine automatically transitions to the configured degraded state (e.g. `TaskExecutionState.Degraded`)

### Presets

Three presets are provided via `CircuitBreakerPolicy`:

| Preset | MaxTransitions | StateTimeout | MaxStateVisits | MaxTotalDuration | DegradedMode |
|--------|---------------|-------------|----------------|------------------|--------------|
| `Strict` | 50 | 2 min | 5 | 10 min | true |
| `Default` | 100 | 5 min | 10 | 30 min | false |
| `Permissive` | 1000 | 30 min | 50 | 2 h | false |

The `Strict` preset is the default for production LLM workloads.

### Observability

The machine exposes two events:

- `OnTransition`: raised after each successful transition, with a `TransitionResult` (from, to, trigger, ordinal, timestamp)
- `OnCircuitBroken`: raised when the circuit trips, with a `CircuitBreakerStatus` including a per-state visit histogram

`CircuitBreakerStatus` provides a complete snapshot: `IsBroken`, `BrokenReason`, `TotalTransitions`, `TimeInCurrentState`, `CurrentStateVisitCount`, `TotalElapsed`, `StateVisitHistogram`.

## Typed guards

The `TaskExecutionStateMachine` uses three typed guards via `TaskExecutionGuardContext` to prevent dangerous loops:

| Guard | Protected transition | Condition |
|-------|---------------------|-----------|
| Tool call budget | `Executing → ToolCalling` | `ToolCallCount < MaxToolCallsPerRound && IsToolRegistered` |
| Retry limit | `Failed → Executing` | `RetryCount < MaxRetries` |
| Validation limit | `Validating → Executing` | `ValidationAttempts < MaxValidationRetries` |

The `IsToolRegistered` guard blocks calls to tools not registered on the agent, which prevents tool hallucinations by the LLM.

## YAML configuration

### `circuitBreaker` schema

The `circuitBreaker` block can be used at two levels in the `config.yaml`:

```yaml
# Crew level — defaults for all tasks
circuitBreaker:
  preset: string              # "strict" | "permissive" | "default"
  maxTransitions: int         # Overrides the preset
  stateTimeoutSeconds: int    # Per-state timeout in seconds
  maxStateVisits: int         # Cycle detection
  maxTotalDurationSeconds: int # Total duration in seconds
  useDegradedMode: bool       # true = Degraded, false = exception
  maxRetries: int             # Retries after failure (guard)
  maxToolCallsPerRound: int   # Max tool calls per round (guard)
  maxValidationRetries: int   # Max validation loops (guard)

tasks:
  <task_id>:
    description: string
    # Task level — override for this specific task
    circuitBreaker:
      maxTransitions: int     # Overrides the crew default
      stateTimeoutSeconds: int
      # ... same fields as above
```

### Resolution hierarchy

```
1. Task-level circuitBreaker     (highest priority)
2. Crew-level circuitBreaker     (default)
3. Named preset                  (if specified)
4. CircuitBreakerPolicy.Strict   (fallback when nothing is configured)
```

Each individual field overrides the preset value. For example, a "strict" preset with `maxTransitions: 200` keeps all the preset values except the transitions.

### YAML models

| C# model | YAML class | File |
|-----------|-------------|---------|
| `CircuitBreakerConfig` | `CircuitBreakerYamlConfig` | `Configuration/Yaml/YamlConfigModels.cs` |

The mapping is performed by `YamlCrewMapper.MapCircuitBreaker()` (private, `Configuration/Yaml/`). The conversion into an executable `CircuitBreakerPolicy` is performed by `CircuitBreakerPolicyFactory.Resolve()`.

## Usage in C# code

### Manual creation (Fluent Builder)

```csharp
using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Task;

// Create an FSM with the Strict preset
var fsm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Strict);

// Observe the transitions
fsm.OnTransition += (_, result) =>
    Console.WriteLine($"{result.FromState} -> {result.ToState} via {result.Trigger}");

fsm.OnCircuitBroken += (_, status) =>
    Console.WriteLine($"CIRCUIT BROKEN: {status.BrokenReason}");

// Guard context
var ctx = new TaskExecutionGuardContext
{
    RetryCount = 0,
    MaxRetries = 3,
    ToolCallCount = 0,
    MaxToolCallsPerRound = 10,
    IsToolRegistered = true,
};

// Run the workflow
fsm.Fire(TaskExecutionEvent.BeginExecution);
fsm.Fire(TaskExecutionEvent.RequestToolCall, ctx);
fsm.Fire(TaskExecutionEvent.ToolCallCompleted);
fsm.Fire(TaskExecutionEvent.SubmitForValidation);
fsm.Fire(TaskExecutionEvent.ValidationPassed);

Console.WriteLine(fsm.CurrentState); // Completed
Console.WriteLine(fsm.IsTerminal);   // true
```

### Creation from the YAML configuration

```csharp
using Orkeon.Infrastructure.Configuration;

// The CircuitBreakerPolicyFactory resolves the crew + task hierarchy
var fsm = CircuitBreakerPolicyFactory.CreateTaskFsm(
    crewDefault: crewConfig.CircuitBreaker,
    taskOverride: taskConfig.CircuitBreaker
);

var guardCtx = CircuitBreakerPolicyFactory.CreateGuardContext(
    crewDefault: crewConfig.CircuitBreaker,
    taskOverride: taskConfig.CircuitBreaker
);
```

### Building a custom FSM

The generic framework lets you define any state graph:

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

## Example 103

The `examples/06-engineering-devops/103-ts-codebase-with-fsm/` example demonstrates the full integration. It reuses the scenario of example 102 (TypeScript codebase analysis) and adds:

- `circuitBreaker` at the crew level with the "strict" preset and degraded mode
- `circuitBreaker` at the task level for the supervisor (raised limits because of the long orchestration)
- Anti-tool-hallucination guards (`maxToolCallsPerRound: 30`)
- Reduced retry limit for the supervisor (`maxRetries: 2`)

See the example's `config.yaml` file for the complete syntax.

## Relationship with the existing code

### StateTransitionManager

The existing `StateTransitionManager` (`Orkeon.Application.Services.StateManagement`) stays in place. It validates persisted state transitions (AgentStatus, CrewStatus, TaskStatus). The `TaskExecutionStateMachine` FSM operates at a different level of granularity: it manages the runtime execution cycle (Assigned → Executing → ToolCalling → Validating → Completed) while the StateTransitionManager manages the lifecycle states (Pending → InProgress → Completed).

The two are complementary: the StateTransitionManager governs the persisted status, the FSM governs the in-flight execution.

### SequentialCrewOrchestrator

The existing orchestrator is not replaced. The FSM plugs in inside the `IProcessStrategy` implementations (Sequential, Hierarchical, Parallel) to drive the execution of each individual task, where LLM loops are the most dangerous.

## Tests

32+ unit tests cover the framework and the specialization:

| Test file | Coverage |
|-----------------|-----------|
| `StateMachineTests.cs` | Valid/invalid transitions, terminal states, complete cycle |
| `StateMachineGuardTests.cs` | Typed guards, guard priority, on-transition actions |
| `CircuitBreakerTests.cs` | Max transitions, cycle detection, degraded mode, reset, histogram |
| `TaskExecutionStateMachineTests.cs` | Happy path, tool calls, retries, validation, cancel, circuit breaker |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/ --filter "FullyQualifiedName~StateMachine"
```
