> 🇫🇷 [Version française](../fr/orchestration/autonomous.md)

> **See also**: [ProcessTypes comparison guide](./process-types.md) · [YAML schema](../architecture/yaml-schema.md) · [Back to index](../INDEX.md)

# Autonomous agentic orchestration

## Overview

Orkeon provides an autonomous orchestration mode (`ProcessType.Autonomous`): for each task, a manager LLM picks the agent that claims it; when that agent fails and allows delegation, the task is handed to a peer over an agent-to-agent channel. All execution is constrained by a multi-dimensional `AgentExecutionBudget` that guarantees termination.

> **Experimental API.** The budget types, `IAgentChannel` and its records, `AutonomousProcessStrategy` and `SpawnAgentTool` carry `[Experimental("ORKEXP002")]`: code that uses them must acknowledge the diagnostic (see [experimental APIs](../reference/experimental-apis.md)). Running a YAML crew with `process: autonomous` needs nothing.

Unlike the Sequential mode, where the declared order and the task's `agent:` decide, the Autonomous mode lets the manager LLM decide who does what, and gives a failed task a second chance with a peer. The orchestrator enforces the budget and collects the results.

### Positioning relative to the other strategies

| Strategy | Routing decision | Delegation | Dynamic spawn | Multi-dimensional budget | A2A communication |
|-----------|--------------------|-----------------------|-----------------|------------------------|--------------------|
| Sequential | Declared agent, else selector | Via `delegate_work_to_coworker` (agents with `allowDelegation`) | No | No | No |
| Hierarchical | Manager LLM | No (manager review instead) | No | No | No |
| Graph | Declared agent, else selector | Via `delegate_work_to_coworker` | No | No (circuit breaker) | No |
| **Autonomous** | **Manager LLM** | **On failure, to a peer (bounded depth)** | **Via `SpawnAgentTool` (host-registered)** | **Yes (5 dimensions)** | **Request/Response** |

## Architecture

### Domain layer — Execution budget

| Class | File | Role |
|--------|---------|------|
| `AgentExecutionBudget` | `Autonomous/AgentExecutionBudget.cs` | Multi-dimensional budget: tool calls, delegation depth, wall time, tokens, spawns |
| `BudgetSnapshot` | `Autonomous/AgentExecutionBudget.cs` | Immutable snapshot for logging and telemetry |
| `BudgetExhaustedException` | `Autonomous/AgentExecutionBudget.cs` | Typed exception with `BudgetDimension` (ToolCalls, DelegationDepth, WallTime, Tokens, SpawnedAgents) |
| `BudgetDimension` | `Autonomous/AgentExecutionBudget.cs` | Enum of the 5 budget dimensions |

The limits are `init`-only; the counters are thread-safe (`Interlocked`). Each `Record*` call (`RecordToolCall`, `RecordDelegation`, `RecordSpawn`, `RecordTokens(n)`) checks the wall time, increments its counter and throws `BudgetExhaustedException` when the counter goes past its limit. `ThrowIfExhausted()` is the pre-flight check that consumes nothing, `AssertWallTime()` checks the clock alone, and `IsExhausted` reports whether any dimension is spent. The clock is injectable (`TimeProvider`) for tests.

### Domain layer — ProcessType

`ProcessType.Autonomous` is a member of the value object, and `IProcessStrategy` has a dedicated entry point:

```csharp
Task<CrewOutput> ExecuteAutonomousAsync(
    Crew crew,
    AgentExecutionBudget budget,
    IReadOnlyDictionary<string, string>? inputVariables = null,
    CancellationToken cancellationToken = default);
```

### Application layer — A2A communication

| Class | File | Role |
|--------|---------|------|
| `IAgentChannel` | `Interfaces/Services/IAgentChannel.cs` | Bidirectional request/response channel between agents |
| `AgentChannelRequest` | `Interfaces/Services/IAgentChannel.cs` | Request with correlation ID, intent, payload, optional metadata (`Create(from, to, intent, payload)`) |
| `AgentChannelResponse` | `Interfaces/Services/IAgentChannel.cs` | Correlated response (`Ok(...)` / `Fail(...)`) with success/error |
| `NullMemoryScope` | `Context/NullMemoryScope.cs` | No-op singleton for contexts without memory |

`IAgentChannel` supports three operations:

- **RequestAsync**: request/response with a timeout (30 s by default); a target that does not answer in time raises `TimeoutException`, a target with no registered handler gets a failed response
- **RegisterHandler**: per-agent handler registration (returns an `IDisposable` that unregisters it)
- **BroadcastAsync**: notification to the other agents of a crew, no response expected; `InMemoryAgentChannel` reaches the agents registered with `RegisterCrewMember(crewId, agentId)`

### Infrastructure layer — Autonomous strategy

