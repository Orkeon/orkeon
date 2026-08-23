> 🇫🇷 [Version française](../fr/guides/blueprint.md)

> **See also**: [ProcessTypes guide](../orchestration/process-types.md) · [Back to index](../INDEX.md)

# Blueprint — Adding an orchestration type to Orkeon

This document serves as a reproducible guide for implementing and documenting a new orchestration type. It builds on the FSM precedent (see [FSM Orchestration](../orchestration/fsm.md)) and covers the 8 required steps, from the Domain layer through to the final documentation.

---

## Prerequisites

Before starting, identify:

- **Type name**: e.g. `StateGraph`, `AgenticSwarm`, `PipelineDAG`
- **Doc number**: create a file in `docs/orchestration/` (e.g. `docs/orchestration/new-type.md`)
- **Example number**: next free index in `examples/` (e.g. `104`)
- **Initial scope**: where the new type applies first (Task, Crew, Flow)

---

## Step 1 — Domain: Generic framework

**Folder**: `src/core/Orkeon.Domain/Common/<NomType>/`

Create the following files (adapt the class names):

| File | Role | Reference model |
|---------|------|---------------------|
| `I<NomType>.cs` | Read-only + mutation interfaces in a single file | `IStateMachine.cs` (contains `IStateMachine` + `IMutableStateMachine`) |
| `<NomType>.cs` | Thread-safe main engine | `StateMachine.cs` |
| `<NomType>Builder.cs` | Fluent API to declare the graph/config | `StateMachineBuilder.cs` |
| `<NomType>Policy.cs` | Immutable configuration record with presets + `CircuitBreakerStatus` | `CircuitBreakerPolicy.cs` |
| `<NomType>Result.cs` | Immutable record of the operation result | `TransitionResult.cs` |
| `<NomType>Exceptions.cs` | Type-specific exceptions | `StateMachineExceptions.cs` |
| `TransitionDefinition.cs` | `internal sealed` class for a unit transition (from, trigger, to, guard, action) | `TransitionDefinition.cs` |

> **Note**: The two interfaces (read-only and mutation) live in a single file by convention.
> The `TransitionDefinition` class is `internal` and exposed to tests via `[assembly: InternalsVisibleTo]`.

### Technical checklist

- [ ] Thread-safety: `lock` or `SemaphoreSlim` depending on the async need
- [ ] Static presets: `Strict`, `Default`, `Permissive` (immutable)
- [ ] Events: at minimum `OnTransition` and `OnCircuitBroken` (or equivalent)
- [ ] `CircuitBreakerStatus` or equivalent with a full snapshot for observability
- [ ] No external dependency (pure Domain)
- [ ] `[assembly: InternalsVisibleTo("Orkeon.Domain.Tests")]` if there are internal classes

### Code pattern — Engine

```csharp
namespace Orkeon.Domain.Common.<NomType>;

public sealed class <NomType><TState, TEvent>
    : I<NomType><TState, TEvent>, IMutable<NomType><TState, TEvent>
    where TState : notnull
    where TEvent : notnull
{
    private readonly object _lock = new();
    // ... internal state, counters, histogram

    // Mandatory extension points:
    // 1. Check limits BEFORE each operation
    // 2. Action hook AFTER each successful operation
    // 3. Events for external observability
    // 4. Degraded mode (fallback state) when UseDegradedMode = true
}
```

### Code pattern — Fluent builder

```csharp
public sealed class <NomType>Builder<TState, TEvent>
    where TState : notnull
    where TEvent : notnull
{
    public <NomType>Builder<TState, TEvent> WithInitialState(TState state) { ... }
    public <NomType>Builder<TState, TEvent> WithTerminalStates(params TState[] states) { ... }
    public <NomType>Builder<TState, TEvent> WithDegradedState(TState state) { ... }
    public <NomType>Builder<TState, TEvent> WithCircuitBreaker(<NomType>Policy policy) { ... }

    // Key functions for internal dictionaries (useful when TState is not an enum):
    public <NomType>Builder<TState, TEvent> WithStateKey(Func<TState, string> keyFunc) { ... }
    public <NomType>Builder<TState, TEvent> WithEventKey(Func<TEvent, string> keyFunc) { ... }

    // Nested builder pattern for transitions/nodes:
    public TransitionBuilder When(TState from, TEvent trigger) { ... }

    // Shortcut for a simple transition without guard or action:
    public <NomType>Builder<TState, TEvent> AddTransition(TState from, TEvent trigger, TState to) { ... }

    public <NomType><TState, TEvent> Build() { ... }

    public sealed class TransitionBuilder
    {
        public TransitionBuilder TransitionTo(TState to) { ... }
        public TransitionBuilder WithGuard(Func<bool> guard, string? desc = null) { ... }
        public TransitionBuilder WithGuard<TContext>(Func<TContext, bool> guard, string? desc = null) { ... }
        public TransitionBuilder WithAction(Action action) { ... }
        public TransitionBuilder WithAction(Action<TState, TState> action) { ... }
        public <NomType>Builder<TState, TEvent> Done() { ... }
    }
}
```

