> 🇫🇷 [Version française](../fr/orchestration/graph.md)

> **See also**: [ProcessTypes comparison guide](./process-types.md) · [FSM orchestration](./fsm.md) · [YAML schema](../architecture/yaml-schema.md) · [Back to index](../INDEX.md)

# Typed state graph orchestration (StateGraph)

## Overview

Orkeon provides a typed state graph engine (`StateGraph<TState>`) in the Domain layer, inspired by LangGraph: nodes transform a typed state, fixed and conditional edges route between them, and cycles are allowed under a circuit breaker.

The engine serves two audiences:

- **The Graph process mode** — `process: graph` in YAML (or `ProcessType.Graph`) runs the crew through `GraphProcessStrategy`, which builds a fixed two-node graph: execute the next task, then route (retry the failed tasks, loop, or end). It is configured by the `graphConfig` block.
- **Your own C# workflows** — build any `StateGraph<TState>` with your own nodes and conditional edges, compile it and run it.

### Positioning relative to the other strategies

| Strategy | Granularity | Cycles | Conditional routing | Circuit breaker |
|-----------|-------------|--------|----------------------|-----------------|
| Sequential | Crew | No | No | No |
| Hierarchical | Crew | Review loop (3 reviews) | Manager LLM | No |
| Parallel | Crew | No | No | No |
| **Graph** | **Crew** | **Yes (retry cycles)** | **Fixed in YAML; free with the C# API** | **Yes (built-in)** |
| FSM | Task (Domain building block) | Yes (guards) | Yes (events/guards) | Yes (built-in) |

The FSM models a task's internal cycle but is not wired into any strategy today (see [FSM orchestration](./fsm.md)); the Graph mode is the runtime consumer of `CircuitBreakerPolicy`.

## Architecture

### Domain layer — Generic engine

The engine components live in `Orkeon.Domain.Graph`:

| Class | Role |
|--------|------|
| `StateGraph<TState>` | Graph definition: `AddNode`, `AddEdge`, `AddConditionalEdge`, `Compile`; sentinels `StartNode` (`"__start__"`) and `EndNode` (`"__end__"`) |
| `GraphNode<TState>` | Processing node: `Func<TState, CancellationToken, Task<TState>>` |
| `IGraphEdge<TState>` | Interface for routing (fixed or conditional) |
| `FixedEdge<TState>` (internal) | Unconditional edge to a target node — built via `AddEdge` |
| `ConditionalEdge<TState>` (internal) | Edge with a routing function `Func<TState, string>` — built via `AddConditionalEdge` |
| `GraphRunner<TState>` | Execution engine with circuit breaker and observability (`RunAsync`) |
| `GraphExecutionResult<TState>` | Result: `FinalState`, `Trace`, `TotalTransitions`, `Duration` |
| `NodeCompletedEventArgs<TState>`, `GraphCircuitBrokenEventArgs` | Payloads of `OnNodeCompleted` / `OnCircuitBroken` |
| `GraphCircuitBrokenException` | Exception thrown when the circuit breaker trips (`NodeName`, `TransitionCount`, `Trace`) |

`TState` must be a reference type (`where TState : class`); nodes usually mutate and return the same instance. `new StateGraph<TState>()` without a policy uses `CircuitBreakerPolicy.Strict`.

`Compile()` validates the structure: an edge must leave `StartNode`, every node needs exactly one outgoing edge, and fixed edges must target a registered node (or `EndNode`). A conditional edge declaring its `possibleTargets` throws at run time if the router returns a name outside that list.

### Domain layer — Configuration

| Class | Role |
|--------|------|
| `GraphConfig` | Immutable DTO for the `graphConfig` block (`MaxRetryCycles` = 2, `CircuitBreakerPreset` = `"strict"`, nullable overrides) |

`GraphConfig?` is carried by `CrewConfiguration.GraphConfig` and by the `Crew` aggregate (`Crew.GraphConfig`, settable with `CrewBuilder.WithGraphConfig(...)`).

### Infrastructure layer — Integration

