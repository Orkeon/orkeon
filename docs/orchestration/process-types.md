> 🇫🇷 [Version française](../fr/orchestration/process-types.md)

# ProcessTypes comparison guide

> **Prerequisites**: [Overview](../getting-started/overview.md) and [YAML and Builders](../getting-started/yaml-and-builders.md)

---

## Overview

Orkeon offers **6 orchestration strategies** via the `ProcessType` value object (Domain layer). Each strategy defines how agents coordinate to execute a Crew's tasks. The choice of ProcessType is the architectural lever with the biggest impact on the behavior of a multi-agent system.

```csharp
// Orkeon.Domain.SharedKernel.ValueObjects.ProcessType (sealed record)
ProcessType.Sequential    // Linear pipeline
ProcessType.Hierarchical  // Manager + workers
ProcessType.Parallel      // Concurrent execution
ProcessType.Consensual    // Voting and consensus
ProcessType.Graph         // State graph with controlled cycles
ProcessType.Autonomous    // Self-organization with budget
```

### Implementation architecture

```
ProcessType (Domain — Value Object)
     │
     ▼
IProcessStrategy (Domain — Interface)
     │
     ▼
ProcessStrategyFactory (Infrastructure)
     │
     ├── SequentialProcessStrategy
     ├── HierarchicalProcessStrategy
     ├── ParallelProcessStrategy
     ├── ConsensualProcessStrategy      ← IConsensualProcessStrategy
     ├── GraphProcessStrategy
     └── AutonomousProcessStrategy
```

The `ProcessStrategyFactory` resolves the appropriate strategy via a switch on `ProcessType.Value`, injected through DI.

---

## Quick comparison matrix

| Criterion | Sequential | Hierarchical | Parallel | Consensual | Graph | Autonomous |
|---------|-----------|-------------|---------|-----------|-------|-----------|
| **Execution model** | Linear | Linear + review | Concurrent | Parallel + vote | State machine | Self-organized |
| **Coordination** | Round-robin | Manager LLM | Round-robin | LLM consensus | Round-robin + routing | LLM + A2A channel |
| **Task dependencies** | Yes (chained) | Yes (via manager) | No | No | Yes (edges) | Yes (delegation) |
| **Circuit breaker** | — | — | — | — | ✅ 4 mechanisms | — |
| **Execution budget** | — | — | — | — | — | ✅ 5 dimensions |
| **Automatic retry** | — | Revisions (max 3) | — | Voting rounds | ✅ configurable | Via delegation |
| **Dynamic spawn** | — | — | — | — | — | ✅ SpawnAgentTool |
| **Complexity** | ⭐ | ⭐⭐ | ⭐ | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Relative LLM cost** | Low | Medium | Low | High | Medium | Variable |
| **Main use case** | ETL pipelines | QA, code review | Independent tasks | Critical decisions | Complex workflows | Exploration, R&D |

---

## 1. Sequential — Linear pipeline

### Principle

Tasks execute **one by one, in the order** defined by the `ExecutionPlan`. Each task receives the results of the previous tasks as context. Agent assignment is round-robin (unless explicitly assigned via `task.AssignedAgent`).

### Internal mechanism

```
Task 1 → Agent A → output₁
                      ↓ (enriched context)
Task 2 → Agent B → output₂
                      ↓
Task 3 → Agent C → output₃ → final result
```

**Key classes**: `SequentialProcessStrategy`, `ExecutionPlan`, `AgentDelegationToolsProvider`

### YAML configuration

```yaml
crew:
  process: sequential
  tasks:
    - description: "Collect the data"
      expected_output: "Raw data"
    - description: "Analyze the data"
      expected_output: "Analysis report"
    - description: "Generate the recommendations"
      expected_output: "Action plan"
```

### Fluent Builder configuration

```csharp
var crew = new CrewBuilder()
    .Sequential()
    .WithAgent(a => a.Role("Collector").Goal("Gather data"))
    .WithAgent(a => a.Role("Analyst").Goal("Analyze data"))
    .WithTask(t => t.Description("Collect").ExpectedOutput("Raw data"))
    .WithTask(t => t.Description("Analyze").ExpectedOutput("Report"))
    .Build();
```

### Advantages

- **Maximum simplicity**: no complex configuration, predictable behavior
- **Traceability**: each step is clearly identifiable in the logs
- **Cumulative context**: each task benefits from the previous results
- **Determinism**: same input → same execution path

### Drawbacks

- **No parallelism**: the total time is the sum of all the tasks
- **Single point of failure**: one failure blocks the whole chain
- **No retry**: no automatic recovery on error
- **Rigid**: the order is fixed, no conditional branching

