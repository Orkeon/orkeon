> 🇫🇷 [Version française](../fr/orchestration/autonomous.md)

> **See also**: [ProcessTypes comparison guide](./process-types.md) · [YAML schema](../architecture/yaml-schema.md) · [Back to index](../INDEX.md)

# Autonomous agentic orchestration

## Overview

Orkeon provides an autonomous orchestration mode (`ProcessType.Autonomous`) in which agents self-organize to claim tasks, delegate recursively to their peers, and spawn specialized sub-agents on the fly. All execution is constrained by a multi-dimensional `AgentExecutionBudget` that guarantees termination.

> **DI default worth knowing**: out of the box, `ITaskDelegator` is a stub that denies
> every delegation request (it warns once, with the fix) — see
> [Default behaviors](../getting-started/default-behaviors.md) before wiring a
> delegation-heavy crew.

Unlike the Sequential/Hierarchical modes where the orchestrator controls the flow, and the Graph mode where the state graph defines the topology, the Autonomous mode lets the agents make the delegation and spawn decisions. The orchestrator only steps in to enforce the budget and collect the results.

### Positioning relative to the other strategies

| Strategy | Routing decision | Recursive delegation | Dynamic spawn | Multi-dimensional budget | A2A communication |
|-----------|--------------------|-----------------------|-----------------|------------------------|--------------------|
| Sequential | Fixed (list order) | No | No | No | No |
| Hierarchical | Manager LLM | 1 level | No | No | Unidirectional |
| Graph | Conditional edges | No | No | No (circuit breaker) | No |
| **Autonomous** | **Agent self-selection** | **Yes (controlled depth)** | **Yes (quota)** | **Yes (5 dimensions)** | **Request/Response** |

## Architecture

### Domain layer — Execution budget

| Class | File | Role |
|--------|---------|------|
| `AgentExecutionBudget` | `Autonomous/AgentExecutionBudget.cs` | Multi-dimensional budget: tool calls, delegation depth, wall time, tokens, spawns |
| `BudgetSnapshot` | `Autonomous/AgentExecutionBudget.cs` | Immutable snapshot for logging and telemetry |
| `BudgetExhaustedException` | `Autonomous/AgentExecutionBudget.cs` | Typed exception with `BudgetDimension` (ToolCalls, DelegationDepth, WallTime, Tokens, SpawnedAgents) |
| `BudgetDimension` | `Autonomous/AgentExecutionBudget.cs` | Enum of the 5 budget dimensions |

The budget is thread-safe (`Interlocked` counters) and immutable after construction (limits as `init`). Each action (`RecordToolCall`, `RecordDelegation`, `RecordSpawn`, `RecordTokens`) decrements the budget and throws `BudgetExhaustedException` when the limit is reached.

### Domain layer — ProcessType

`ProcessType.Autonomous` is added to the existing value object. `IProcessStrategy` exposes a new method:

```csharp
Task<CrewOutput> ExecuteAutonomousAsync(
    Crew crew,
    AgentExecutionBudget budget,
    IReadOnlyDictionary<string, string>? inputVariables = null);
```

### Application layer — A2A communication

| Class | File | Role |
|--------|---------|------|
| `IAgentChannel` | `Interfaces/Services/IAgentChannel.cs` | Bidirectional request/response channel between agents |
| `AgentChannelRequest` | `Interfaces/Services/IAgentChannel.cs` | Request with correlation ID, intent, payload |
| `AgentChannelResponse` | `Interfaces/Services/IAgentChannel.cs` | Correlated response with success/error |
| `NullMemoryScope` | `Context/NullMemoryScope.cs` | No-op singleton for contexts without memory |

`IAgentChannel` supports three modes:

- **RequestAsync**: synchronous request/response with a configurable timeout
- **RegisterHandler**: per-agent handler registration (returns `IDisposable`)
- **BroadcastAsync**: notification to all agents of a crew (fire-and-forget)

### Infrastructure layer — Autonomous strategy