| Class | Role |
|--------|------|
| `GraphProcessStrategy` | Implements `IProcessStrategy` (`ExecuteSequentialAsync`), builds and runs the crew graph |
| `CrewGraphState` | Typed state traversing the graph (tasks, agents, outputs, retries, token counters) |
| `GraphYamlConfig` | YAML model for the `graphConfig` section |
| `CircuitBreakerPolicyFactory.ResolveGraph` | Resolves the effective policy from `graphConfig`, the crew `circuitBreaker` and the fallback |

### Where the mode is wired

| Place | Role |
|--------|-------------|
| `ProcessType.Graph` | The value object member (`"Graph"`) |
| `YamlCrewMapper.ParseProcessType` | `process: graph` (case-insensitive) |
| `ProcessStrategyFactory` | `"Graph"` → `GraphProcessStrategy` |
| `SequentialCrewOrchestrator` | Dispatches `"Graph"` to `ExecuteSequentialAsync` |
| `AddOrkeonInfrastructure()` | Registers `GraphProcessStrategy` (scoped) |

## Graph topology

```
START ──► [execute_task] ──► [route] ──┬──► [execute_task]   (tasks pending, or failed ones re-enqueued)
                                       │
                                       └──► END              (nothing left)
```

The `execute_task` node dequeues the next task (in the order resolved from the plan or the declared `dependencies`), picks its agent — the task's `agent:` when declared, otherwise the configured selector (round-robin by default, see [agent selection](./process-types.md#who-runs-a-task-that-names-no-agent)) — executes it and accumulates the result into `CrewGraphState`. Agents with `allowDelegation: true` receive the delegation tools.

The `route` node inspects the state:

- If tasks remain in the queue → loops back to `execute_task`
- If the queue is empty and some tasks failed with retries left → re-enqueues them and loops back
- Otherwise → routes to END

The crew's output is the last result produced. A task still failing after its retries stays in the outputs as failed, but the crew is reported as **completed**: only the circuit breaker fails a graph run. Unlike the Sequential mode, a failed task does not skip the tasks that depend on it.

## Circuit breaker

The circuit breaker is built into `GraphRunner<TState>` and checks three conditions before each node:

### Protection mechanisms

| Mechanism | Parameter | Description |
|-----------|-----------|-------------|
| Max transitions | `MaxTransitions` | Total number of node executions |
| Cycle detection | `MaxStateVisits` | Maximum number of visits to the same node (0 disables it) |
| Total duration | `MaxTotalDuration` | Maximum lifetime of the run (`TimeSpan.Zero` disables it) |

The StateGraph reuses `CircuitBreakerPolicy` from `Orkeon.Domain.Common.StateMachine` (the same record as the FSM); its `StateTimeout` and `UseDegradedMode` are not used by the graph runner.

In the Graph mode, each task attempt is one visit of `execute_task` (and one of `route`), so:

- `MaxStateVisits` caps the number of **task attempts** in the whole run — 5 under the Strict default;
- `MaxTransitions` caps them at half its value (two node executions per attempt).

Size `maxStateVisits` for the number of tasks plus their retries. When a condition is violated, a `GraphCircuitBrokenException` is thrown with the complete trace; `GraphProcessStrategy` catches it and returns a failed `CrewOutput` ("Graph execution stopped by circuit breaker: …") that keeps the outputs and the token usage produced so far.

### Presets

| Preset | MaxTransitions | MaxStateVisits | MaxTotalDuration |
|--------|---------------|----------------|------------------|
| `Strict` (default) | 50 | 5 | 10 min |
| `Default` | 100 | 10 | 30 min |
| `Permissive` | 1000 | 50 | 2 h |

### Observability

The `GraphRunner<TState>` exposes two events:

- `OnNodeCompleted`: raised after each node, with `NodeCompletedEventArgs<TState>` (`NodeName`, `State`, `TransitionOrdinal`, `TraceSnapshot`)
- `OnCircuitBroken`: raised when the circuit trips, with `GraphCircuitBrokenEventArgs` (`Reason`, `NodeName`, `TransitionCount`, `Trace`)

