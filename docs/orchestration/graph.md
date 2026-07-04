> 🇫🇷 [Version française](../fr/orchestration/graph.md)

> **See also**: [ProcessTypes comparison guide](./process-types.md) · [FSM orchestration](./fsm.md) · [YAML schema](../architecture/yaml-schema.md) · [Back to index](../INDEX.md)

# Typed state graph orchestration (StateGraph)

## Overview

Orkeon provides a typed state graph engine (`StateGraph<TState>`) in the Domain layer, inspired by LangGraph. Unlike the generic FSM (see [FSM orchestration](./fsm.md)) which drives transitions within a task, the StateGraph orchestrates the flow between tasks at the Crew level, with conditional edges and controlled cycles.

The StateGraph is configurable via YAML (`graphConfig` field) and integrates as an alternative `IProcessStrategy` (`GraphProcessStrategy`) selectable via `process: "graph"` in the config.yaml.

### Positioning relative to the other strategies

| Strategy | Granularity | Cycles | Conditional routing | Circuit breaker |
|-----------|-------------|--------|----------------------|-----------------|
| Sequential | Crew | No | No | No (via per-task FSM) |
| Hierarchical | Crew | No | Manager LLM | No (via per-task FSM) |
| Parallel | Crew | No | No | No (via per-task FSM) |
| **Graph** | **Crew** | **Yes (controlled)** | **Yes (conditional edges)** | **Yes (built-in)** |
| FSM | Task | Yes (guards) | Yes (events/guards) | Yes (built-in) |

The Graph and the FSM are complementary: the Graph orchestrates the task sequence, the FSM orchestrates the internal execution of each task.

## Architecture

### Domain layer — Generic engine

The engine components live in `Orkeon.Domain.Graph`:

| Class | Role |
|--------|------|
| `StateGraph<TState>` | Graph definition: nodes, fixed and conditional edges, compilation |
| `GraphNode<TState>` | Processing node: `Func<TState, CancellationToken, Task<TState>>` |
| `IGraphEdge<TState>` | Interface for routing (fixed or conditional) |
| `FixedEdge<TState>` | Unconditional edge to a target node |
| `ConditionalEdge<TState>` | Edge with a routing function `Func<TState, string>` |
| `GraphRunner<TState>` | Execution engine with circuit breaker and observability |
| `GraphExecutionResult<TState>` | Result: final state, trace, transitions, duration |
| `GraphCircuitBrokenException` | Exception thrown when the circuit breaker trips |

### Domain layer — Configuration

| Class | Role |
|--------|------|
| `GraphConfig` | Immutable DTO for the graph configuration from YAML |

The `GraphConfig?` field is added to `CrewConfiguration` (`Orkeon.Domain.Configuration`).

### Infrastructure layer — Integration

| Class | Role |
|--------|------|
| `GraphProcessStrategy` | Implements `IProcessStrategy`, builds and runs the StateGraph |
| `CrewGraphState` | Typed state traversing the graph (tasks, agents, results, retries) |
| `GraphYamlConfig` | YAML model for the `graphConfig` section |

### Infrastructure layer — Registration

| Class | Change |
|--------|-------------|
| `ProcessStrategyFactory` | Added the `"Graph"` case → `GraphProcessStrategy` |
| `YamlCrewDefinitionLoader` | Added `GraphYamlConfig`, `MapGraphConfig()`, `"graph"` in `ParseProcessType` |
| `ProcessType` | Added `ProcessType.Graph` |

## Graph topology

```
START ──► [execute_task] ──► [route] ──┬──► [execute_task]   (remaining tasks or retry)
                                       │
                                       └──► END              (everything is done)
```

The `execute_task` node dequeues a task, has an agent execute it (round-robin or explicit assignment), and accumulates the result into the typed state `CrewGraphState`.

The `route` node inspects the state:

- If tasks remain in the queue → loops back to `execute_task`
- If some tasks failed and the retry counter is not exhausted → re-enqueues the failures and loops back
- Otherwise → routes to END

The circuit breaker trips automatically if the graph exceeds the configured thresholds.

## Circuit breaker

The circuit breaker is built into `GraphRunner<TState>` and checks three conditions before each node:

### Protection mechanisms

| Mechanism | Parameter | Description |
|-----------|-----------|-------------|
| Max transitions | `MaxTransitions` | Total number of allowed node executions |
| Cycle detection | `MaxStateVisits` | Maximum number of visits to the same node |
| Total duration | `MaxTotalDuration` | Maximum lifetime of the graph |