| Class | File | Role |
|--------|---------|------|
| `AutonomousProcessStrategy` | `Crew/Strategies/AutonomousProcessStrategy.cs` | Implements `IProcessStrategy.ExecuteAutonomousAsync` |
| `InMemoryAgentChannel` | `Communication/InMemoryAgentChannel.cs` | In-process implementation of the A2A channel (lock-free, `ConcurrentDictionary`), registered as the scoped `IAgentChannel` |
| `SpawnAgentTool` | `Tools/SpawnAgentTool.cs` | Tool that lets an agent spawn a sub-agent |
| `DelegateWorkTool` | `Tools/DelegateWorkTool.cs` | Takes an optional `AgentExecutionBudget` and calls `RecordDelegation()` before each delegation |

### Wiring

| Place | Role |
|--------|-------------|
| `ProcessStrategyFactory` | `"Autonomous"` → `AutonomousProcessStrategy` |
| `SequentialCrewOrchestrator` | Dispatches `"Autonomous"` to `ExecuteAutonomousAsync` with `AgentExecutionBudget.Permissive` |
| `AddOrkeonInfrastructure()` | Registers `AutonomousProcessStrategy` and `IAgentChannel` → `InMemoryAgentChannel` (scoped) |

## Execution flow

```
                    ┌──────────────────────────────────────────────────┐
                    │         AutonomousProcessStrategy                │
                    │                                                  │
  crew tasks ──►    │  register every agent on the IAgentChannel       │
  (dependency       │  for each task:                                  │
   order)           │    0. budget.AssertWallTime()                    │
                    │    1. manager.AssignTaskAsync (LLM) → agent      │
                    │    2. budget.RecordToolCall()                    │
                    │    3. ExecuteTaskAsync(agent, task)              │
                    │    4. failed + AllowDelegation + depth + a peer: │
                    │       ├─ budget.RecordDelegation()               │
                    │       ├─ channel.RequestAsync("delegate",        │
                    │       │    first other agent, timeout 2 min)     │
                    │       └─ peer executes under a child budget      │
                    │    5. BudgetExhausted in the task → partial      │
                    │       output "[BUDGET EXHAUSTED] …", stop        │
                    │  BudgetExhausted between tasks → stop the loop   │
                    └──────────────────────────────────────────────────┘
```

Details that matter when sizing a run:

- **Assignment** goes through `IManagerAgent` (`LlmBasedManager`), which calls the host's registered LLM, as in the Hierarchical mode; a task's `agent:` is not consulted. An LLM error falls back to the first agent.
- **Tool calls**: the strategy records **one** `RecordToolCall()` per task handed out (the assignment); the agent's own tool calls inside its loop are not counted against the budget. `MaxToolCalls` therefore bounds the number of tasks attempted.
- **Delegation depth**: `RecordDelegation()` increments a crew-wide counter that is never decremented, so `MaxDelegationDepth` behaves as the number of delegations allowed in the run. The delegate is the **first other agent** in the crew's order; it takes the task itself over, in the context of the attempt that failed, derived from it (GAP-21): the crew's id and memory scope, the run's input variables and the outputs so far, plus `delegation_context` and `autonomous_child_budget_snapshot`. It recalls the crew's memory like any execution that answers a task, its output — when it succeeds — is the task's result, stored once under the peer with `memory: true`, and it does not delegate further. The agent that failed fails the task (`AgentFailedTaskEvent`), the task is assigned to the peer, and completes under it ([Events](../architecture/domain-events.md)).
- **Tokens**: the tokens of a delegated execution are recorded on both the child and the parent budget; the direct executions feed the crew's token telemetry but not `MaxTokensConsumed`.
- **Outcome**: the crew output concatenates the task outputs. A task that failed — directly, with no peer to delegate to, or delegated to a peer who failed too — **fails the crew**, and the tasks that depend on it are skipped without being claimed. An exhausted budget fails the crew too, its error opening with `Execution budget exhausted: <dimension>` and naming every task it never reached; `ICrewExecutionHook` receives `Failed` with that reason (`Canceled` is kept for an actual cancellation). `orkeon run` exits 2 in both cases.

## Multi-dimensional budget

The budget controls 5 independent dimensions. Each dimension has a thread-safe counter and a limit. Exhausting any dimension throws `BudgetExhaustedException`.

| Dimension | Default | Strict | Permissive | Description |
|-----------|--------|--------|------------|-------------|
| MaxToolCalls | 15 | 8 | 50 | Maximum number of tool calls |
| MaxDelegationDepth | 2 | 1 | 4 | Maximum delegation depth (A→B→C = 2) |
| MaxWallTime | 5 min | 2 min | 15 min | Maximum wall-clock time |
| MaxTokensConsumed | 16,000 | 8,000 | 64,000 | Total tokens (prompt + completion) |
| MaxSpawnedAgents | 3 | 1 | 10 | Maximum number of spawned sub-agents |