---

## Step 2 — Domain: Specialization

**Folder**: `src/core/Orkeon.Domain/<Scope>/` (e.g. `Task/`, `Crew/`, `Flow/`)

| File | Role |
|---------|------|
| `<Scope><NomType>State.cs` | BOTH enums (states + events) in a single file | 
| `<Scope><NomType>Machine.cs` | Static factory `Create()` + `CreateBuilder()` + `GuardContext` record |

> **FSM convention**: In the existing implementation, `TaskExecutionState.cs` contains both
> the `TaskExecutionState` enum and the `TaskExecutionEvent` enum. Group both in a single
> file when they are always used together.

### Guard context type

```csharp
public sealed record <Scope>GuardContext
{
    // Loop counters (adapt to the orchestration type)
    public int RetryCount { get; init; }
    public int MaxRetries { get; init; } = 3;

    // Type-specific counters
    // FSM example: ToolCallCount, ValidationAttempts
    // LangGraph example: NodeVisitCount, EdgeTraversalCount
    // Agentic example: SubAgentSpawnCount, DelegationDepth

    // Derived predicates
    public bool CanRetry => RetryCount < MaxRetries;
    // ...
}
```

### Anti-hallucination guards (mandatory)

Every orchestration type MUST implement at minimum:

| Guard | Purpose | FSM example |
|-------|-----|-------------|
| Operation budget | Limit expensive calls | `ToolCallCount < MaxToolCallsPerRound` |
| Registered tool | Block hallucinated tools | `IsToolRegistered == true` |
| Retry limit | Avoid infinite loops | `RetryCount < MaxRetries` |

---

## Step 3 — Domain: Configuration DTO

**File**: `src/core/Orkeon.Domain/Configuration/<NomType>Config.cs`

```csharp
namespace Orkeon.Domain.Configuration;

/// <summary>
/// Immutable DTO for the <NomType> configuration coming from YAML.
/// All fields are nullable to allow crew → task inheritance.
/// </summary>
public sealed record <NomType>Config
{
    public string? Preset { get; init; }

    // Circuit breaker fields (common to all types)
    public int? MaxTransitions { get; init; }
    public int? StateTimeoutSeconds { get; init; }
    public int? MaxStateVisits { get; init; }
    public int? MaxTotalDurationSeconds { get; init; }
    public bool? UseDegradedMode { get; init; }

    // Type-specific fields (guards)
    public int? MaxRetries { get; init; }
    // ... adapt to the type
}
```

**Modify** `CrewConfiguration.cs` (which also contains `TaskConfiguration`):

```csharp
// In the CrewConfiguration record (same file):
public <NomType>Config? <NomType> { get; init; }

// In the TaskConfiguration record (same CrewConfiguration.cs file):
public <NomType>Config? <NomType> { get; init; }
```

> **Note**: `TaskConfiguration` is defined in the same file as `CrewConfiguration`
> (`src/core/Orkeon.Domain/Configuration/CrewConfiguration.cs`). There is no separate
> `TaskConfiguration.cs` file.

---

## Step 4 — Infrastructure: YAML → Domain bridge

**File**: `src/core/Orkeon.Infrastructure/Configuration/<NomType>PolicyFactory.cs`

```csharp
namespace Orkeon.Infrastructure.Configuration;

public static class <NomType>PolicyFactory
{
    /// <summary>
    /// Hierarchical resolution: task override → crew default → preset → Strict fallback.
    /// </summary>
    public static <NomType>Policy Resolve(
        <NomType>Config? crewDefault,
        <NomType>Config? taskOverride)
    {
        // 1. Determine the base preset
        // 2. Apply the crew-level overrides
        // 3. Apply the task-level overrides (they override crew)
        // 4. Convert int seconds → TimeSpan
    }

    /// <summary>
    /// Creates a specialized FSM (concrete return type, not generic).
    /// The method name must reflect the scope: CreateTaskFsm, CreateCrewFsm, etc.
    /// </summary>
    public static StateMachine<TaskExecutionState, TaskExecutionEvent> Create<Scope>Fsm(
        <NomType>Config? crewDefault,
        <NomType>Config? taskOverride)
    {
        var policy = Resolve(crewDefault, taskOverride);
        return <Scope><NomType>Machine.Create(policy);
    }

    /// <summary>
    /// Builds the guard context with hierarchical merging of the limits.
    /// The task override wins for non-null fields, then crew default, then the record's default value.
    /// </summary>
    public static <Scope>GuardContext CreateGuardContext(
        <NomType>Config? crewDefault,
        <NomType>Config? taskOverride)
    {
        var effective = taskOverride ?? crewDefault;
        return new <Scope>GuardContext
        {
            MaxRetries = effective?.MaxRetries ?? 3,
            // ... other fields with default fallback
            IsToolRegistered = true, // Runtime concern, not config
        };
    }
}

// --- Real example (CircuitBreakerPolicyFactory) ---
// The `CreateTaskFsm` method returns a concrete type, not a generic one.
// The `ApplyOverrides` method is private and handles the int seconds → TimeSpan conversion.
```

