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
ProcessType.Parallel      // Dependency waves, concurrent inside a wave
ProcessType.Consensual    // Every agent runs every task, then a vote
ProcessType.Graph         // State graph with controlled retry cycles
ProcessType.Autonomous    // Self-organization under a budget
```

In YAML the mode is `process:` (case-insensitive, `sequential` when absent); an unknown value fails the load and names the six valid ones (`YamlCrewMapper.ParseProcessType`). With the Fluent Builder, `CrewBuilder` has `.Sequential()`, `.Hierarchical(manager)`, `.Parallel()` and `.Consensual()`; Graph and Autonomous go through `.Process(ProcessType.Graph)` / `.Process(ProcessType.Autonomous)`.

### Implementation architecture

```
ICrewOrchestrationService.KickoffAsync (SequentialCrewOrchestrator)
     │  planning (when enabled), then a switch on crew.ProcessType
     ▼
IProcessStrategyFactory.CreateStrategy(ProcessType)   (ProcessStrategyFactory, Infrastructure)
     │
     ├── SequentialProcessStrategy      ← ExecuteSequentialAsync
     ├── HierarchicalProcessStrategy    ← ExecuteHierarchicalAsync(manager id)
     ├── ParallelProcessStrategy        ← ExecuteParallelAsync
     ├── ConsensualProcessStrategy      ← ExecuteSequentialAsync (also IConsensualProcessStrategy)
     ├── GraphProcessStrategy           ← ExecuteSequentialAsync
     └── AutonomousProcessStrategy      ← ExecuteAutonomousAsync(AgentExecutionBudget.Permissive)