### When to use it

- ETL pipelines (extract → transform → load)
- Sequential writing (research → drafting → proofreading)
- Workflows where each step strictly depends on the previous one
- Rapid prototyping and POCs

### When not to use it

- Independent tasks that could run in parallel
- Workflows requiring conditional branches
- Scenarios demanding resilience (retry, fallback)

---

## 2. Hierarchical — Manager + Workers

### Principle

A **manager agent** (LLM-driven) coordinates a team of workers. For each task, the manager selects the most appropriate agent via `AssignTaskAsync()`, then **reviews the output** and can request up to 3 revisions.

### Internal mechanism

```
                  ┌─── Manager Agent ───┐
                  │   AssignTask()      │
                  │   ReviewOutput()    │
                  └────────┬────────────┘
                           │
            ┌──────────────┼──────────────┐
            ▼              ▼              ▼
        Agent A        Agent B        Agent C
        (chosen)       (waiting)      (waiting)
            │
            ▼
        Output → Review → OK? → yes → next task
                           → no → revision (max 3)
```

**Key classes**: `HierarchicalProcessStrategy`, `IManagerAgent`, `LlmBasedManager`, `TaskAssignment`

**`IManagerAgent` interface**:
- `AssignTaskAsync(task, agents, context)` → `TaskAssignment` (selected agent + justification)
- `ReviewOutputAsync(output, task)` → `bool` (approved or not)

### YAML configuration

```yaml
crew:
  process: hierarchical
  manager_llm:
    provider: openai
    model: gpt-4o
  agents:
    - role: "Senior Developer"
      goal: "Write production code"
    - role: "QA Engineer"
      goal: "Test and validate"
    - role: "Tech Writer"
      goal: "Document the code"
```

### Advantages

- **Smart allocation**: the manager picks the agent best suited to each task
- **Built-in quality control**: automatic review loop
- **Flexibility**: the manager can adapt the strategy during execution
- **Traceability**: every assignment is justified

### Drawbacks

- **Extra LLM cost**: the manager consumes tokens for every decision + review
- **Bottleneck**: everything goes through the manager (no parallelism)
- **Limited revisions**: max 3 revisions (hardcoded), no structural retry
- **Dependence on manager quality**: a bad manager prompt degrades the whole workflow

### When to use it

- Code review (the manager assigns the most competent reviewer)
- Projects with heterogeneous specialists (dev, QA, design, writing)
- Workflows requiring simulated human validation
- Situations where quality matters more than speed

### When not to use it

- Homogeneous tasks (round-robin is enough)
- Strict LLM cost constraints
- High-frequency workflows (the manager is a bottleneck)

---

## 3. Parallel — Concurrent execution

### Principle

All tasks execute **simultaneously** via `Task.WhenAll()`. Each task has an independent execution context (no results shared between tasks). The results are aggregated at the end.

### Internal mechanism

```
        ┌── Task 1 → Agent A → output₁ ──┐
        │                                  │
Start ──┼── Task 2 → Agent B → output₂ ──┼── Aggregation → Result
        │                                  │
        └── Task 3 → Agent C → output₃ ──┘
```

**Key classes**: `ParallelProcessStrategy`

### YAML configuration

```yaml
crew:
  process: parallel
  tasks:
    - description: "Analyze the French market"
      expected_output: "France report"
    - description: "Analyze the German market"
      expected_output: "Germany report"
    - description: "Analyze the Spanish market"
      expected_output: "Spain report"
```

### Advantages

- **Maximum speed**: total time = duration of the longest task
- **Simplicity**: no complex coordination
- **Scalability**: adding tasks has no impact on the total time
- **Isolation**: one task's failure does not impact the others

### Drawbacks

- **No dependencies**: impossible to chain results between tasks
- **API consumption spikes**: all LLM requests fire at the same time (rate limiting)
- **Basic aggregation**: results are simply concatenated
- **No retry**: no automatic recovery

### When to use it

- Independent multi-market or multi-source analyses
- Batch content generation (one article per market, per language)
- Parallel classification tasks
- Any scenario where the tasks have no mutual dependency

### When not to use it

- Tasks with dependencies (use Sequential or Graph)
- APIs with strict rate limiting (simultaneous calls may be throttled)
- Scenarios requiring progressive synthesis

---

## 4. Consensual — Voting and consensus

### Principle

For each task, **all agents execute it independently**, then a vote determines the best result. If no consensus is reached, **discussion rounds** let agents reconsider their position after seeing the others' results. A fallback mechanism settles the matter as a last resort.