### Resolution hierarchy (invariant across all types)

```
1. Task-level config       (high priority — non-null fields override)
2. Crew-level config       (default — non-null fields override the preset)
3. Named preset            (if specified in Preset — base values)
4. <NomType>Policy.Strict  (fallback when nothing is configured)
```

---

## Step 5 — Infrastructure: YAML parsing

**Modified files**: `src/core/Orkeon.Infrastructure/Configuration/Yaml/YamlConfigModels.cs`
(the models) and `Configuration/Yaml/YamlCrewMapper.cs` (the mapping) — the loader
(`YamlCrewDefinitionLoader.cs`) only deserializes and delegates.

### 5.1 Add the YAML class

No `[YamlMember]` attributes — the existing models carry **none**: key resolution is
by convention (`CamelCaseNamingConvention` plus the `CamelOrSnakeCaseTypeInspector`
snake_case fallback in `YamlDotNetSerializer`). Follow the shape of the shipped
models — public, non-sealed, in the shared `YamlConfigModels.cs`:

```csharp
public class <NomType>YamlConfig
{
    public string? Preset { get; set; }         // reachable as preset:
    public int? MaxTransitions { get; set; }    // reachable as maxTransitions: or max_transitions:
    // ... plain properties, camelCase/snake_case both accepted by convention
}
```

### 5.2 Add the property on the THREE YAML models

The `YamlCrewDefinitionLoader` uses different models for single-file and multi-file loading:

```csharp
// In CrewYamlConfig (single-file config.yaml loading):
public <NomType>YamlConfig? <NomType> { get; set; }   // key: <nomType> by convention

// In CrewSettingsYamlConfig (multi-file loading: crew.yaml):
public <NomType>YamlConfig? <NomType> { get; set; }

// In TaskYamlConfig (used in both modes):
public <NomType>YamlConfig? <NomType> { get; set; }
```

> **Warning**: There are THREE YAML models to modify, not two. `CrewSettingsYamlConfig` is
> used only in multi-file mode (folder with `crew.yaml` + `agents.yaml` + `tasks.yaml`).

### 5.3 Add the mapping

```csharp
private static <NomType>Config? Map<NomType>(<NomType>YamlConfig? yaml)
{
    if (yaml is null) return null;
    return new <NomType>Config
    {
        Preset = yaml.Preset,
        MaxTransitions = yaml.MaxTransitions,
        // ...
    };
}
```

### 5.4 Wire into the loading methods

Add `<NomType> = Map<NomType>(...)`:

- in `YamlCrewMapper.MapTasks()` — for the task level;
- crew-level values flow through `CrewMappingSettings`, which the loader populates in
  `LoadFromStringAsync()` (single-file) and `LoadFromDirectoryCoreAsync()` (multi-file)
  before delegating to the mapper.

---

## Step 6 — Tests

**Folder**: `tests/core/Orkeon.Domain.Tests/Common/<NomType>/`

### 6.1 Generic framework tests

| File | Minimum tests |
|---------|--------------|
| `<NomType>Tests.cs` | Correct initial state, valid operation, full cycle, invalid operation throws, TryFire false, terminal blocked, CanFire, OnTransition event |
| `<NomType>GuardTests.cs` | Guard true allowed, guard false blocked, multiple guards priority, action executed |
| `<NomType>CircuitBreakerTests.cs` | Max transitions trip, cycle detection, degraded mode, OnCircuitBroken, reset, histogram |

### 6.2 Specialization tests

| File | Minimum tests |
|---------|--------------|
| `<Scope><NomType>MachineTests.cs` | Full happy path, each guard individually (budget, tool, retries, validation), cancel from any state, circuit breaker in a loop |

### 6.3 Infrastructure tests (optional but recommended)