```

`IProcessStrategy` (Domain) has four entry points — `ExecuteSequentialAsync`, `ExecuteHierarchicalAsync`, `ExecuteParallelAsync`, `ExecuteAutonomousAsync`. `SequentialCrewOrchestrator` picks the entry point from `ProcessType.Value`; Graph and Consensual reuse the sequential one, and a strategy throws `NotSupportedException` on the entry points it does not serve. The strategies are registered as scoped services by `AddOrkeonInfrastructure()` (Consensual through `AddOrkeonConsensus()`, which it calls).

What every strategy shares:

- **Task order** — the modes that hand tasks out one after another (all but Parallel) run them in the order resolved by `CrewTaskSequencer`: the planner's order when `planning: true` produced one, otherwise a stable topological sort on the declared `dependencies` (detailed under Sequential).
- **Lifecycle hooks** — every mode reports through `ICrewExecutionHook` (`OnTaskStartedAsync`, `OnTaskCompletedAsync`, `OnCrewCompletedAsync`, `OnCrewFailedAsync`), on every exit including cancellation; this is what feeds `AUTO_SUMMARY.md`, the `orkeon run --events` stream and the host's progress.
- **Token telemetry** — the real token usage (prompt, completion, cache hits/misses when the provider reports them) travels in the `CrewOutput` metadata; `CrewOutput.TokensUsed` stays `null` when nothing was measured.

### Who runs a task that names no agent

A task-level `agent:` is a decision, not a hint: Sequential, Parallel and Graph always honour it. For a task without one, those three modes ask `TaskAgentSelector`, driven by `OrkeonApplicationOptions.AgentSelectionStrategy`:

| Value | Behavior |
|-------|----------|
| `FirstFit` (default) | Round-robin over the crew's agents |
| `Embedding` | Semantic match between the task and each agent profile (needs a real embedding provider — see [limitations](../reference/limitations.md)) |
| `Skill` | Lexical (Jaccard) match on role/goal/backstory keywords, no embedding provider |

A selection that fails, or names an agent the crew does not carry, falls back to round-robin with a warning. Hierarchical and Autonomous let the manager LLM choose (a task's `agent:` is not consulted there); Consensual runs every task with every agent.

---

## Quick comparison matrix

| Criterion | Sequential | Hierarchical | Parallel | Consensual | Graph | Autonomous |
|---------|-----------|-------------|---------|-----------|-------|-----------|
| **Execution model** | Linear | Linear + manager review | Dependency waves, concurrent inside a wave | Every agent per task + vote | Linear + retry cycle | Manager-assigned, delegation on failure |
| **Agent choice** | Declared, else selector | Manager LLM | Declared, else selector | All agents | Declared, else selector | Manager LLM |
| **Task dependencies** | Order + skip on failure | Order | Waves | Order | Order | Order |
| **A failed task fails the crew** | ✅ (dependents skipped) | — | — | Only with `FallbackStrategy: Fail` | — (circuit breaker only) | — |
| **Circuit breaker** | — | — | — | — | ✅ 3 mechanisms | — |
| **Execution budget** | — | — | — | — | — | ✅ 5 dimensions (Permissive) |
| **Automatic retry** | — | Up to 2 re-executions after review | — | Voting rounds | ✅ `maxRetryCycles` | One delegation to a peer |
| **Complexity** | ⭐ | ⭐⭐ | ⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐ |
| **Relative LLM cost** | Low | Medium | Low | High | Low to medium | Medium |
| **Main use case** | ETL pipelines | QA, review loops | Fan-out + synthesis | Multiple independent attempts | Flaky tasks worth retrying | Exploration, R&D |

---

## 1. Sequential — Linear pipeline

### Principle

Tasks execute **one by one**. Each task receives the outputs of the tasks that ran before it as context. Agent assignment follows the rule above: the task's `agent:` when declared, otherwise the configured selector (round-robin by default).

**Execution order without a plan** (`planning: false`, the default): the tasks run in a **stable topological order on their declared `dependencies`** — a task runs after every task it depends on, and wherever the dependencies allow it the declared order is kept, so a crew that declares no dependency runs exactly as written. This holds in every layout: the multi-file layout (`tasks/*.yaml`) lists the tasks in the ordinal order of their file names, so without the sort `consolidate.yaml` ran before the `extract.yaml` it depends on. A dependency naming an unknown task id is ignored; a cycle never fails the crew — the declared order is kept for the tasks caught in it and a warning names them. The same rule orders the hierarchical, consensual, graph and autonomous modes, which also hand their tasks out one after another; the parallel mode keeps its own semantics (dependency **waves**, and a cycle is refused). With `planning: true`, the planner's order is taken as is.

**Failure handling (Sequential only)**: a task whose declared dependency did not succeed — failed, or skipped in its turn — is **skipped**, never run on a context that says `Task failed: …` where its input should have been: it shows as `⊘ skipped` in `AUTO_SUMMARY.md` and as a `task.completed` event with `skipped: true`, the tasks that do not depend on it still run, and the crew fails naming every failed and skipped task (LLM-11). The other modes do not skip dependents and report the crew as completed even when a task failed (see the matrix).

### Internal mechanism

```
Task 1 → Agent A → output₁
                      ↓ (enriched context)
Task 2 → Agent B → output₂
                      ↓
Task 3 → Agent C → output₃ → final result
```

**Key classes**: `SequentialProcessStrategy`, `CrewTaskSequencer`, `TaskAgentSelector`, `ExecutionPlan`, `AgentDelegationToolsProvider` (agents with `allowDelegation: true` receive `delegate_work_to_coworker` and `ask_question_to_coworker`)

### YAML configuration

```yaml
# Flat root — there is no crew: wrapper key, and agents:/tasks: are mappings
# keyed by id (a crew:-wrapped file loads SILENTLY as an empty crew).
name: "pipeline"
goal: "Sequential demo"
process: sequential
tasks:
  collect:
    description: "Collect the data"
    expectedOutput: "Raw data"
  analyze:
    description: "Analyze the data"
    expectedOutput: "Analysis report"
    dependencies: [collect]
  recommend:
    description: "Generate the recommendations"
    expectedOutput: "Action plan"
    dependencies: [analyze]
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
- **Honest outcome**: the only mode where a failed step fails the crew and stops its dependents

### Drawbacks

- **No parallelism**: the total time is the sum of all the tasks
- **Single point of failure**: one failure blocks the rest of the chain — its dependents are skipped, only the independent tasks still run
- **No retry**: no automatic recovery on error
- **Rigid**: the order is fixed, no conditional branching

### When to use it

- ETL pipelines (extract → transform → load)
- Sequential writing (research → drafting → proofreading)
- Workflows where each step strictly depends on the previous one
- Rapid prototyping and POCs

### When not to use it

- Independent tasks that could run in parallel
- Workflows that should retry a flaky step (Graph)

---

## 2. Hierarchical — Manager + Workers

### Principle

A **manager** (`IManagerAgent`, implemented by `LlmBasedManager`) coordinates the workers. For each task it picks a worker via `AssignTaskAsync()`, the worker executes, then the manager **reviews the output** with `ReviewOutputAsync()`.

- The crew must name its manager: `managerAgent:` in YAML (or `.Hierarchical(manager)` / `.WithManagerId(...)`); without one the run fails with "Hierarchical process requires a manager agent". That agent is removed from the worker pool.
- The manager's decisions go through the **host's registered LLM** (`IChatClient` when present, else `IBasicLlmProvider` — the `Llm` section in a runner host), not through the manager agent's own `llm:` block; they are metered under the manager's role.
- Assignment: the manager LLM answers with JSON; an unparseable answer falls back to a role/keyword heuristic, an LLM error to the first worker. A task's `agent:` is not consulted.
- Review: up to **3 reviews per task**, so at most **2 re-executions** (each with a `revision_feedback` context variable). A third rejection keeps the last output, prefixed `[NEEDS REVISION]` and marked failed. A review that errors counts as an approval.

### Internal mechanism

```
                  ┌─── Manager (host LLM) ───┐
                  │   AssignTaskAsync()       │
                  │   ReviewOutputAsync()     │
                  └────────┬──────────────────┘
                           │
            ┌──────────────┼──────────────┐
            ▼              ▼              ▼
        Worker A       Worker B       Worker C
        (chosen)
            │
            ▼
        Output → Review → approved? → yes → next task
                           → no → re-execute (max 2), then [NEEDS REVISION]
```

**Key classes**: `HierarchicalProcessStrategy`, `IManagerAgent`, `LlmBasedManager`, `TaskAssignment` (`TaskId`, `AssignedAgent`, `Reason`, `AssignedAt`)

**`IManagerAgent` interface** (`Orkeon.Application.Interfaces`):
- `AssignTaskAsync(task, availableAgents, context)` → `TaskAssignment`
- `ReviewOutputAsync(output, originalTask)` → `bool` (approved or not)

### YAML configuration

```yaml
name: "delivery-team"
goal: "Hierarchical demo"
process: hierarchical
managerAgent: lead          # the id of the managing agent (there is no manager_llm key)
llm:                        # crew-default LLM applied to agents without their own
  model: gpt-4o
agents:
  lead:
    role: "Tech Lead"
    goal: "Coordinate the delivery"
  dev:
    role: "Senior Developer"
    goal: "Write production code"
  qa:
    role: "QA Engineer"
    goal: "Test and validate"
```

### Advantages

- **Smart allocation**: the manager picks the worker best suited to each task
- **Built-in quality control**: automatic review loop
- **Traceability**: every assignment carries a justification (`TaskAssignment.Reason`)

### Drawbacks

- **Extra LLM cost**: one assignment call + one to three review calls per task, on top of the workers
- **Bottleneck**: everything goes through the manager (no parallelism)
- **Fixed revisions**: 3 reviews per task, hardcoded
- **Soft failure**: a rejected or failed task does not fail the crew — read the task outputs

### When to use it

- Code review (the manager assigns the most competent reviewer)
- Projects with heterogeneous specialists (dev, QA, design, writing)
- Situations where quality matters more than speed

### When not to use it

- Homogeneous tasks (round-robin is enough)
- Strict LLM cost constraints
- High-frequency workflows (the manager is a bottleneck)

---

## 3. Parallel — Dependency waves

### Principle

Tasks are grouped into **dependency waves**. A wave holds every task whose declared `dependencies` are already satisfied; its tasks run **concurrently** (`Task.WhenAll`), and the next wave starts when they are all done, **reading their outputs** as context. A crew declaring no dependency is a single wave — one flat fan-out.

- A dependency naming a task the crew does not carry counts as satisfied.
- A dependency **cycle is refused**: the run fails naming the tasks caught in it.
- A failed task does not stop its wave siblings, nor its dependents in the next wave, and the crew is reported as completed.
- There is no concurrency cap: every task of a wave calls its LLM at the same time.

### Internal mechanism

```
Wave 1 ──┬── Task A → Agent 1 ──┐
         └── Task B → Agent 2 ──┤  (concurrent)
                                ▼
Wave 2 ────── Task C (depends on A, B) → reads A + B → final result
```

**Key classes**: `ParallelProcessStrategy`, `TaskAgentSelector`

### YAML configuration

```yaml
name: "market-scan"
goal: "Parallel demo"
process: parallel
tasks:
  france:
    description: "Analyze the French market"
    expectedOutput: "France report"
  germany:
    description: "Analyze the German market"
    expectedOutput: "Germany report"
  synthesis:
    description: "Compare the two markets"
    expectedOutput: "Comparative summary"
    dependencies: [france, germany]   # second wave, reads both reports
```

### Advantages

- **Speed**: total time = sum of the longest task of each wave
- **Simplicity**: no coordination beyond the declared dependencies
- **Isolation**: one task's failure does not impact its wave siblings

### Drawbacks

- **Coarse ordering**: a task waits for its whole wave, not only for what it declared
- **API consumption spikes**: all the LLM requests of a wave fire at once (rate limiting)
- **No retry**, and a failed task still feeds (as a failure message) the next wave

### When to use it

- Independent multi-market or multi-source analyses
- Batch content generation (one article per market, per language)
- A fan-out followed by a synthesis: the collectors run together, the synthesis reads them

### When not to use it

- Retry of flaky tasks (use Graph)
- APIs with strict rate limiting
- Pipelines where a failed step must stop what depends on it (use Sequential)

---

## 4. Consensual — Voting and consensus

### Principle

For each task, **every agent of the crew executes it** (concurrently). The executions are turned into ballots and tallied by the configured `IVotingStrategy`. If no consensus is reached and `EnableDiscussion` is on, another round runs where each agent sees the others' previous outputs (a `discussion_context` context variable). After `MaxVotingRounds` rounds without consensus, the `FallbackStrategy` decides.

### Internal mechanism

```
Task N ──┬── Agent A → result A ────┐
         ├── Agent B → result B ────┼── Tally ── Consensus? ── yes → winning result
         └── Agent C → result C ────┘                │
                                                     no
                                                      ↓
                                     Next round (with discussion_context)
                                                      ↓
                                   Rounds exhausted → FallbackStrategy
```

**Registration**: `AddOrkeonConsensus()` (`Orkeon.Infrastructure.DependencyInjection`) binds `ConsensualProcessOptions` to the `Orkeon:Consensus` section, registers `IVotingStrategyFactory` → `VotingStrategyFactory` and the `IVotingStrategy` built from the configured `ConsensusType` (singletons, `TryAdd`), and `ConsensualProcessStrategy` (scoped) with `IConsensualProcessStrategy` mapped to the same instance. `AddOrkeonInfrastructure()` already calls it — call it yourself only in a host that does not use `AddOrkeonInfrastructure()`; registering your own `IVotingStrategy` before it replaces the configured one.

**Key classes**: `ConsensualProcessStrategy` (also `IConsensualProcessStrategy`), `IVotingStrategy`, `IVotingStrategyFactory` / `VotingStrategyFactory`, `Vote`, `VoteResult`, `VotingOptions`, `ConsensualProcessOptions`, `ConsensusFallback`

### How a ballot is formed — read before relying on the vote

The pipeline does **not** compare the contents of the outputs. Each agent's execution becomes **one ballot for itself**: `Choice` = its own agent id, `Confidence` = 1.0 when its execution succeeded and 0.1 when it failed, `Weight` = 1.0. Consequences with two agents or more:

- `Majority`, `SuperMajority` and `Unanimity` without weighting **never reach consensus** (each choice holds 1/N of the votes); with `UseWeightedVotes: true` (or `WeightedConsensus`), a choice wins when the other agents' executions failed.
- `BordaCount` expects comma-separated rankings; a single-choice ballot scores 0 for everyone, so it declares the **first agent** the winner in round 1.
- The fallback `AcceptBestScore` **re-executes the task with the first agent** and keeps that output; `ManagerDecision` does the same today; `Fail` stops the crew ("Consensus could not be reached for task …").

In practice, for a crew whose agents all succeed, every task costs `MaxVotingRounds` × N executions plus one fallback execution (N executions with `BordaCount`). The crew's input variables are not interpolated in this mode; earlier tasks' winning outputs are passed as context.

### Available consensus types

The voting mechanism is **selectable via configuration**: `Orkeon:Consensus:VotingOptions:ConsensusType` (.NET `appsettings.json`) picks the strategy through `IVotingStrategyFactory`. Default: `Majority`.

| Type | Resolved strategy | Consensus rule | Default threshold |
|------|-------------------|----------------|------------------|
| `Majority` | `MajorityVotingStrategy` | Winning share strictly above 50% | 50% |
| `SuperMajority` | `SuperMajorityVotingStrategy` | Winning share ≥ `ConsensusThreshold` | 66.7% |
| `Unanimity` | `UnanimityVotingStrategy` | A single distinct choice | 100% |
| `WeightedConsensus` | `WeightedConsensusStrategy` | Role weight (`RoleWeights`) × confidence, share above `ConsensusThreshold` | 66.7% |
| `BordaCount` | `BordaCountStrategy` | Borda score ranking, always a winner | N/A |

### Configuration (.NET, `Orkeon:Consensus` section)

```json
{
  "Orkeon": {
    "Consensus": {
      "MaxVotingRounds": 3,
      "VotingOptions": {
        "ConsensusType": "SuperMajority",
        "ConsensusThreshold": 75,
        "UseWeightedVotes": true
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

| Key | Default | Effect |
|-----|---------|--------|
| `MaxVotingRounds` | 3 | Rounds before the fallback (the `VotingOptions:MaxVotingRounds` copy is not read) |
| `VotingOptions:ConsensusType` | `Majority` | Voting strategy |
| `VotingOptions:ConsensusThreshold` | 66.7 | Threshold of `SuperMajority` / `WeightedConsensus`, as a **percentage (0–100)** — 75% is written `75` |
| `VotingOptions:UseWeightedVotes` | `false` | Weight × confidence instead of one vote per ballot |
| `VotingOptions:QuorumPercent`, `VotingOptions:AllowAbstention` | 50, `true` | Carried to the strategies but not enforced by any of them |
| `EnableDiscussion` | `true` | Rounds after the first see the other agents' outputs |
| `FallbackStrategy` | `AcceptBestScore` | `AcceptBestScore`, `Fail`, `ManagerDecision` (same as `AcceptBestScore` today) |
| `RoleWeights` | empty | Role → weight, read by `WeightedConsensus` |

### Advantages

- **Several independent attempts** per task, run concurrently
- **Configurable voting**: 5 strategies, role weighting
- **Discussion**: agents see each other's outputs between rounds

### Drawbacks

- **High LLM cost**: N agents × rounds (+ fallback) executions per task
- **The vote is not semantic** (see above): it measures which executions succeeded, not which answer is best
- **Configuration complexity**: many parameters, some carried but not enforced

### When to use it

- Tasks where independent attempts by several agents are worth their cost
- Crews where one agent's failure should be absorbed by the others (weighted votes)

### When not to use it

- Factual tasks with a single correct answer
- LLM budget constraints (multiplied by N agents × R rounds)
- High-frequency workflows (too slow)

---

## 5. Graph — State graph with controlled retry

### Principle

The crew runs through a **typed state graph** (`StateGraph<CrewGraphState>`) with a fixed topology: `execute_task` runs the next pending task, `route` re-enqueues the failed tasks that still have retries left and loops back while work remains. A **3-mechanism circuit breaker** bounds the loop. Conditional edges and arbitrary topologies are available through the Domain `StateGraph<TState>` API in C#; the YAML mode does not declare its own graph.

### Internal mechanism

```
START ──→ execute_task ──→ route ──┬── tasks pending (or failed with retries left) ──→ execute_task
                                   │
                                   └── nothing left ──→ END
```

**Key classes**:
- `StateGraph<TState>` — Graph definition (nodes + fixed and conditional edges)
- `GraphRunner<TState>` — Execution engine with circuit breaker
- `GraphProcessStrategy` — `IProcessStrategy` implementation
- `CircuitBreakerPolicy` — Limits (the record shared with the FSM)
- `CrewGraphState` — Typed state flowing through the graph

### `CrewGraphState` — State properties

| Property | Type | Description |
|-----------|------|-------------|
| `PendingTaskIds` | `Queue<TaskId>` | Remaining tasks to execute |
| `FailedTaskIds` | `Queue<TaskId>` | Tasks waiting for a retry cycle |
| `RetryCounts` | `Dictionary<string, int>` | Per-task retry counter |
| `MaxRetryCycles` | `int` | Retries per failed task (default: 2) |
| `ApplicationOutputs` / `DomainResults` | `IReadOnlyList<…>` | Accumulated outputs, one per attempt |
| `TotalTokensUsed` | `int` | Consumed tokens (propagated to the `CrewOutput` metadata under `totalTokens`) |
| `PromptTokensUsed` / `CompletionTokensUsed` | `int` | Prompt/completion split (0 when the provider does not report it) |
| `CacheHitTokensUsed` / `CacheMissTokensUsed` | `long` | Prompt-cache split (0 when unreported) |
| `AgentIndex` | `int` | Round-robin cursor for tasks without an agent |

### Circuit breaker — 3 protection mechanisms

| Mechanism | Strict (default) | Default | Permissive |
|-----------|--------|---------|------------|
| `MaxTransitions` (node executions) | 50 | 100 | 1000 |
| `MaxStateVisits` (visits of one node) | 5 | 10 | 50 |
| `MaxTotalDuration` | 10 min | 30 min | 2 h |

Every task attempt is one visit of `execute_task`, so `MaxStateVisits` caps the number of task attempts in the whole run: **5 under Strict** — raise `maxStateVisits` for a crew with more tasks (retries included). The FSM's fourth mechanism, the per-state timeout, is not checked by the graph runner. A tripped breaker returns a failed `CrewOutput` ("Graph execution stopped by circuit breaker: …") with the outputs produced so far.

### YAML configuration

```yaml
name: "review-loop"
goal: "Graph demo"
process: graph
graphConfig:
  circuitBreakerPreset: "strict"   # or "default", "permissive"
  maxRetryCycles: 3
  # Individual overrides possible:
  maxTransitions: 75
  maxStateVisits: 20
  maxTotalDurationSeconds: 1800
```

### Advantages

- **Built-in retry**: failed tasks are retried after the pending ones, up to `maxRetryCycles`
- **Safety**: a circuit breaker bounds executions, visits and duration
- **Observability**: `OnNodeCompleted` / `OnCircuitBroken` events, logged by the strategy
- **Presets**: Strict (default) vs Permissive

### Drawbacks

- **Strict is tight**: the default caps the run at 5 task attempts
- **Soft failure**: a task still failing after its retries does not fail the crew (only the breaker does)
- **Fixed topology in YAML**: conditional routing needs the C# `StateGraph<TState>` API

### When to use it

- Pipelines with flaky steps worth retrying (web scraping, unstable APIs)
- Runs that must be bounded in executions and duration

### When not to use it

- Simple linear pipelines where a failure should stop the run (Sequential)
- Independent tasks (Parallel)

> **See also**: [Graph orchestration](./graph.md) for the full details.

---

## 6. Autonomous — Self-organization with budget

### Principle

For each task, the manager LLM (`LlmBasedManager`, as in Hierarchical) picks the agent that **claims** it. When that agent's execution fails and the agent allows delegation, the task is **delegated to a peer** over the A2A channel (`IAgentChannel`), under a derived child budget. A **multi-dimensional budget** (5 axes) bounds the run. The API is experimental (`ORKEXP002`, see [experimental APIs](../reference/experimental-apis.md)).

### Internal mechanism

```
                    ┌─── AgentExecutionBudget (5 dimensions) ───┐
                    │  tool calls · depth · wall time            │
                    │  tokens · spawned agents                   │
                    └──────────────────┬─────────────────────────┘
                                       │
for each task:  Manager (LLM) ── assigns ──→ Agent A ── executes
                                       │
                          failed + allowDelegation?
                                       │ yes
                                       ▼
                  channel.RequestAsync("delegate") ──→ first other agent
                                                        (child budget)
```

**Key classes**:
- `AutonomousProcessStrategy` — Orchestration strategy
- `AgentExecutionBudget` — Multi-dimensional budget (Domain)
- `IAgentChannel` / `InMemoryAgentChannel` — A2A request/response channel
- `SpawnAgentTool` — Dynamic agent creation (host-constructed, not injected by the strategy)
- `BudgetExhaustedException` — Thrown when an axis is exhausted

### Multi-dimensional budget — 5 axes

| Dimension | Strict | Default | Permissive | Description |
|-----------|--------|---------|------------|-------------|
| `MaxToolCalls` | 8 | 15 | 50 | Tool calls; the strategy counts one per task handed out |
| `MaxDelegationDepth` | 1 | 2 | 4 | Delegation hops; the crew-wide counter is never decremented |
| `MaxTokensConsumed` | 8,000 | 16,000 | 64,000 | Tokens recorded against the budget (delegated executions) |
| `MaxSpawnedAgents` | 1 | 3 | 10 | Agents created through `SpawnAgentTool` |
| `MaxWallTime` | 2 min | 5 min | 15 min | Maximum execution duration |

`ICrewOrchestrationService` always runs this mode with **`AgentExecutionBudget.Permissive`**; another budget requires calling `AutonomousProcessStrategy.ExecuteAutonomousAsync(crew, budget, …)` directly.

**Child budgets**: `CreateChildBudget()` gives the delegate the parent's **remaining** allowance on each axis (one less delegation level, at least 1 tool call and 100 tokens). Each child budget is an independent copy: two delegates each receive the full remainder.

### A2A communication

```csharp
// Request/Response (default timeout: 30 s)
var request = AgentChannelRequest.Create(
    from: analyst.Id,
    to: researcher.Id,
    intent: "find_data",
    payload: "2025 market statistics");

var response = await channel.RequestAsync(request, timeout: TimeSpan.FromSeconds(30));

// Broadcast (no response expected) to the members registered for that crew
await channel.BroadcastAsync(analyst.Id, crew.Id, "Results available", ct);
```

### YAML configuration

```yaml
name: "research-team"
goal: "Autonomous demo"
process: autonomous
# There is NO autonomous budget key in YAML: a YAML autonomous crew always runs
# under AgentExecutionBudget.Permissive (50 tool calls, depth 4, 15 min,
# 64 000 tokens, 10 spawns) — see the Autonomous guide and yaml-schema.md.
```

### Advantages

- **LLM-driven assignment**: the manager matches each task to an agent
- **Second chance**: a failed task is handed to a peer
- **Guaranteed termination**: the budget and its wall time bound the run
- **Budget telemetry**: the crew metadata carries the consumption of each axis

### Drawbacks

- **Experimental**: `ORKEXP002`, semantics may still move
- **Non-deterministic**: the manager's choices vary between runs
- **Budget exhausted = run cut short**: the remaining tasks are not executed, and the crew is still reported as completed
- **Fixed budget** through the orchestrator (Permissive)

### When to use it

- Exploration (research, R&D, exploratory analysis)
- Heterogeneous teams where the task-to-agent match is not known in advance

### When not to use it

- Deterministic, well-defined workflows (Sequential or Graph)
- Regulatory scenarios requiring full traceability of the execution path

> **See also**: [Autonomous orchestration](./autonomous.md) for the full details.

---

## Decision tree

```
Do your tasks have dependencies between them?
│
├── NO
│   └── Do you want several agents to attempt each task?
│       ├── YES → Consensual
│       └── NO → Parallel
│
└── YES
    └── Should a flaky task be retried automatically?
        │
        ├── NO
        │   └── Do you need a manager to assign and review?
        │       ├── YES → Hierarchical
        │       └── NO → Sequential
        │
        └── YES
            └── Retry the same task, or hand it to a peer?
                ├── Same task, bounded → Graph
                └── A peer, LLM-assigned → Autonomous
```

---

## Combinations and complementarity

ProcessTypes are exclusive at the scale of one crew, but they combine with the other building blocks:

**Sequential/Graph + delegation tools**: in these two modes, an agent with `allowDelegation: true` receives `delegate_work_to_coworker` and `ask_question_to_coworker`, giving a local hierarchical behavior without a manager.

**Graph + FSM**: `GraphProcessStrategy` and the [FSM](./fsm.md) share `CircuitBreakerPolicy` and its presets. The FSM (`TaskExecutionStateMachine`) is a Domain building block for your own code: no strategy drives its tasks through it today.

**Flows**: to chain a crew with LLM calls, tool calls, conditions or human input, a [flow](./flows.md) (`IFlowEngine`) runs a crew as one of its steps — a C# API today, not reachable from `orkeon run`.

**Scripting DSL**: a `.ork.ts` crew declares its mode with `crewBuilder().process("…")` (the same six values). The declarative shape (`globalThis.crew = crew`) runs through the strategies above; the procedural shape (`await crew.run()`) runs agent bodies in declaration order and only tags the mode — see [Scripting](../architecture/scripting.md).

---

## Cost and performance summary

| ProcessType | Executions (N tasks, M agents) | Extra LLM calls | Latency | Predictability |
|-------------|------------------|-----------------|---------|---------------|
| Sequential | N | — | Σ(durations) | ⭐⭐⭐⭐⭐ |
| Hierarchical | N × (1 to 3) | N assignments + N × (1 to 3) reviews | Σ(durations) × 1.5–3 | ⭐⭐⭐⭐ |
| Parallel | N | — | Σ(longest task of each wave) | ⭐⭐⭐⭐ |
| Consensual | N × M × rounds (+ N fallback) | — | Σ(max(durations) × rounds) | ⭐⭐⭐ |
| Graph | N × (1 + retries) | — | Σ(durations of the attempts) | ⭐⭐⭐⭐ |
| Autonomous | N (+ delegations) | N assignments | Bounded by the wall time | ⭐⭐ |

---

## Cross references

| Topic | Document |
|-------|----------|
| Architecture and core concepts | [Overview](../getting-started/overview.md) |
| Detailed features | [YAML and Builders](../getting-started/yaml-and-builders.md) |
| YAML schema (`process`, `graphConfig`, `circuitBreaker`) | [YAML schema](../architecture/yaml-schema.md) |
| FSM (Domain building block) | [./fsm.md](./fsm.md) |
| Graph orchestration | [./graph.md](./graph.md) |
| Autonomous orchestration | [./autonomous.md](./autonomous.md) |
| Flows (steps chaining crews, LLM and tools) | [./flows.md](./flows.md) |
| Blueprint for a new ProcessType | [../guides/blueprint.md](../guides/blueprint.md) |