The StateGraph reuses `CircuitBreakerPolicy` from `Orkeon.Domain.Common.StateMachine` (the same record as the FSM).

When a condition is violated, a `GraphCircuitBrokenException` is thrown with the complete graph trace. The `GraphProcessStrategy` catches this exception and returns a `CrewOutput.CreateFailure()` instead of propagating it.

### Presets

The presets are the same as for the FSM:

| Preset | MaxTransitions | StateTimeout | MaxStateVisits | MaxTotalDuration |
|--------|---------------|-------------|----------------|------------------|
| `Strict` | 50 | 2 min | 5 | 10 min |
| `Default` | 100 | 5 min | 10 | 30 min |
| `Permissive` | 1000 | 30 min | 50 | 2 h |

### Observability

The `GraphRunner<TState>` exposes two events:

- `OnNodeCompleted`: raised after each node, with `NodeCompletedEventArgs` (node name, current state, ordinal, trace snapshot)
- `OnCircuitBroken`: raised when the circuit trips, with `GraphCircuitBrokenEventArgs` (reason, node, counter, trace)

The `GraphProcessStrategy` subscribes to these events for structured logging via `ILogger` (source-generated `LoggerMessage`).

## Controlled retry

The `GraphProcessStrategy` adds a retry mechanism on top of the circuit breaker:

| Parameter | Field | Description |
|-----------|-------|-------------|
| Retry cycles | `MaxRetryCycles` | Number of retry cycles for failed tasks |

How it works:

1. When a task fails, it is added to `FailedTaskIds` with a counter
2. When `PendingTaskIds` is empty, the `route` node promotes the eligible failed tasks
3. Tasks that exceeded `MaxRetryCycles` are abandoned
4. The circuit breaker trips if the total number of transitions exceeds the thresholds

## YAML configuration

### `graphConfig` schema

The `graphConfig` block can be used at the root level of the YAML:

```yaml
name: "my-crew"
process: "graph"

graphConfig:
  maxRetryCycles: int           # default: 2 — cycles de retry pour tâches échouées
  circuitBreakerPreset: string  # "strict" | "permissive" | "default"
  maxTransitions: int           # Surcharge le preset
  maxStateVisits: int           # Détection de cycles (surcharge le preset)
  maxTotalDurationSeconds: int  # Durée totale en secondes (surcharge le preset)

agents:
  <agent_id>:
    # ... même schéma que sequential
tasks:
  <task_id>:
    # ... même schéma que sequential
```

### Resolution hierarchy

```
1. graphConfig explicit fields       (highest priority)
2. graphConfig.circuitBreakerPreset  (base values)
3. CircuitBreakerPolicy.Strict       (fallback when nothing is configured)
```

### YAML models

| C# model | YAML class | File |
|-----------|-------------|---------|
| `GraphConfig` | `GraphYamlConfig` | `YamlCrewDefinitionLoader.cs` |

The mapping is performed by `YamlCrewDefinitionLoader.MapGraphConfig()`.

## Usage in C# code

### Direct StateGraph usage (generic framework)

```csharp
using Orkeon.Domain.Graph;
using Orkeon.Domain.Common.StateMachine;

// Définir un état type
class PipelineState
{
    public Queue<string> Pending { get; set; } = new();
    public List<string> Results { get; set; } = [];
    public int RetryCount { get; set; }
}

// Construire le graphe
var graph = new StateGraph<PipelineState>(CircuitBreakerPolicy.Strict)
    .AddNode("process", async (state, ct) =>
    {
        var item = state.Pending.Dequeue();
        var result = await ProcessItemAsync(item, ct);
        state.Results.Add(result);
        return state;
    })
    .AddNode("decide", (state, _) => Task.FromResult(state))
    .AddEdge(StateGraph<PipelineState>.StartNode, "process")
    .AddEdge("process", "decide")
    .AddConditionalEdge("decide",
        state => state.Pending.Count > 0
            ? "process"
            : StateGraph<PipelineState>.EndNode,
        ["process", StateGraph<PipelineState>.EndNode]);

// Compiler et exécuter
var runner = graph.Compile();

runner.OnNodeCompleted += (_, args) =>
    Console.WriteLine($"Node '{args.NodeName}' done (#{args.TransitionOrdinal})");

runner.OnCircuitBroken += (_, args) =>
    Console.WriteLine($"CIRCUIT BROKEN at '{args.NodeName}': {args.Reason}");

var result = await runner.RunAsync(new PipelineState
{
    Pending = new Queue<string>(["a", "b", "c"])
});

Console.WriteLine($"Trace: {string.Join(" -> ", result.Trace)}");
Console.WriteLine($"Total transitions: {result.TotalTransitions}");
```