### Internal mechanism

```
Task N ──┬── Agent A → result A ────┐
         ├── Agent B → result B ────┼── Vote ── Consensus? ── yes → Accepted
         └── Agent C → result C ────┘              │
                                                   no
                                                    ↓
                                          Discussion (round 2)
                                          Agents see the others' results
                                                    ↓
                                                  Re-vote
                                                    ↓
                                          Failure → Fallback
```

**Key classes**: `ConsensualProcessStrategy`, `IVotingStrategy`, `IVotingStrategyFactory`, `Vote`, `VoteResult`, `VotingOptions`, `ConsensualProcessOptions`

### Available consensus types

The voting mechanism is **selectable via configuration**: the value of
`Orkeon:Consensus:VotingOptions:ConsensusType` (.NET `appsettings.json` section)
picks the dedicated strategy via `IVotingStrategyFactory`. Without configuration,
the default remains `Majority` (backward compatible).

| Type | Resolved strategy | Description | Default threshold |
|------|-------------------|-------------|------------------|
| `Majority` | `MajorityVotingStrategy` | More than 50% of the votes | 50% |
| `SuperMajority` | `SuperMajorityVotingStrategy` | Configurable threshold (default 2/3) | 66.7% |
| `Unanimity` | `UnanimityVotingStrategy` | All agents must agree | 100% |
| `WeightedConsensus` | `WeightedConsensusStrategy` | Votes weighted by role (`RoleWeights`) | Configurable |
| `BordaCount` | `BordaCountStrategy` | Borda score ranking | N/A |

### Configuration (.NET, `Orkeon:Consensus` section)

```json
{
  "Orkeon": {
    "Consensus": {
      "VotingOptions": {
        "ConsensusType": "SuperMajority",
        "ConsensusThreshold": 75,
        "QuorumPercent": 60,
        "MaxVotingRounds": 3,
        "UseWeightedVotes": true,
        "AllowAbstention": false
      },
      "EnableDiscussion": true,
      "FallbackStrategy": "AcceptBestScore",
      "RoleWeights": {
        "Senior Analyst": 2.0,
        "Junior Analyst": 1.0
      }
    }
  }
}
```

> **Note**: `ConsensusThreshold` and `QuorumPercent` are expressed as a **percentage
> (0–100)**, not as a fraction. A super-majority threshold of 75% is written `75`.

### Advantages

- **Robustness**: reduces hallucinations and individual biases
- **Quality**: "collective wisdom" often produces better results
- **Voting flexibility**: 5 consensus strategies, role-based weighting
- **Discussion**: agents can improve each other between rounds
- **Fallback**: 3 fallback strategies (best score, failure, manager decision)

### Drawbacks

- **High LLM cost**: each task is executed N times (N = number of agents) × rounds
- **Slowness**: multiplied LLM calls, especially with discussion enabled
- **Configuration complexity**: many parameters (threshold, quorum, weighting, rounds)
- **Uncertain outcome**: the fallback may produce an unsatisfactory result

### When to use it

- Critical decisions (medical diagnosis, risk assessment, audit)
- Situations where reliability matters more than cost
- Subjective evaluations requiring multiple perspectives
- "Red team" scenarios (several agents try to find flaws)

### When not to use it

- Factual tasks with a single correct answer
- LLM budget constraints (multiplied by N agents × R rounds)
- High-frequency workflows (too slow)

---

## 5. Graph — State graph with controlled cycles

### Principle

Tasks are organized in a **typed state graph** inspired by LangGraph. The graph supports **conditional edges** (dynamic routing) and **controlled cycles** (automatic retry). A 4-mechanism **circuit breaker** prevents infinite loops.

### Internal mechanism

```
START ──→ execute_task ──→ route ──┬── success ──→ execute_task (next)
                                   │                      ↓
                                   │               ... (loop) ...
                                   │                      ↓
                                   ├── failure ──→ execute_task (retry)
                                   │
                                   └── done ──→ END
```

**Key classes**:
- `StateGraph<TState>` — Graph definition (nodes + edges)
- `GraphRunner<TState>` — Execution engine
- `GraphProcessStrategy` — `IProcessStrategy` implementation
- `CircuitBreakerPolicy` — Anti-loop protection
- `CrewGraphState` — Typed state flowing through the graph

### `CrewGraphState` — State properties