| File | Minimum tests |
|---------|--------------|
| `<NomType>PolicyFactoryTests.cs` | Preset-only resolution, crew override, task override overrides crew, Strict fallback |

### Test command

```bash
dotnet test tests/core/Orkeon.Domain.Tests/ --filter "FullyQualifiedName~<NomType>"
```

---

## Step 7 — Example

**Folder**: `examples/06-engineering-devops/<NumExemple>-<nom-court>/`

### 7.1 Copy an existing example

Start from example 102 or 103 as the base, then:

1. Duplicate the `config.yaml`
2. Add/replace the configuration block for the new type
3. Adapt tasks and agents if necessary

### 7.2 config.yaml structure

```yaml
# === Configuration block of the new type at crew level ===
<nomType>:                          # e.g. stateGraph, agenticSwarm
  preset: "strict"
  useDegradedMode: true
  maxRetries: 3
  # ... type-specific fields

agents:
  my_agent:               # mapping keyed by agent id — never a sequence
    role: "..."
    # ...

tasks:
  task_1:                 # the id IS the key (TaskYamlConfig has no id: field)
    description: "..."
    # === Task-level override ===
    <nomType>:
      maxTransitions: 200
      stateTimeoutSeconds: 600
      # ...
```

### 7.3 Example README

README structure:

```markdown
# Example <Num> — <Title>

## Goal
What and why.

## Diagram
ASCII or Mermaid of the graph/workflow.

## YAML configuration
Annotated block with the key values.

## Diff with example <Num-1>
What changes compared to the previous example.

## Run
Command to run the example.
```

---

## Step 8 — Documentation

**File**: `docs/orchestration/<nom-type>.md`

### 8.1 Mandatory document structure

```markdown
# <NomType> orchestration

## Overview
Introductory paragraph: what, why, where in the architecture.

## Architecture

### Domain layer — Generic framework
Table: Class | Role (point to the files)

### Domain layer — <Scope> specialization
Table: Class | Role

### Infrastructure layer — YAML integration
Table: Class | Role

### Domain layer — Configuration
Table: Class | Role

## Graph / Diagram
ASCII art of the state graph or workflow.
List the terminal states.

## Circuit breaker / Protection mechanisms
Table: Mechanism | Parameter | Description
Explain the two modes (exception vs degraded).

## Presets
Table: Preset | Values (reuse Strict, Default, Permissive)

## Observability
Exposed events, status/snapshot contents.

## Typical guards
Table: Guard | Protected transition/node | Condition
Explain the anti-hallucination guard.

## Configuration YAML

### Schema
Annotated YAML block with types and descriptions.

### Resolution hierarchy
ASCII diagram: task → crew → preset → fallback.

### YAML models
Table: C# model | YAML class | File

## Usage in C# code

### Manual creation (Fluent Builder)
Complete example with observability and guard context.

### Creation from the YAML configuration
Example with the PolicyFactory.

### Custom construction
Example with the generic builder.

## Example <Num>
Reference to the example, summary of the additions.

## Relation to the existing code

### StateTransitionManager
Explanation of the complementarity (lifecycle vs runtime).

### SequentialCrewOrchestrator
Explanation of the integration point (IProcessStrategy).

### <Other existing orchestration types>
How this type coexists with the previous ones.

## Tests
Table: File | Coverage
Test command.
```

### 8.2 Update the existing files

1. **`docs/INDEX.md`**:
   - Add a line in the Orchestration section
   - Add the file to the "I want to understand the framework" reading path

2. **`docs/architecture/yaml-schema.md`**:
   - Add the YAML schema of the new configuration block
   - Document the presets and default values

3. **`docs/orchestration/process-types.md`**:
   - Add the new type to the comparison matrix
   - Add a dedicated section with pros/cons/use cases
   - Update the decision tree

---

## Stability criteria (final checklist)

Before considering the implementation complete, verify:

| Criterion | Expected mechanism | Verified |
|---------|-------------------|---------|
| Recursive loops | Circuit breaker (4 mechanisms minimum) + typed guards | [ ] |
| Tool hallucinations | `IsToolRegistered` guard or equivalent | [ ] |
| Observability | Events + snapshot/status with histogram | [ ] |
| Interruptibility | Cancel from any non-terminal state | [ ] |
| Multi-model determinism | Per-task configurable limits (Flash ≠ Opus) | [ ] |
| 2-level YAML config | Crew default + task override | [ ] |
| Degraded mode | Auto transition to a safe state instead of an exception | [ ] |
| Tests | 30+ tests covering framework + specialization | [ ] |
| Documentation | Complete doc + index + features updated | [ ] |
| Example | Working config.yaml with README | [ ] |