### Usage via YAML (ProcessType.Graph)

```csharp
using Orkeon.Infrastructure.Configuration;
using Orkeon.Domain.Shared.ValueObjects;

// Charger la config YAML
var loader = serviceProvider.GetRequiredService<ICrewDefinitionLoader>();
var config = await loader.LoadFromFileAsync("config.yaml");

// La factory crée automatiquement le GraphProcessStrategy
var factory = serviceProvider.GetRequiredService<IProcessStrategyFactory>();
var strategy = factory.CreateStrategy(ProcessType.Graph);

// Exécuter
var result = await strategy.ExecuteSequentialAsync(crew, plan, inputVariables);
```

### Building a custom graph with more nodes

```csharp
var graph = new StateGraph<MyState>(new CircuitBreakerPolicy
    {
        MaxTransitions = 100,
        MaxStateVisits = 5,
        MaxTotalDuration = TimeSpan.FromMinutes(15)
    })
    .AddNode("fetch", async (s, ct) => { /* ... */ return s; })
    .AddNode("validate", async (s, ct) => { /* ... */ return s; })
    .AddNode("transform", async (s, ct) => { /* ... */ return s; })
    .AddNode("error_handler", async (s, ct) => { /* ... */ return s; })
    .AddEdge(StateGraph<MyState>.StartNode, "fetch")
    .AddEdge("fetch", "validate")
    .AddConditionalEdge("validate",
        s => s.IsValid ? "transform" : "error_handler",
        ["transform", "error_handler"])
    .AddEdge("transform", StateGraph<MyState>.EndNode)
    .AddConditionalEdge("error_handler",
        s => s.RetryCount < 3 ? "fetch" : StateGraph<MyState>.EndNode,
        ["fetch", StateGraph<MyState>.EndNode]);

var runner = graph.Compile();
var result = await runner.RunAsync(initialState);
```

## Example 102

The `examples/09-experimental/102-graph-orchestration/` example demonstrates the full integration with:

- `process: "graph"` to enable the `GraphProcessStrategy`
- `graphConfig` with `maxRetryCycles: 2` and the "strict" preset
- 4 agents (Data Collector, Data Validator, Insight Analyst, Report Writer)
- 4 tasks with linear dependencies
- Automatic retry of failed tasks (transient web scraping failures)

See the example's `config.yaml` and `README.md` files for the complete syntax.

## Relationship with the existing code

### FSM TaskExecutionStateMachine

The StateGraph and the FSM operate at different levels:

- **FSM**: manages the internal execution of a task (Assigned → Executing → ToolCalling → Validating → Completed)
- **StateGraph**: manages the flow between tasks (which task to execute, when to retry, when to finish)

The two are complementary and coexist. When the `GraphProcessStrategy` executes a task via `IAgentExecutionService.ExecuteTaskAsync()`, the FSM manages that task's internal cycle.

### SequentialCrewOrchestrator

The existing orchestrator is not replaced. The `GraphProcessStrategy` is an alternative to the `SequentialProcessStrategy` within the same `IProcessStrategy` framework. The orchestrator uses the `ProcessStrategyFactory` to pick the right strategy based on `ProcessType`.

### Existing process strategies

The `GraphProcessStrategy` coexists with the existing strategies:

```
ProcessStrategyFactory.CreateStrategy(processType) switch
{
    "Sequential"   → SequentialProcessStrategy
    "Hierarchical" → HierarchicalProcessStrategy
    "Parallel"     → ParallelProcessStrategy
    "Graph"        → GraphProcessStrategy
}
```

The addition does not impact the existing strategies. Client code chooses via `ProcessType.Graph` or `process: "graph"` in the YAML.

## Tests

30+ unit tests cover the engine and the strategy:

| Test file | Coverage |
|-----------------|-----------|
| `StateGraphTests.cs` | Linear flow, conditional routing, controlled loops, circuit breaker (max transitions, cycle detection), observability (OnNodeCompleted, OnCircuitBroken), cancellation, graph validation, state mutation |
| `GraphProcessStrategyTests.cs` | Happy path (0 tasks, 1 task, N tasks), controlled retry (success after retry, abandon after max), circuit breaker integration, unsupported process types, missing tasks, missing agents |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/ --filter "FullyQualifiedName~StateGraph"
dotnet test tests/core/Orkeon.Infrastructure.Tests/ --filter "FullyQualifiedName~GraphProcessStrategy"
```