| Property | Type | Description |
|-----------|------|-------------|
| `PendingTaskIds` | `Queue<string>` | Remaining tasks to execute |
| `FailedTaskIds` | `Queue<string>` | Tasks eligible for retry |
| `RetryCounts` | `Dict<string, int>` | Per-task retry counter |
| `MaxRetryCycles` | `int` | Maximum number of retries (default: 2) |
| `ApplicationOutputs` | `Dict` | Accumulated outputs |
| `TotalTokensUsed` | `int` | Consumed token counter (propagated to `CrewOutput` metadata under `totalTokens`) |
| `PromptTokensUsed` | `int` | Prompt-side token counter (0 when the provider does not report the split) |
| `CompletionTokensUsed` | `int` | Completion-side token counter (0 when the provider does not report the split) |

### Circuit breaker — 4 protection mechanisms

| Mechanism | Strict | Default | Permissive |
|-----------|--------|---------|------------|
| `MaxTransitions` | 50 | 100 | 1000 |
| `StateTimeout` | 2 min | 5 min | 30 min |
| `MaxStateVisits` | 5 | 10 | 50 |
| `MaxTotalDuration` | 10 min | 30 min | 2 h |

### YAML configuration

```yaml
crew:
  process: graph
  graph_config:
    circuit_breaker: Strict    # or Default, Permissive
    max_retry_cycles: 3
    # Individual overrides possible:
    max_transitions: 75
    state_timeout: "00:03:00"
```

### Advantages

- **Conditional branching**: dynamic routing based on each node's result
- **Built-in retry**: failed tasks are automatically retried
- **Safety**: a 4-level circuit breaker prevents infinite executions
- **Observability**: `OnNodeCompleted`, `OnCircuitBroken` events
- **Useful cycles**: feedback → correction → validation loop
- **Presets**: Strict (production) vs Permissive (development)

### Drawbacks

- **Design complexity**: defining a correct graph takes some thought
- **Debugging**: tracing a path through a cyclic graph is harder
- **Overhead**: the graph engine adds a layer of complexity
- **Circuit breaker**: can cut execution prematurely if misconfigured

### When to use it

- Workflows with conditional branches (validation → OK/KO → different paths)
- Pipelines needing retry with backoff
- Iterative correction scenarios (drafting → review → correction → re-review)
- Regulatory workflows with exception paths

### When not to use it

- Simple linear pipelines (Sequential is enough)
- Independent tasks (Parallel is enough)
- Teams not comfortable with graph modeling

> **See also**: [FSM orchestration](./fsm.md) for the full details.

---

## 6. Autonomous — Self-organization with budget

### Principle

Agents **self-organize**: they claim tasks, delegate recursively to their peers, and can **dynamically spawn** new agents. A **multi-dimensional budget** (5 axes) guarantees termination. Inter-agent communication happens over a bidirectional channel (`IAgentChannel`).

### Internal mechanism

```
                    ┌─── Budget (5 dimensions) ───┐
                    │  tool calls · depth · time   │
                    │  tokens · spawned agents      │
                    └──────────────┬────────────────┘
                                   │
Manager (LLM) ── assigns ──→ Agent A
                                   │
                    ┌──────────────┼──────────────┐
                    ▼              ▼              ▼
              DelegateWork   SpawnAgent      Direct
              (child budget)  (via factory)   execution
                    │              │
                    ▼              ▼
                Agent B       Agent D (new)
                    │              │
                    ▼              ▼
              Result ←──── A2A channel ────→ Result
```

**Key classes**:
- `AutonomousProcessStrategy` — Orchestration strategy
- `AgentExecutionBudget` — Multi-dimensional budget (Domain)
- `IAgentChannel` / `InMemoryAgentChannel` — A2A communication (lock-free)
- `SpawnAgentTool` — Dynamic agent creation
- `DelegateWorkTool` — Delegation with inherited budget
- `BudgetExhaustedException` — Exception thrown when an axis is exhausted

### Multi-dimensional budget — 5 axes

| Dimension | Strict | Default | Permissive | Description |
|-----------|--------|---------|------------|-------------|
| `MaxToolCalls` | 8 | 15 | 50 | Maximum number of tool calls |
| `MaxDelegationDepth` | 1 | 2 | 4 | Delegation depth (A→B→C = 2) |
| `MaxTokensConsumed` | 8,000 | 16,000 | 64,000 | LLM tokens consumed |
| `MaxSpawnedAgents` | 1 | 3 | 10 | Dynamically created agents |
| `MaxWallTime` | 2 min | 5 min | 15 min | Maximum execution duration |

**Child budgets**: when an agent delegates, it creates a child budget derived from its own remaining resources (via `CreateChildBudget()`). The child budget is always less than or equal to the remaining parent budget.

