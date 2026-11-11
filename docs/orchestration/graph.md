> 🇫🇷 [Version française](../fr/orchestration/graph.md)

> **See also**: [ProcessTypes comparison guide](./process-types.md) · [FSM orchestration](./fsm.md) · [YAML schema](../architecture/yaml-schema.md) · [Back to index](../INDEX.md)

# Typed state graph orchestration (StateGraph)

## Overview

Orkeon provides a typed state graph engine (`StateGraph<TState>`) in the Domain layer, inspired by LangGraph: nodes transform a typed state, fixed and conditional edges route between them, and cycles are allowed under a circuit breaker.

The engine serves two audiences:

- **The Graph process mode** — `process: graph` in YAML (or `ProcessType.Graph`) runs the crew through `GraphProcessStrategy`, which builds one node per task, chained in execution order (a task's attempts are its own `retry:`, not a loop of its node). It is described and bounded by the `graph:` block (nodes, edges, budgets; [ADR-018](../adr/ADR-018-graph-engines-shared-topology-and-yaml-graph-block.md)).
- **Your own C# workflows** — build any `StateGraph<TState>` with your own nodes and conditional edges, compile it and run it.

### Positioning relative to the other strategies

| Strategy | Granularity | Cycles | Conditional routing | Circuit breaker |
|-----------|-------------|--------|----------------------|-----------------|
| Sequential | Crew | No | No | No |
| Hierarchical | Crew | Review loop (3 reviews) | Manager LLM | No |
| Parallel | Crew | No | No | No |
| **Graph** | **Crew** | **Yes (an edge back to a node, bounded by `maxVisits`)** | **Decision edges (`when`) in YAML; any router with the C# API** | **Yes (built-in)** |
| FSM | Any (generic Domain engine) | Yes (guards) | Yes (events/guards) | Yes (built-in) |

The FSM is a generic engine (`orkeon forge` runs on it) that no crew strategy uses (see [FSM](./fsm.md)); the Graph mode shares its `CircuitBreakerPolicy`.

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
| `NodeEnteredEventArgs`, `NodeCompletedEventArgs<TState>`, `GraphCircuitBrokenEventArgs` | Payloads of `OnNodeEntered` / `OnNodeCompleted` / `OnCircuitBroken` |
| `GraphCircuitBrokenException` | Exception thrown when the circuit breaker trips (`NodeName`, `TransitionCount`, `Trace`) |

`TState` must be a reference type (`where TState : class`); nodes usually mutate and return the same instance. `new StateGraph<TState>()` without a policy uses `CircuitBreakerPolicy.Strict`.

`Compile()` validates the structure: an edge must leave `StartNode`, every node needs exactly one outgoing edge, and fixed edges must target a registered node (or `EndNode`). A conditional edge declaring its `possibleTargets` throws at run time if the router returns a name outside that list.

### Domain layer — Configuration

| Class | Role |
|--------|------|
| `GraphConfig` | Immutable DTO for the `graph:` block: `Start`, `Nodes` (`GraphNodeConfig`: id and task key), `Edges` (`GraphEdgeConfig`: from, to, optional `TaskCondition`), `CircuitBreakerPreset` = `"strict"`, nullable budgets |

`GraphConfig?` is carried by `CrewConfiguration.GraphConfig` and by the `Crew` aggregate (`Crew.GraphConfig`, settable with `CrewBuilder.WithGraphConfig(...)`).

### Infrastructure layer — Integration

| Class | Role |
|--------|------|
| `GraphProcessStrategy` | Implements `IProcessStrategy` (`ExecuteSequentialAsync`), builds and runs the crew graph |
| `CrewGraphState` | Typed state traversing the graph (tasks, agents, outputs, retries, token counters) |
| `GraphYamlConfig` | YAML model for the `graph:` block (with `GraphNodeYamlConfig` and `GraphEdgeYamlConfig`) |
| `CircuitBreakerPolicyFactory.ResolveGraph` | Resolves the effective policy from `graph:`, else the fallback |

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
START ──► [first] ──► [second] ──► … ──► [last] ──► END
```

A crew in the Graph process builds one node per task (ADR-018). A node's id is its task's name (the task id when the name is missing or taken), without nodes or edges in `graph:`, the nodes are chained in the stable topological order of the declared `dependencies` (`CrewTaskSequencer`), and the last one leads to END. A crew with no task goes from START to END.

A node picks its agent — the task's `agent:` when declared, otherwise the configured selector (round-robin by default, see [agent selection](./process-types.md#who-runs-a-task-that-names-no-agent)) — executes the task and accumulates the result into `CrewGraphState`. Agents with `allowDelegation: true` receive the delegation tools. A task skipped by its dependencies or its `when` condition still passes through its node.

Without nodes or edges in `graph:`, the edge out of a node is plain: it leads to the next node, or to END. With them, the topology is the declared one (see [`graph:` schema](#graph-schema)). The graph does not retry a failed task: its attempts are its own `retry:`, applied below the strategy.

The crew's output is the last result produced. A task still failing after its retries stays in the outputs as failed and **fails the crew**, as in every mode: `Success = false`, an error naming it, a `Failed` hook, exit code 2. The tasks that depend on it are **skipped**. A task's `retry:` runs inside its own turn, before the next task starts, so a dependant only ever sees a dependency that succeeded or gave up.

## Circuit breaker

The circuit breaker is built into `GraphRunner<TState>` and checks three conditions before each node:

### Protection mechanisms

| Mechanism | Parameter | Description |
|-----------|-----------|-------------|
| Max transitions | `MaxTransitions` | Total number of node executions |
| Cycle detection | `MaxStateVisits` | Maximum number of visits to the same node (0 disables it) |
| Total duration | `MaxTotalDuration` | Maximum lifetime of the run (`TimeSpan.Zero` disables it) |

The StateGraph reuses `CircuitBreakerPolicy` from `Orkeon.Domain.Common.StateMachine` (the same record as the FSM); its `StateTimeout` and `UseDegradedMode` are not used by the graph runner.

In the Graph mode, each entry into a node is one visit (a task's `retry:` attempts happen inside it), so:

- `MaxStateVisits` (`maxVisits` in YAML) caps the **visits of one node**: a loop that enters a node again counts there;
- `MaxTransitions` caps the node executions of the whole run.

Without an explicit `maxVisits` / `maxTransitions`, both are **computed from the crew**: the sum over the tasks of `1 + retries` visits (the attempts of each task's `retry:` count; `tasks × (1 + retries)` when they all retry alike) — the most a crew can make — and `2 × visits + 1` transitions. A healthy crew is never cut short whatever its size; a real loop still trips the breaker. An explicit value in `graph:` wins. `MaxTotalDuration` stays the preset's — 10 minutes under Strict, the default — a cost bound `maxTotalDurationSeconds` overrides. When a condition is violated, a `GraphCircuitBrokenException` is thrown with the complete trace; `GraphProcessStrategy` catches it and returns a failed `CrewOutput` ("Graph execution stopped by circuit breaker: …") that keeps the outputs and the token usage produced so far.

### Presets

The preset supplies what is not computed — the total duration first. Its visit and transition caps apply only where the strategy is given a fixed policy (`GraphProcessStrategy.CircuitPolicy`, in C#) and the crew carries no configuration.

| Preset | MaxTransitions | MaxStateVisits | MaxTotalDuration |
|--------|---------------|----------------|------------------|
| `Strict` (default) | 50 | 5 | 10 min |
| `Default` | 100 | 10 | 30 min |
| `Permissive` | 1000 | 50 | 2 h |

### Limit of the topology checks

Both engines check the shape of a graph with `GraphTopologyValidator` when it is built (ADR-018). One limit: an edge whose targets are unknown, a script's function edge or a `StateGraph` conditional edge declared without `possibleTargets`, is assumed to reach END **and every declared node**. This goes beyond ADR-018 3.6, which only says such an edge reaches END. Once a function edge is reachable from START, the orphan check and the no-path-to-END check are therefore inert for the graph: they cannot find a node it would never reach, because the edge might lead to it. The checks that stay exact are the unknown ids, the nodes with no outgoing edge, the reserved or repeated ids and the fixed-edge rule. Declare `possibleTargets` on a conditional edge to keep the full checks.

### Observability

The `GraphRunner<TState>` exposes three events:

- `OnNodeEntered`: raised when a node is about to run, after the breaker let it through, with `NodeEnteredEventArgs` (`NodeName`, `Visit`: how many times the node has been entered, this entry included). It is the source of the `graph.node.entered` run event
- `OnNodeCompleted`: raised after each node, with `NodeCompletedEventArgs<TState>` (`NodeName`, `State`, `TransitionOrdinal`, `TraceSnapshot`)
- `OnCircuitBroken`: raised when the circuit trips, with `GraphCircuitBrokenEventArgs` (`Reason`, `NodeName`, `TransitionCount`, `Trace`)

`GraphProcessStrategy` subscribes to both for structured logging (source-generated `LoggerMessage`). The strategy also tells the host's `IGraphNodeObserver`, if one is registered, each node entered (`GraphNodeEntered`: the node, its visit, the task id); `orkeon run --events jsonl` writes it as a [`graph.node.entered`](../architecture/run-event-bus.md) line. The strategy announces each task start live through `ICrewExecutionHook`, and reports the task completions when the graph settles.

## Retry

A task's attempts are its own `retry:` block (`attempts`, `backoffMs`, `backoff`), applied by one decorator below every strategy, in all six modes: see [Task retry](../architecture/yaml-schema.md#task-retry). The graph has **no retry loop of its own**: a task that fails after its last attempt ends the run's task list as failed, fails the crew, and its dependants are skipped.

| What | Where |
|------|-------|
| Attempts of a task | the task's `retry:` (`attempts` runs in all, the first included) |
| Wait between attempts | `backoffMs` and `backoff` (`fixed` or `exponential`), on the host's `TimeProvider`, cancelled with the run |
| Visits of a node | one per entry into the node (a loop enters it again): a task's attempts happen inside its visit, but they count in the default budget (below) |

The default breaker bounds add the attempts up, so a crew whose tasks retry is never cut short by them: `maxVisits` defaults to the sum over the tasks of `1 + retries` (`retries` = `attempts - 1`), `maxTransitions` to twice that plus one. A `graphConfig` block (and any `maxRetryCycles`) is refused at load, pointing to `graph:`. There is no `maxCycles` key either: a bound on cycles is `maxVisits`. Each attempt of a task that is replayed is one `task.started` carrying its `attempt`.

## YAML configuration

### `graph:` schema

The `graph:` block sits at the root of the crew file (read with `process: graph` only: elsewhere it is refused at load):

```yaml
name: "my-crew"
goal: "…"
process: "graph"

graph:
  start: triage              # optional: the first node by default
  nodes:                     # optional: absent = one implicit node per task, its id the task's name
    - { id: triage,   task: triage }
    - { id: fix,      task: fix }
    - { id: escalate, task: escalate }
  edges:
    - { from: triage, to: fix, when: { triage.category: [bug] } }
    - { from: triage, to: escalate }          # no `when`: the "otherwise"
    - { from: fix,      to: END }
    - { from: escalate, to: END }
  circuitBreakerPreset: strict   # "strict" (default) | "permissive" | "default"
  maxTransitions: 50             # default: computed, 2 x visits + 1
  maxVisits: 5                   # visits of ONE node - default: computed, sum over the tasks of 1 + their retries
  maxTotalDurationSeconds: 600   # total duration (overrides the preset)
```

- **Nodes.** A node is `{id, task}`: `task` names a task of the crew, `START` and `END` are refused as ids. A task is run by one node at most; when `nodes` is given, a task no node names is refused. `edges` without `nodes` gives one implicit node per task. A node is revisited by an edge leading back to it: on a revisit the latest output and the latest decision win. A node whose task fails ends the run: there is no routing on failure (a retry is the task's `retry:`).
- **Edges.** Several `edges` from one `from` are one conditional edge, evaluated in the order declared: the first whose `when` holds wins, and the edge without `when` is the "otherwise". A `from` that carries `when` edges is a [decision task](../architecture/decision-models.md) and has exactly one edge without `when`; an ordinary task has exactly one edge. The `when` keeps the grammar of the task-level one, with its `<task>.` prefix: `<task>.<question>: [option, ...]`, `<task>.<question>: ">= 0.7"`, `<task>.<question>.<option>: ">= 0.25"`; the task is the `from` task, and its questions are checked at load. `otherwise` is not written: an edge without `when` is it.
- **Refused at load**, each message naming the node: a `when` on a `from` that is not a decision task, a decision with no edge without `when`, two edges without `when` from one node, several edges on an ordinary task, an orphan node, a node with no way to `END`, an edge to an unknown node, a `graph:` outside `process: graph`, and a task-level `when:` or `dependencies:` in a crew that has nodes or edges (the edges order the tasks, a second router would contradict them). The topology faults come from `GraphTopologyValidator`, all at once, and the export checks the same rules before it writes.
- **Budgets.** `maxVisits` is the Domain's `MaxStateVisits`, counted per node: a node entered more often than that trips the breaker. Left unset, `maxVisits` is the sum over the tasks of `1 + retries` and `maxTransitions` twice that plus one; an explicit value wins.
- **Refused keys.** `graphConfig:`, `maxRetryCycles` and `maxCycles` are refused at load, pointing to `graph:` (a bound on cycles is `maxVisits`; a retry is the task's `retry:`).
- **Execution.** The Domain engine runs every crew of the Graph process, with a `graph:` block or without; `JsStateGraph` runs only the `stateGraph(...)` of a `.ork.ts` script. A resumed run walks the graph again: the executions of a task that already ended are answered from the checkpoint, keyed `<task>#<n>` by their order, so a node entered twice has two entries. The export writes `graph:` back, so import, export and import give the same graph.

The `agents:` and `tasks:` are the sequential schema; a decision task is a task with a `decision:` block ([decision tasks](../architecture/decision-models.md)).

### Resolution hierarchy

```
With a graph block:
1. graph explicit fields                (highest priority)
2. graph.circuitBreakerPreset         (base values; "strict" when absent or unknown)

Without a graph block:
3. CircuitBreakerPolicy.Strict       (fallback: supplies the duration only)
```

`graph:` is the crew's only circuit-breaker setting: a `circuitBreaker:` block, at the crew root or on a task, is refused at load. Whatever the level, `maxVisits` and `maxTransitions` left unset are **computed from the crew** rather than taken from the preset (see [Circuit breaker](#circuit-breaker)); the preset supplies the duration. A failed task is retried by its own `retry:` (see [Retry](#retry)), never by the graph: a `graphConfig` block is refused at load.

### Configuration flow to execution

The `graph:` block travels all the way to the running graph:

1. `YamlCrewMapper` maps the YAML into `CrewConfiguration.GraphConfig` (the loader deserializes and delegates; single-file `CrewYamlConfig` and multi-file `CrewSettingsYamlConfig` both carry the block).
2. `CrewFactory` carries it onto the domain `Crew` aggregate (`Crew.GraphConfig`), so it
   survives to execution time.
3. At execution, `GraphProcessStrategy` reads the config **off the crew argument** and resolves the
   effective `CircuitBreakerPolicy` via `CircuitBreakerPolicyFactory.ResolveGraph`.
   Reading from the crew (not from the shared, scoped strategy instance) keeps per-crew settings from
   leaking between concurrent executions.

When the crew carries no `graph:` block, the strategy computes the visit and transition bounds on the Strict preset's duration; its `CircuitPolicy` property, null by default, replaces that computed policy when a C# caller sets it.

### YAML models

| C# model | YAML class | File |
|-----------|-------------|---------|
| `GraphConfig` | `GraphYamlConfig` | `Configuration/Yaml/YamlConfigModels.cs` |

The mapping is performed by `YamlCrewMapper.MapGraph()` (private, `Configuration/Yaml/YamlCrewMapper.Graph.cs`), which refuses every fault of the block at once; the export (`YamlCrewExporter`) writes it back after the same topology check.

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

// Load the crew (process: graph + graph:) — the path is a VFS path
var crewFactory = serviceProvider.GetRequiredService<ICrewFactory>();
var crew = await crewFactory.CreateFromFileAsync("/workspace/config.yaml");

// The orchestrator resolves GraphProcessStrategy through the ProcessStrategyFactory
var orchestrator = serviceProvider.GetRequiredService<ICrewOrchestrationService>();
var output = await orchestrator.KickoffAsync(crew.Id, CrewInput.Empty());

Console.WriteLine(output.Succeeded ? output.FinalOutput : output.Error);
```

The Fluent Builder equivalent is `new CrewBuilder().Process(ProcessType.Graph).WithGraphConfig(new GraphConfig { MaxStateVisits = 20 })…`.

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

The same engine drives the corrective RAG pipeline (`CorrectiveRagPipeline` on `StateGraph<RagGraphState>`, see [RAG pipeline](../architecture/rag-pipeline.md)). The scripting DSL has its own `stateGraph()` literal (`graph.d.ts`), a separate JavaScript implementation configured by a `graphConfig` of transition limits (the script's own key, unrelated to the crew's `graph:` block) — see [Scripting](../architecture/scripting.md).

## Example 102

The [102-graph-orchestration](https://github.com/orkeon/orkeon/blob/main/examples/09-experimental/102-graph-orchestration/) example demonstrates the full integration with:

- `process: "graph"` to enable the `GraphProcessStrategy`
- a `graph:` block (nodes, edges, the "strict" preset) and a task `retry:`
- 4 agents (Data Collector, Data Validator, Insight Analyst, Report Writer)
- 4 tasks with linear dependencies
- Automatic retry of failed tasks (transient web scraping failures)

See the example's `config.yaml` and `README.md` files for the complete syntax.

## Relationship with the existing code

### Tasks inside the graph

The StateGraph models the flow between tasks (which task to execute, when to retry, when to finish); it does not model a task's own execution. When the `GraphProcessStrategy` executes a task, it calls `IAgentExecutionService.ExecuteTaskAsync()`, whose agent loop bounds the task (`maxIter`, identical-error stop, output-validation retries). The generic [FSM engine](./fsm.md) shares the graph's `CircuitBreakerPolicy` but runs no task.

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
| `Orkeon.Infrastructure.Tests/Strategies/GraphProcessStrategy/GraphProcessStrategyTests.cs` (16) | Happy path (0, 1, N tasks), controlled retry (success after retry, abandon after max), circuit breaker integration, unsupported entry points, missing tasks, missing agents |
| `Orkeon.Infrastructure.Tests/Configuration/CircuitBreakerPolicyFactoryGraphTests.cs` (4) | `ResolveGraph`: presets, overrides, fallback |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --filter "FullyQualifiedName~StateGraph"
dotnet test tests/core/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GraphProcessStrategy"
```