| Class | File | Role |
|--------|---------|------|
| `AutonomousProcessStrategy` | `Crew/Strategies/AutonomousProcessStrategy.cs` | Implements `IProcessStrategy.ExecuteAutonomousAsync` |
| `InMemoryAgentChannel` | `Communication/InMemoryAgentChannel.cs` | In-process implementation of the A2A channel (lock-free, `ConcurrentDictionary`) |
| `SpawnAgentTool` | `Tools/SpawnAgentTool.cs` | Tool that lets agents spawn sub-agents |

### Infrastructure layer — Changes to existing code

| Class | Change |
|--------|-------------|
| `ProcessStrategyFactory` | Added the `"Autonomous"` case → `AutonomousProcessStrategy` |
| `SequentialCrewOrchestrator` | Added the `"Autonomous"` dispatch with `AgentExecutionBudget.Default` |
| `DelegateWorkTool` | Added the optional `AgentExecutionBudget?` parameter, `RecordDelegation()` call before each delegation |

## Execution flow

```
                    ┌────────────────────────────────────────────┐
                    │         AutonomousProcessStrategy          │
                    │                                            │
  Crew.Tasks ──►    │  for each task:                            │
                    │    1. AssignTaskAsync (LLM-based)           │
                    │    2. budget.RecordToolCall()               │
                    │    3. ExecuteTaskAsync(agent, task)         │
                    │    4. On failure + AllowDelegation:         │
                    │       ├─ budget.RecordDelegation()          │
                    │       ├─ channel.RequestAsync(peer, task)   │
                    │       └─ peer executes with childBudget     │
                    │    5. BudgetExhausted? → partial output     │
                    │                                            │
                    └────────────────────────────────────────────┘

  SpawnAgentTool (optional, injected into the agent):
    1. budget.RecordSpawn()
    2. IAgentFactory.CreateAgentAsync(spawnRequest)
    3. ExecuteTaskAsync(spawnedAgent, task) with childBudget
```

## Multi-dimensional budget

The budget controls 5 independent dimensions. Each dimension has a thread-safe counter and a limit. Exhausting any dimension throws `BudgetExhaustedException`.

| Dimension | Default | Strict | Permissive | Description |
|-----------|--------|--------|------------|-------------|
| MaxToolCalls | 15 | 8 | 50 | Maximum number of tool calls |
| MaxDelegationDepth | 2 | 1 | 4 | Maximum recursive delegation depth (A→B→C = 2) |
| MaxWallTime | 5 min | 2 min | 15 min | Maximum wall-clock time |
| MaxTokensConsumed | 16,000 | 8,000 | 64,000 | Total tokens (prompt + completion) |
| MaxSpawnedAgents | 3 | 1 | 10 | Maximum number of spawned sub-agents |

### Presets

```csharp
// Production : limites conservatrices
var budget = AgentExecutionBudget.Strict;

// Développement : limites larges
var budget = AgentExecutionBudget.Permissive;

// Custom
var budget = new AgentExecutionBudget
{
    MaxToolCalls = 20,
    MaxDelegationDepth = 3,
    MaxWallTime = TimeSpan.FromMinutes(10),
    MaxTokensConsumed = 32_000,
    MaxSpawnedAgents = 5
};
```

### Child budgets

When an agent delegates or spawns, the sub-agent receives a derived child budget with the remaining quotas:

```csharp
var childBudget = parentBudget.CreateChildBudget();
// MaxToolCalls = parent.Max - parent.Current
// MaxDelegationDepth = parent.Max - parent.Current - 1
// MaxWallTime = parent.Max - parent.Elapsed
// etc.
```

This guarantees that the sum of the children's consumption never exceeds the parent budget.

## A2A communication (IAgentChannel)

The bidirectional channel lets agents communicate in request/response mode:

```csharp
// Agent A demande a Agent B de clarifier
var request = AgentChannelRequest.Create(
    from: agentA.Id,
    to: agentB.Id,
    intent: "clarify",
    payload: "Quel format de données pour le rapport ?");

var response = await channel.RequestAsync(request, timeout: TimeSpan.FromSeconds(30));

if (response.Success)
    Console.WriteLine($"Réponse: {response.Payload}");
```