### A2A communication

```csharp
// Request/Response
var request = AgentChannelRequest.Create(
    from: analyst.Id,
    to: researcher.Id,
    intent: "find_data",
    payload: "2025 market statistics");

var response = await channel.RequestAsync(request, timeout);

// Broadcast (fire-and-forget)
await channel.BroadcastAsync(analyst.Id, crew.Id, "Results available", ct);
```

### YAML configuration

```yaml
crew:
  process: autonomous
  autonomous_budget:
    preset: Default           # Strict, Default, or Permissive
    # Possible overrides:
    max_tool_calls: 20
    max_delegation_depth: 3
    max_wall_time: "00:10:00"
    max_tokens_consumed: 32000
    max_spawned_agents: 5
```

### Advantages

- **Maximum adaptability**: agents react to the context in real time
- **Dynamic spawn**: ability to create specialists on demand
- **Guaranteed termination**: the multi-dimensional budget prevents infinite executions
- **Rich communication**: A2A request/response with correlation
- **Presets**: quick configuration (Strict/Default/Permissive)
- **Scalability**: agents distribute the work organically

### Drawbacks

- **High complexity**: the hardest mode to configure and debug
- **Unpredictable cost**: token consumption depends on the agents' decisions
- **Non-deterministic**: two identical runs may follow different paths
- **Overly strict budget**: can cut execution before a complete result is obtained
- **Observability**: requires good logging (BudgetSnapshot) to understand the behavior

### When to use it

- Exploration (research, R&D, exploratory analysis)
- Ill-defined problems where the optimal strategy is not known in advance
- Systems needing self-repair (an agent detects a problem → delegates the fix)
- Multi-step scenarios where each step may reveal new sub-tasks

### When not to use it

- Deterministic, well-defined workflows (Sequential or Graph)
- LLM-budget-constrained environments with no headroom
- Regulatory scenarios requiring full traceability of the execution path

> **See also**: [Autonomous orchestration](./autonomous.md) for the full details.

---

## Decision tree

```
Do your tasks have dependencies between them?
│
├── NO
│   └── Do you need maximum reliability (multiple perspectives)?
│       ├── YES → Consensual
│       └── NO → Parallel
│
└── YES
    └── Does the workflow have conditional branches or loops?
        │
        ├── NO
        │   └── Do you need quality control (manager review)?
        │       ├── YES → Hierarchical
        │       └── NO → Sequential
        │
        └── YES
            └── Is the optimal strategy known in advance?
                ├── YES → Graph (modelable workflow)
                └── NO → Autonomous (exploration)
```

---

## Combinations and complementarity

ProcessTypes are not mutually exclusive at the scale of a system. It is common to combine several strategies:

**Graph + FSM**: The Graph orchestrates the Crew (inter-task level), while the FSM manages the internal execution of each task (intra-task level). Both use `CircuitBreakerPolicy` with the same presets.

**Sequential + Hierarchical**: A sequential Crew can contain tasks whose agents use `DelegateWorkTool` to simulate a local hierarchical behavior.

**Autonomous + Graph**: An autonomous agent can decide to create a Graph sub-workflow to structure a complex sub-task it discovered dynamically.

---

## Cost and performance summary

| ProcessType | LLM calls (N tasks, M agents) | Latency | Predictability |
|-------------|----------------------------------|---------|---------------|
| Sequential | N | Σ(durations) | ⭐⭐⭐⭐⭐ |
| Hierarchical | N × (1 assign + 1-3 reviews) | Σ(durations) × 1.5-3 | ⭐⭐⭐⭐ |
| Parallel | N | max(durations) | ⭐⭐⭐⭐ |
| Consensual | N × M × rounds | Σ(max(durations) × rounds) | ⭐⭐⭐ |
| Graph | N × (1 + retries) | Variable | ⭐⭐⭐ |
| Autonomous | Unpredictable (budget-bounded) | Variable (wall-time bounded) | ⭐⭐ |

---

## Cross references

| Topic | Document |
|-------|----------|
| Architecture and core concepts | [Overview](../getting-started/overview.md) |
| Detailed features | [YAML and Builders](../getting-started/yaml-and-builders.md) |
| FSM orchestration (intra-task) | [./fsm.md](./fsm.md) |
| Graph orchestration (inter-task) | [./graph.md](./graph.md) |
| Autonomous orchestration | [./autonomous.md](./autonomous.md) |
| Blueprint for a new ProcessType | [../guides/blueprint.md](../guides/blueprint.md) |