`GraphProcessStrategy` subscribes to both for structured logging (source-generated `LoggerMessage`). A graph's nodes are not tasks: the strategy announces each task start live through `ICrewExecutionHook`, and reports the task completions when the graph settles.

## Controlled retry

The `GraphProcessStrategy` adds a retry mechanism on top of the circuit breaker:

| Parameter | Field | Description |
|-----------|-------|-------------|
| Retry cycles | `MaxRetryCycles` | Retries per failed task (default 2) |

How it works:

1. When a task fails, its counter in `RetryCounts` increases; while it stays ≤ `MaxRetryCycles`, the task goes to `FailedTaskIds`
2. When `PendingTaskIds` is empty, the `route` node moves every task of `FailedTaskIds` back to the pending queue
3. A task whose counter exceeds `MaxRetryCycles` is abandoned (its failed output stays)
4. Every attempt is recorded (one output and one usage entry per attempt), and counts against the circuit breaker

## YAML configuration

### `graphConfig` schema

The `graphConfig` block sits at the root of the crew file:

```yaml
name: "my-crew"
goal: "…"
process: "graph"

graphConfig:
  maxRetryCycles: int           # default: 2 — retries per failed task
  circuitBreakerPreset: string  # "strict" (default) | "permissive" | "default"
  maxTransitions: int           # Overrides the preset
  maxStateVisits: int           # Cycle detection / task-attempt cap (overrides the preset)
  maxTotalDurationSeconds: int  # Total duration in seconds (overrides the preset)

agents:
  <agent_id>:
    # ... same schema as sequential
tasks:
  <task_id>:
    # ... same schema as sequential
```

### Resolution hierarchy

```
With a graphConfig block:
1. graphConfig explicit fields       (highest priority)
2. graphConfig.circuitBreakerPreset  (base values; "strict" when absent or unknown)
   — a crew-level circuitBreaker is then ignored

Without graphConfig:
3. crew-level circuitBreaker config  (its preset + its overrides, see FSM)
4. CircuitBreakerPolicy.Strict       (fallback when nothing is configured)
```

`maxRetryCycles` comes from `graphConfig` only (2 otherwise).

### Configuration flow to execution

The `graphConfig` block travels all the way to the running graph:

1. `YamlCrewMapper` maps the YAML into `CrewConfiguration.GraphConfig` (the loader deserializes and delegates; single-file `CrewYamlConfig` and multi-file `CrewSettingsYamlConfig` both carry the block).
2. `CrewFactory` carries it (and any crew-level `circuitBreaker`) onto the domain `Crew`
   aggregate (`Crew.GraphConfig` / `Crew.CircuitBreaker`), so it survives to execution time.
3. At execution, `GraphProcessStrategy` reads the config **off the crew argument** and resolves the
   effective `CircuitBreakerPolicy` + `MaxRetryCycles` via `CircuitBreakerPolicyFactory.ResolveGraph`.
   Reading from the crew (not from the shared, scoped strategy instance) keeps per-crew settings from
   leaking between concurrent executions.

When the crew carries neither block, the strategy uses its own `CircuitPolicy` (`Strict`) and `MaxRetryCycles` (2) properties.

### YAML models

| C# model | YAML class | File |
|-----------|-------------|---------|
| `GraphConfig` | `GraphYamlConfig` | `Configuration/Yaml/YamlConfigModels.cs` |

The mapping is performed by `YamlCrewMapper.MapGraphConfig()` (private, `Configuration/Yaml/`).

## Usage in C# code

### Direct StateGraph usage (generic framework)