### Presets

```csharp
#pragma warning disable ORKEXP002
// Production: conservative limits
var strict = AgentExecutionBudget.Strict;

// Development: loose limits — what ICrewOrchestrationService always uses
var permissive = AgentExecutionBudget.Permissive;

// Custom
var custom = new AgentExecutionBudget
{
    MaxToolCalls = 20,
    MaxDelegationDepth = 3,
    MaxWallTime = TimeSpan.FromMinutes(10),
    MaxTokensConsumed = 32_000,
    MaxSpawnedAgents = 5
};

// A budget other than Permissive: call the strategy directly
var strategy = serviceProvider.GetRequiredService<AutonomousProcessStrategy>();
var output = await strategy.ExecuteAutonomousAsync(crew, custom, inputVariables: null, ct);
#pragma warning restore ORKEXP002
```

`KickoffAsync` does not take a budget: a crew run through `ICrewOrchestrationService` (the runner, the host, Studio) always gets `Permissive`. Calling `ExecuteAutonomousAsync` directly skips what the orchestrator adds around a run (planning, checkpointing, the crew's state transitions).

### Child budgets

When the strategy delegates, or `SpawnAgentTool` spawns, the sub-agent receives a derived child budget:

```csharp
var childBudget = parentBudget.CreateChildBudget();
// MaxToolCalls       = max(1, parent.Max - parent.Current)
// MaxDelegationDepth = max(0, parent.Max - parent.Current - 1)
// MaxWallTime        = parent.MaxWallTime - parent.Elapsed
// MaxTokensConsumed  = max(100, parent.Max - parent.Current)
// MaxSpawnedAgents   = max(0, parent.Max - parent.Current)
```

A child budget is an independent copy of the parent's **remaining** allowance, with its own counters: two children derived one after the other each receive the full remainder. The parent's cap is held where the consumption is recorded on it too — the tokens of a delegated execution, and each spawn.

## A2A communication (IAgentChannel)

The bidirectional channel lets agents communicate in request/response mode:

```csharp
// Agent A asks Agent B to clarify
var request = AgentChannelRequest.Create(
    from: agentA.Id,
    to: agentB.Id,
    intent: "clarify",
    payload: "Which data format for the report?");

var response = await channel.RequestAsync(request, timeout: TimeSpan.FromSeconds(30));

if (response.Success)
    Console.WriteLine($"Response: {response.Payload}");
```

### Intents

The intent is a free string. Inside an autonomous run, the handler each agent registers understands:

| Intent | Handling |
|--------|-------------|
| `delegate` | Executes the payload as a task under a child budget and returns its output (or `Budget exhausted: …`) |
| anything else (`clarify`, …) | Acknowledged: `Agent <role> acknowledges: <intent>` |
| `broadcast` | The intent `BroadcastAsync` stamps on its notifications |

The `InMemoryAgentChannel` implementation is in-process and lock-free. For a multi-host deployment, implement `IAgentChannel` with Redis Streams or a message broker.

## SpawnAgentTool — Agent self-spawn

A tool (`spawn_agent`) that lets an agent create a specialized sub-agent on the fly.
The class ships in `Orkeon.Infrastructure` but **neither the strategy nor any shipped
composition root provides it**: its constructor takes the parent agent's id, the crew id,
the `AgentExecutionBudget` it enforces and an `IAgentFactory` (plus, for a synchronous
spawn, an `IAgentExecutionService`), so a host that wants self-spawn builds one per agent
and adds it to that agent — see the [tool inventory](../tools/inventory.md). The budget it
enforces is the one the host hands it, not the run's budget.

```csharp
// The agent's LLM generates this tool call:
{
    "tool": "spawn_agent",
    "parameters": {
        "role": "data_analyst",
        "goal": "Analyze Q4 sales trends",
        "task": "Produce a CSV report of sales by region",
        "wait_for_result": true,
        "allow_delegation": false
    }
}
```

`role` and `goal` are required; `backstory` is optional. Each spawn calls `RecordSpawn()` on the budget (a spent budget refuses the call). The spawned agent is created with `MaxIterations = 5` and a child budget (passed in the spawn request metadata and in the execution context). With `wait_for_result: true` (the default) the tool executes the task and returns its output; `false` only creates the agent.

## Observability

### Output metadata

The `CrewOutput` in Autonomous mode includes budget metadata, next to the token telemetry:

```json
{
    "process_type": "autonomous",
    "agent_count": 3,
    "budget_tool_calls": "12/50",
    "budget_delegation_depth": "1/4",
    "budget_tokens": "9200/64000",
    "budget_spawned": "0/10",
    "budget_exhausted": false
}
```

### BudgetSnapshot

`budget.ToSnapshot()` returns an immutable `BudgetSnapshot` at any time (`ToolCalls`/`MaxToolCalls`, `DelegationDepth`/`MaxDelegationDepth`, `TokensConsumed`/`MaxTokensConsumed`, `SpawnedAgents`/`MaxSpawnedAgents`, `Elapsed`/`MaxWallTime`, `IsExhausted`), loggable and serializable.

### Structured logging

All key events are logged via `LoggerMessage`:

- `Starting autonomous execution for crew {CrewId} (budget: {MaxToolCalls} tool calls, depth {MaxDepth})`
- `Agent {AgentId} claimed task {TaskId}: {Reason}`
- `Delegation: {From} → {To} for task {TaskId} (depth: {Depth})`
- `Task {TaskId} completed (success: {Success}, budget: {Snapshot})`
- `Budget exhausted for crew {CrewId}: dimension={Dimension}, {Message}`
- `Autonomous execution completed for crew {CrewId} in {Duration} (tool calls: {ToolCalls}, delegation depth: {Depth})`
- `SpawnAgentTool` logs each spawn (parent, child id, role) and the completion of a synchronous spawn

Like every mode, the strategy also reports through `ICrewExecutionHook`: the task start when an agent claims it, the task completions and the crew outcome when the run settles.

## YAML configuration

```yaml
# Flat root — no crew: wrapper; agents: is a mapping keyed by agent id.
name: research-team
process: autonomous        # ← enables the autonomous mode
goal: "Produce a complete research report"

agents:
  researcher:
    role: Researcher
    goal: "Find reliable sources"
    allowDelegation: true   # a failed task of this agent is handed to a peer
    tools: [web_search]

  analyst:
    role: Analyst
    goal: "Analyze and synthesize the data"
    allowDelegation: true
    tools: [json_tool, csv_reader]

  writer:
    role: Writer
    goal: "Write the final report"
    allowDelegation: false
```

`spawn_agent` is not listed: the YAML loader resolves tool names against the tool registry, where no `SpawnAgentTool` is registered by default (an unknown name is skipped with a warning, or fails the load in strict-tools mode).

> **Note**: there is no `autonomousBudget` YAML key — the loader does not parse one. In YAML crews the Autonomous mode always runs with `AgentExecutionBudget.Permissive` (50 tool calls, depth 4, 15 min, 64 000 tokens, 10 spawns). In the scripting DSL, `crewBuilder().budget({...})` bounds only the procedural shape (`await crew.run()`); the declarative shape ignores it with a warning.

> **Delegation defaults**: the autonomous strategy delegates through the `IAgentChannel`, not through `ITaskDelegator` — the stub that denies every request (see [Default behaviors](../getting-started/default-behaviors.md)) is not consulted by this mode.

## Complementarity with the other modes

| Need | Recommended mode |
|--------|----------------|
| Linear pipeline, maximum determinism | Sequential |
| Centralized manager, quality review | Hierarchical |
| Independent tasks, parallelism | Parallel |
| Retry of flaky tasks, bounded run | Graph |
| **LLM-driven assignment, second chance with a peer, budget-bounded run** | **Autonomous** |

The Autonomous mode is the least deterministic. For sensitive production workloads, prefer Sequential or Hierarchical and reserve Autonomous for cases where letting the manager distribute the work brings more value than the cost of non-determinism (exploratory research, creative writing, complex multi-domain problem solving).

## Tests

| Test file | Coverage |
|-----------------|-----------|
| `Orkeon.Infrastructure.Tests/Strategies/CovAutonomous_AutonomousProcessStrategyTests.cs` | Entry points, assignment fallback, delegation over the channel, tool-call and wall-time exhaustion, token telemetry, channel handlers |
| `Orkeon.Domain.Tests/Autonomous/AgentExecutionBudgetTimeProviderTests.cs` | Wall-time budget with a controllable clock |
| `Orkeon.Infrastructure.Tests/Communication/CovStubs_InMemoryAgentChannelTests.cs` | Request/response, timeout, missing handler, broadcast |
| `Orkeon.Infrastructure.Tests/Tools/SpawnAgentTool/SpawnAgentToolTests.cs` | Spawn recorded on the parent budget, child budget propagation (metadata, context variables), token accounting |
| `Orkeon.Infrastructure.Tests/Orchestration/CovAutonomous_SequentialCrewOrchestratorTests.cs` | Dispatch of the six modes (Autonomous with the Permissive budget) |

---

> **See also**: [ProcessTypes comparison guide](./process-types.md) · [Graph orchestration](./graph.md) · [YAML schema](../architecture/yaml-schema.md) · [Back to index](../INDEX.md)