### Standard intents

| Intent | Description |
|--------|-------------|
| `delegate` | Work delegation (processed by the target's handler) |
| `clarify` | Request for information or clarification |
| `broadcast` | Notification to all agents of the crew |

The `InMemoryAgentChannel` implementation is in-process and lock-free. For a multi-host deployment, implement `IAgentChannel` with Redis Streams or a message broker.

## SpawnAgentTool — Agent self-spawn

Tool injected into autonomous agents to create specialized sub-agents on the fly:

```csharp
// Le LLM de l'agent génère cet appel d'outil :
{
    "tool": "spawn_agent",
    "parameters": {
        "role": "data_analyst",
        "goal": "Analyser les tendances de ventes Q4",
        "task": "Produire un rapport CSV des ventes par region",
        "wait_for_result": true,
        "allow_delegation": false
    }
}
```

Each spawn is controlled by the budget (`RecordSpawn`). The spawned agent receives a child budget with reduced limits (5 iterations max, remaining quotas).

## Observability

### Output metadata

The `CrewOutput` in Autonomous mode includes budget metadata:

```json
{
    "process_type": "autonomous",
    "agent_count": 3,
    "budget_tool_calls": "12/15",
    "budget_delegation_depth": "1/2",
    "budget_tokens": "9200/16000",
    "budget_spawned": "1/3",
    "budget_exhausted": false
}
```

### BudgetSnapshot

`budget.ToSnapshot()` returns an immutable `BudgetSnapshot` at any time, loggable and serializable.

### Structured logging

All key events are logged via `LoggerMessage`:

- `Starting autonomous execution for crew {CrewId} (budget: {MaxToolCalls} tool calls, depth {MaxDepth})`
- `Agent {AgentId} claimed task {TaskId}: {Reason}`
- `Delegation: {From} -> {To} for task {TaskId} (depth: {Depth})`
- `Budget exhausted for crew {CrewId}: dimension={Dimension}, {Message}`
- `Agent {ParentId} spawned sub-agent {ChildId} (role: {Role})`

## YAML configuration

```yaml
crew:
  name: research-team
  process: autonomous      # ← active le mode autonome
  goal: "Produire un rapport de recherche complet"
  
  autonomousBudget:        # ← optionnel, défauts si absent
    maxToolCalls: 20
    maxDelegationDepth: 2
    maxWallTime: "00:10:00"
    maxTokensConsumed: 32000
    maxSpawnedAgents: 3
    preset: default        # ou "strict" / "permissive"

  agents:
    - role: researcher
      goal: "Trouver des sources fiables"
      allowDelegation: true
      tools: [web_search, spawn_agent]  # ← spawn_agent pour self-spawn

    - role: analyst
      goal: "Analyser et synthétiser les données"
      allowDelegation: true
      tools: [json_search, csv_search]

    - role: writer
      goal: "Rédiger le rapport final"
      allowDelegation: false
```

> **Note**: YAML parsing of `autonomousBudget` is not implemented yet. The Autonomous mode uses `AgentExecutionBudget.Default` for now. Injecting a custom budget from YAML is planned for v1.1.

## Complementarity with the other modes

| Need | Recommended mode |
|--------|----------------|
| Linear pipeline, maximum determinism | Sequential |
| Centralized manager, quality review | Hierarchical |
| Independent tasks, parallelism | Parallel |
| Refinement loops, conditional retry | Graph |
| **Self-organizing agents, recursive delegation, dynamic spawn** | **Autonomous** |

The Autonomous mode is the most expressive but also the least deterministic. For sensitive production workloads, prefer Sequential or Hierarchical and reserve Autonomous for cases where agent autonomy brings more value than the cost of non-determinism (exploratory research, creative writing, complex multi-domain problem solving).

---

> **See also**: [ProcessTypes comparison guide](./process-types.md) · [Graph orchestration](./graph.md) · [YAML schema](../architecture/yaml-schema.md) · [Back to index](../INDEX.md)