```csharp
using Orkeon.Domain.Graph;
using Orkeon.Domain.Common.StateMachine;

// Define a state type
class PipelineState
{
    public Queue<string> Pending { get; set; } = new();
    public List<string> Results { get; set; } = [];
    public int RetryCount { get; set; }
}

// Build the graph
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

// Compile and run
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
using Orkeon.Application.Interfaces;           // ICrewFactory
using Orkeon.Application.Interfaces.Services;  // ICrewOrchestrationService, CrewInput

// Load the crew (process: graph + graphConfig) — the path is a VFS path
var crewFactory = serviceProvider.GetRequiredService<ICrewFactory>();
var crew = await crewFactory.CreateFromFileAsync("/workspace/config.yaml");

// The orchestrator resolves GraphProcessStrategy through the ProcessStrategyFactory
var orchestrator = serviceProvider.GetRequiredService<ICrewOrchestrationService>();
var output = await orchestrator.KickoffAsync(crew.Id, CrewInput.Empty());

Console.WriteLine(output.Succeeded ? output.FinalOutput : output.Error);
```

The Fluent Builder equivalent is `new CrewBuilder().Process(ProcessType.Graph).WithGraphConfig(new GraphConfig { MaxRetryCycles = 3 })…`.

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

The same engine drives the corrective RAG pipeline (`CorrectiveRagPipeline` on `StateGraph<RagGraphState>`, see [RAG pipeline](../architecture/rag-pipeline.md)). The scripting DSL has its own `stateGraph()` literal (`graph.d.ts`), a separate JavaScript implementation configured by a `graphConfig` of transition limits — see [Scripting](../architecture/scripting.md).

## Example 102

The [102-graph-orchestration](https://github.com/orkeon/orkeon/blob/main/examples/09-experimental/102-graph-orchestration/) example demonstrates the full integration with:

- `process: "graph"` to enable the `GraphProcessStrategy`
- `graphConfig` with `maxRetryCycles: 2` and the "strict" preset
- 4 agents (Data Collector, Data Validator, Insight Analyst, Report Writer)
- 4 tasks with linear dependencies
- Automatic retry of failed tasks (transient web scraping failures)

See the example's `config.yaml` and `README.md` files for the complete syntax.

## Relationship with the existing code

### FSM TaskExecutionStateMachine

The StateGraph and the FSM model different levels:

- **FSM**: the internal execution of a task (Assigned → Executing → ToolCalling → Validating → Completed) — a Domain building block, not driven by any strategy today
- **StateGraph**: the flow between tasks (which task to execute, when to retry, when to finish)

When the `GraphProcessStrategy` executes a task, it calls `IAgentExecutionService.ExecuteTaskAsync()`, whose agent loop bounds the task (`maxIter`, identical-error stop).

### SequentialCrewOrchestrator

The `GraphProcessStrategy` is one of the six `IProcessStrategy` implementations. The orchestrator uses the `ProcessStrategyFactory` to pick the strategy from the `ProcessType`, then calls its `ExecuteSequentialAsync` entry point:

```
ProcessStrategyFactory.CreateStrategy(processType) switch
{
    "Sequential"   → SequentialProcessStrategy
    "Hierarchical" → HierarchicalProcessStrategy
    "Parallel"     → ParallelProcessStrategy
    "Consensual"   → ConsensualProcessStrategy
    "Graph"        → GraphProcessStrategy
    "Autonomous"   → AutonomousProcessStrategy
}
```

Client code chooses via `ProcessType.Graph` or `process: "graph"` in the YAML.

## Tests

| Test file | Coverage |
|-----------------|-----------|
| `Orkeon.Domain.Tests/Graph/StateGraphTests.cs` (18) | Linear flow, conditional routing, controlled loops, circuit breaker (max transitions, cycle detection), observability (OnNodeCompleted, OnCircuitBroken), cancellation, graph validation, state mutation |
| `Orkeon.Infrastructure.Tests/Strategies/GraphProcessStrategy/GraphProcessStrategyTests.cs` (17) | Happy path (0, 1, N tasks), controlled retry (success after retry, abandon after max), circuit breaker integration, unsupported entry points, missing tasks, missing agents |
| `Orkeon.Infrastructure.Tests/Configuration/CircuitBreakerPolicyFactoryGraphTests.cs` (5) | `ResolveGraph` precedence (graphConfig, crew circuitBreaker, fallback) |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --filter "FullyQualifiedName~StateGraph"
dotnet test tests/core/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GraphProcessStrategy"
```
