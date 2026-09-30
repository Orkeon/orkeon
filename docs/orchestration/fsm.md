> 🇫🇷 [Version française](../fr/orchestration/fsm.md)

> **See also**: [ProcessTypes comparison guide](./process-types.md) · [YAML schema](../architecture/yaml-schema.md) · [Back to index](../INDEX.md)

# Finite state machine (FSM) orchestration

## Overview

Orkeon provides a generic finite state machine framework (`StateMachine<TState, TEvent>`) in the Domain layer, with a built-in circuit breaker to prevent runaway recursive loops during multi-agent orchestration. It comes with a ready-made specialization for the task execution lifecycle (`TaskExecutionStateMachine`) and is extensible to other domains.

> **What runs today.** The FSM is a Domain building block: **no process strategy drives its tasks through `TaskExecutionStateMachine` yet.** The YAML `circuitBreaker` block is parsed at both levels, but at execution only the **crew-level** block is read, by the [Graph mode](./graph.md) (when no `graphConfig` is declared); the task-level block and the three guard limits are resolved by `CircuitBreakerPolicyFactory` but no execution path calls that resolution for tasks (see [YAML schema](../architecture/yaml-schema.md#circuit-breaker-configuration)). What bounds a task at runtime is the agent loop: `maxIter` iterations, and a stop after 3 consecutive identical tool errors.

## Architecture

### Domain layer — Generic framework

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

### Domain layer — Task specialization

The components specific to the task lifecycle live in `Orkeon.Domain.Task`:

| Class | Role |
|--------|------|
| `TaskExecutionState` | Enum of execution states (Assigned, Planning, Executing, etc.) — `TaskExecutionState.cs` |
| `TaskExecutionEvent` | Enum of events (StartPlanning, RequestToolCall, etc.) — same file |
| `TaskExecutionStateMachine` | Static factory (`Create`, `CreateBuilder`) with guards and the complete graph |
| `TaskExecutionGuardContext` | Typed context for the guards (retries, tool calls, validation) — same file as the factory |

### Infrastructure layer — YAML integration

| Class | Role |
|--------|------|
| `CircuitBreakerYamlConfig` | YAML model for the `circuitBreaker` section |
| `CircuitBreakerPolicyFactory` | Converts the config into a `CircuitBreakerPolicy`, a task FSM or a guard context; `ResolveGraph` serves the Graph mode |

### Domain layer — Configuration

| Class | Role |
|--------|------|
| `CircuitBreakerConfig` | Immutable DTO for the circuit breaker configuration (`CrewConfiguration.CircuitBreaker`, `TaskConfiguration.CircuitBreaker`, `Crew.CircuitBreaker`) |

## TaskExecutionStateMachine state graph

```
[Assigned] ─► [Planning] ─► [Executing] ⇄ [ToolCalling]
                                │  ▲
                                ▼  │ ValidationFailed / Retry / HumanInputReceived
                          [Validating] ─► [Completed]
                          [Failed] · [WaitingForHumanInput]

Cancel (any non-terminal state) ─► [Cancelled]      breaker trip (degraded mode) ─► [Degraded]
```

| From | Event | To | Guard |
|------|-------|----|-------|
| `Assigned` | `StartPlanning` | `Planning` | — |
| `Assigned`, `Planning` | `BeginExecution` | `Executing` | — |
| `Executing` | `RequestToolCall` | `ToolCalling` | `CanCallTool` |
| `ToolCalling` | `ToolCallCompleted`, `ToolCallFailed` | `Executing` | — |
| `Executing` | `SubmitForValidation` | `Validating` | — |
| `Validating` | `ValidationPassed` | `Completed` | — |
| `Validating` | `ValidationFailed` | `Executing` | `CanRetryValidation` |
| `Executing` | `RequestHumanInput` | `WaitingForHumanInput` | — |
| `WaitingForHumanInput` | `HumanInputReceived` | `Executing` | — |
| `Executing`, `Validating` | `Fail` | `Failed` | — |
| `Failed` | `Retry` | `Executing` | `CanRetry` |
| any non-terminal state | `Cancel` | `Cancelled` | — |

`ToolCallFailed` returns to `Executing` (the agent decides what to do with the error). Terminal states: `Completed`, `Cancelled`, `Degraded`.

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
- `true`: the machine transitions to the degraded state declared with `WithDegradedState(...)` (e.g. `TaskExecutionState.Degraded`); without a declared degraded state it throws as above

`TryFire()` returns `false` instead of throwing, for a tripped breaker as for a missing transition. A guard that rejects every candidate transition surfaces as an `InvalidTransitionException`. `ResetCircuitBreaker()` clears the trip and the counters.

### Presets

Three presets are provided via `CircuitBreakerPolicy`:

| Preset | MaxTransitions | StateTimeout | MaxStateVisits | MaxTotalDuration | DegradedMode |
|--------|---------------|-------------|----------------|------------------|--------------|
| `Strict` | 50 | 2 min | 5 | 10 min | true |
| `Default` | 100 | 5 min | 10 | 30 min | false |
| `Permissive` | 1000 | 30 min | 50 | 2 h | false |

`new CircuitBreakerPolicy()` equals `Default`. `Strict` is the fallback of `TaskExecutionStateMachine.Create()` and of `CircuitBreakerPolicyFactory` when nothing (or an unknown preset) is configured.

### Observability

The machine exposes two events:

- `OnTransition`: raised after each successful transition, with a `TransitionResult` (`FromState`, `ToState`, `Trigger`, `TransitionOrdinal`, `Timestamp`, `StateChanged`)
- `OnCircuitBroken`: raised when the circuit trips, with a `CircuitBreakerStatus` including a per-state visit histogram

`CircuitBreakerStatus` provides a complete snapshot: `IsBroken`, `BrokenReason`, `TotalTransitions`, `TimeInCurrentState`, `CurrentStateVisitCount`, `TotalElapsed`, `StateVisitHistogram`. The read-only interface also offers `CurrentState`, `IsTerminal`, `TransitionCount`, `GetPermittedEvents()` and `CanFire(event)`.

## Typed guards

The `TaskExecutionStateMachine` uses three typed guards via `TaskExecutionGuardContext` to prevent dangerous loops:

| Guard | Protected transition | Condition |
|-------|---------------------|-----------|
| Tool call budget | `Executing → ToolCalling` | `ToolCallCount < MaxToolCallsPerRound && IsToolRegistered` (`CanCallTool`) |
| Retry limit | `Failed → Executing` | `RetryCount < MaxRetries` (`CanRetry`) |
| Validation limit | `Validating → Executing` | `ValidationAttempts < MaxValidationRetries` (`CanRetryValidation`) |

Defaults: `MaxRetries = 3`, `MaxToolCallsPerRound = 10`, `MaxValidationRetries = 3`, `IsToolRegistered = true`. The `IsToolRegistered` guard blocks calls to tools not registered on the agent, which prevents tool hallucinations by the LLM.

## YAML configuration

### `circuitBreaker` schema

The `circuitBreaker` block can be declared at two levels of a crew file (see the runtime status in the [Overview](#overview)):

```yaml
# Crew level — defaults for all tasks (read by the Graph mode)
circuitBreaker:
  preset: string              # "strict" (also the fallback) | "permissive" | "default"
  maxTransitions: int         # Overrides the preset
  stateTimeoutSeconds: int    # Per-state timeout in seconds
  maxStateVisits: int         # Cycle detection
  maxTotalDurationSeconds: int # Total duration in seconds
  useDegradedMode: bool       # true = Degraded, false = exception
  maxRetries: int             # Retries after failure (guard, default 3)
  maxToolCallsPerRound: int   # Max tool calls per round (guard, default 10)
  maxValidationRetries: int   # Max validation loops (guard, default 3)

tasks:
  <task_id>:
    description: string
    # Task level — override for this specific task (parsed, not applied yet)
    circuitBreaker:
      maxTransitions: int     # Overrides the crew default
      stateTimeoutSeconds: int
      # ... same fields as above
```

Keys are camelCase or snake_case (`max_transitions`), like the rest of the schema.

### Resolution hierarchy (`CircuitBreakerPolicyFactory.Resolve`)

```
Policy limits (field by field):
1. Task-level circuitBreaker     (highest priority)
2. Crew-level circuitBreaker
3. Named preset                  (the task's preset, else the crew's)
4. CircuitBreakerPolicy.Strict   (no preset, unknown preset, or nothing configured)

Guard limits (CreateGuardContext — whole block):
   the task block if present, else the crew block, else 3 / 10 / 3
```

Each individual policy field overrides the preset value: a "strict" preset with `maxTransitions: 200` keeps all the preset values except the transitions. The guard limits do not merge field by field — a task block without `maxRetries` falls back to 3, not to the crew's value.

### YAML models

| C# model | YAML class | File |
|-----------|-------------|---------|
| `CircuitBreakerConfig` | `CircuitBreakerYamlConfig` (on `CrewYamlConfig`, `CrewSettingsYamlConfig` and `TaskYamlConfig`) | `Configuration/Yaml/YamlConfigModels.cs` |

The mapping is performed by `YamlCrewMapper.MapCircuitBreaker()` (private, `Configuration/Yaml/`); `CrewFactory` carries the crew-level block onto the `Crew` aggregate (`Crew.CircuitBreaker`, also settable with `CrewBuilder.WithCircuitBreaker(...)`). The conversion into an executable `CircuitBreakerPolicy` is performed by `CircuitBreakerPolicyFactory.Resolve()`.

## Usage in C# code

### Manual creation

```csharp
using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Task;

// Create an FSM with the Strict preset (also the default of Create())
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

No execution path makes this call: to run a task through an FSM configured by the YAML blocks, your own code builds it with `CircuitBreakerPolicyFactory` and fires its events.

```csharp
using Orkeon.Infrastructure.Configuration;

// crewConfig / taskConfig: the CrewConfiguration / TaskConfiguration the loader produced
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

`AddTransition(from, trigger, to)` is the shortcut for a transition without guard or action; `WithStateKey` / `WithEventKey` supply the key functions when `TState` / `TEvent` are not enums.

> The scripting DSL has its own `stateMachine()` literal (`fsm.d.ts`), a separate JavaScript implementation without the circuit breaker — see [Scripting](../architecture/scripting.md).

## Example 103

The [103-ts-codebase-with-fsm](https://github.com/orkeon/orkeon/blob/main/examples/06-engineering-devops/103-ts-codebase-with-fsm/) example shows the full `circuitBreaker` syntax on the scenario of example 102 (TypeScript codebase analysis):

- `circuitBreaker` at the crew level with the "strict" preset and degraded mode
- `circuitBreaker` at the task level for the supervisor (raised limits because of the long orchestration)
- Anti-tool-hallucination guards (`maxToolCallsPerRound: 30`)
- Reduced retry limit for the supervisor (`maxRetries: 2`)

The crew is `process: sequential`, so today these blocks are parsed and validated but not enforced (see the [Overview](#overview)).

## Relationship with the existing code

### StateTransitionManager

The `StateTransitionManager` (`Orkeon.Application.Services.StateManagement`) validates persisted state transitions (AgentStatus, CrewStatus, TaskStatus). The `TaskExecutionStateMachine` models a different level of granularity: the runtime execution cycle (Assigned → Executing → ToolCalling → Validating → Completed), where the StateTransitionManager governs the lifecycle states (Pending → InProgress → Completed).

### Process strategies

The `IProcessStrategy` implementations execute a task through `IAgentExecutionService` and its agent loop; none of them instantiates the task FSM. The only runtime consumer of the circuit breaker is the [Graph mode](./graph.md), which applies `CircuitBreakerPolicy` to its `StateGraph` runner.

## Tests

32 test methods cover the framework and the specialization:

| Test file | Coverage |
|-----------------|-----------|
| `Common/StateMachine/StateMachineTests.cs` | Valid/invalid transitions, terminal states, complete cycle |
| `Common/StateMachine/StateMachineGuardTests.cs` | Typed guards, guard priority, on-transition actions |
| `Common/StateMachine/CircuitBreakerTests.cs` | Max transitions, cycle detection, degraded mode, reset, histogram |
| `Task/TaskExecutionStateMachineTests.cs` | Happy path, tool calls, retries, validation, cancel, circuit breaker |

`CircuitBreakerPolicyFactory` is covered in `Orkeon.Infrastructure.Tests` (`Configuration/CovSecurity_CircuitBreakerPolicyFactoryTests.cs`, `Configuration/CircuitBreakerPolicyFactoryGraphTests.cs`).

```bash
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --filter "FullyQualifiedName~StateMachine"
```
