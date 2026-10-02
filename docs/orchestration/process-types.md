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
     ├── ConsensualProcessStrategy      ← ExecuteSequentialAsync
     ├── GraphProcessStrategy           ← ExecuteSequentialAsync
     └── AutonomousProcessStrategy      ← ExecuteAutonomousAsync(AgentExecutionBudget.Permissive)
```

`IProcessStrategy` (Domain) has four entry points — `ExecuteSequentialAsync`, `ExecuteHierarchicalAsync`, `ExecuteParallelAsync`, `ExecuteAutonomousAsync`. `SequentialCrewOrchestrator` picks the entry point from `ProcessType.Value`; Graph and Consensual reuse the sequential one, and a strategy throws `NotSupportedException` on the entry points it does not serve. The strategies are registered as scoped services by `AddOrkeonInfrastructure()` (Consensual through `AddOrkeonConsensus()`, which it calls).

What every strategy shares:

- **Task order** — the modes that hand tasks out one after another (all but Parallel) run them in the order resolved by `CrewTaskSequencer`: a stable topological sort on the declared `dependencies` (detailed under Sequential), which follows the planner's order wherever the dependencies allow it when `planning: true` produced a plan (Sequential, Graph, Consensual).
- **Planning** — `planning: true` (YAML) or `.Planning(true)` (C#; the `.ork.ts` DSL has no planning switch) makes one LLM call before the first task, on the provider `CrewBuilder.WithPlanningLlm` sets, else on the host's **default profile** — the `Llm` section: the planner stays there whatever profiles the agents name. It is given the crew's goal, its input and the ids of its agents and tasks, at temperature 0.3, and returns an order. Parallel takes the plan's task list and still builds its waves from the dependencies; Hierarchical and Autonomous make the plan without using it — their manager and their budget hand the tasks out (see [Known limitations](../reference/limitations.md)). A plan that fails fails the run with its cause: the provider's own error (a refused key, an elapsed timeout), or the task the plan left out.
- **Lifecycle hooks** — every mode reports through `ICrewExecutionHook` (`OnTaskStartedAsync`, `OnTaskCompletedAsync`, `OnCrewCompletedAsync`, `OnCrewFailedAsync`), on every exit including cancellation; this is what feeds `AUTO_SUMMARY.md`, the `orkeon run --events` stream and the host's progress.
- **Outcome** — a failed task fails the crew and skips its dependents, in every mode (detailed under Sequential): `Success = false`, an `Error` naming every failed and skipped task, a `Failed` hook, exit code 2.
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
| **Execution model** | Linear | Linear + manager review | Dependency waves, concurrent inside a wave | Every agent per task + peer ballots | Linear + retry cycle | Manager-assigned, delegation on failure |
| **Agent choice** | Declared, else selector | Manager LLM | Declared, else selector | All agents | Declared, else selector | Manager LLM |
| **Task dependencies** | Order + skip on failure | Order + skip on failure | Waves + skip on failure | Order + skip on failure | Order + skip on failure | Order + skip on failure |
| **A failed task fails the crew** | ✅ (dependents skipped) | ✅ (dependents skipped) | ✅ (dependents skipped) | ✅ (dependents skipped) | ✅ after its retries (dependents skipped) | ✅ (dependents skipped; an exhausted budget too) |
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

**Execution order without a plan** (`planning: false`, the default): the tasks run in a **stable topological order on their declared `dependencies`** — a task runs after every task it depends on, and wherever the dependencies allow it the declared order is kept, so a crew that declares no dependency runs exactly as written. This holds in every layout: the multi-file layout (`tasks/*.yaml`) lists the tasks in the ordinal order of their file names, so without the sort `consolidate.yaml` ran before the `extract.yaml` it depends on. A dependency naming an unknown task id is ignored; a cycle never fails the crew — the declared order is kept for the tasks caught in it and a warning names them. The same rule orders the hierarchical, consensual, graph and autonomous modes, which also hand their tasks out one after another; the parallel mode keeps its own semantics (dependency **waves**, and a cycle is refused). With `planning: true`, the planner's order replaces the declared one — still under the dependencies: the planner sees the tasks' ids, not their dependencies, and its plan never runs a task before one it depends on.

**Failure handling (every mode)**: a task whose declared dependency did not succeed — failed, or skipped in its turn — is **skipped**, never run on a context that says `Task failed: …` where its input should have been: it shows as `⊘ skipped` in `AUTO_SUMMARY.md` and as a `task.completed` event with `skipped: true`, the tasks that do not depend on it still run, and the crew fails naming every failed and skipped task (LLM-11). The rule is the same in the six modes: a crew with a failed task returns `Success = false`, its hook hears `Failed` with the same reason, and `orkeon run` exits 2. A mode that tolerates a failure — Graph and its retries, Autonomous and its delegation — does so before the task counts as failed.

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
- **Honest outcome**: a failed step fails the crew and stops its dependents (as in every mode)

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
- Review: up to **3 reviews per task**, so at most **2 re-executions** (each with a `revision_feedback` context variable). A third rejection keeps the last output, prefixed `[NEEDS REVISION]` and marked failed — and the task fails the crew. A review that errors counts as an approval.
- A task assigned to an agent the crew does not carry never runs, and fails the crew. A task whose dependency failed is skipped without asking the manager.

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
- A failed task does not stop its wave siblings; its dependents in the next waves are **skipped**, and the crew fails naming every failed and skipped task.
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
- **No retry**: a failed task fails the crew, and skips what depends on it

### When to use it

- Independent multi-market or multi-source analyses
- Batch content generation (one article per market, per language)
- A fan-out followed by a synthesis: the collectors run together, the synthesis reads them

### When not to use it

- Retry of flaky tasks (use Graph)
- APIs with strict rate limiting

---

## 4. Consensual — Voting and consensus

### Principle

For each task, **every agent of the crew executes it** (concurrently). Then **every agent casts a ballot**: it ranks the other agents' successful answers, anonymised under labels (`A`, `B`, …). The configured `IVotingStrategy` tallies the ballots. If no consensus is reached and `EnableDiscussion` is on, another round runs where each agent sees the others' previous answers (a `discussion_context` context variable). After `MaxVotingRounds` rounds without consensus, the `FallbackStrategy` decides.

### Internal mechanism

```
Task N ──┬── Agent A → answer A ──┐                ┌── A ranks the answers of B, C ──┐
         ├── Agent B → answer B ──┼── labels A,B,C ┼── B ranks the answers of A, C ──┼── Tally ── Consensus? ── yes → winning answer
         └── Agent C → answer C ──┘                └── C ranks the answers of A, B ──┘                │
                                                                                                    no
                                                                                                     ↓
                                                                     Next round (with discussion_context)
                                                                                                     ↓
                                                                   Rounds exhausted → FallbackStrategy
```

**Registration**: `AddOrkeonConsensus()` (`Orkeon.Infrastructure.DependencyInjection`) binds `ConsensualProcessOptions` to the `Orkeon:Consensus` section, registers `IVotingStrategyFactory` → `VotingStrategyFactory` and the `IVotingStrategy` built from the configured `ConsensusType` (singletons, `TryAdd`), `IBallotCollector` → `AgentBallotCollector` (scoped), and `ConsensualProcessStrategy` (scoped). `AddOrkeonInfrastructure()` already calls it — call it yourself only in a host that does not use `AddOrkeonInfrastructure()`; registering your own `IVotingStrategy` or `IBallotCollector` before it replaces the default one.

**Key classes**: `ConsensualProcessStrategy`, `IBallotCollector` / `AgentBallotCollector`, `BallotRequest`, `BallotCandidate`, `Ballot`, `IVotingStrategy`, `IVotingStrategyFactory` / `VotingStrategyFactory`, `Vote`, `VoteResult`, `VotingOptions`, `ConsensualProcessOptions`, `ConsensusFallback`

### How a ballot is formed

- **Candidates**: only the executions that succeeded. A failed execution is never a candidate — its agent still votes, on every answer. A lone successful answer is kept without a ballot; when every execution of a round failed, the task fails with the agents' error, without another round.
- **Anonymised**: the answers carry labels `A`, `B`, … in an order shuffled per task and per round (deterministic: the same run gives the same labels). A voter sees neither the authors' roles nor their ids, so it votes on the answer, not on the agent.
- **No vote for oneself**: an agent ranks the other agents' answers, never its own. A choice's share is therefore counted among the ballots that could name it (`Vote.OwnChoice`): three agents that give the same answer reach `Majority`, `SuperMajority` and `Unanimity` in round one.
- **Who casts it**: the voting agent itself, through `IAgentExecutionService` — its own LLM configuration, and its tokens counted in the task's telemetry. It is asked for a JSON object (response format `json_object`): `{"ranking": ["B", "A"], "abstain": false, "confidence": 0.8, "justification": "…"}`. A reply that is not that object, a ranking that names no offered label, a ballot execution that failed, or `"abstain": true` is an **abstention**: it is logged and never fails the task.
- **What is counted**: `Majority`, `SuperMajority`, `Unanimity` and `WeightedConsensus` count the first choice; `BordaCount` counts the whole ranking. The ballot's `confidence` is read with `UseWeightedVotes`.
- **Ties**: a tie at the top is no consensus. Two agents can only name each other, so their vote always ties — a consensual crew needs three agents or more to decide by vote.
- **The manager**: a crew's `managerAgent` (`CrewBuilder.WithManager` in C#) neither answers nor votes; it only arbitrates under `ManagerDecision`.

**Fallbacks**, after `MaxVotingRounds` rounds without consensus:

- `AcceptBestScore` keeps the answer the last count put first, **without running anything again**. When no ballot of that round named an answer (every voter abstained), the task fails.
- `ManagerDecision`: the crew's manager agent ranks the last round's answers, anonymised like the peers saw them, and its first choice is kept; if it abstains, the task fails. A crew without a manager agent is **refused at kickoff, before any agent runs**, with a message that names the fix (the fallback is a host setting, so the YAML validator cannot see it).
- `Fail` fails the task ("Consensus could not be reached for task …").

The retained result is the task's result: when it failed, the crew fails and the task's dependents are skipped; the tasks that do not depend on it still run.

**What the crew remembers**: the retained answer, once, under the agent that wrote it — never a candidate the vote rejected, never a ballot. The candidates and the ballots run with `SimpleExecutionContext.StoreResultInMemory` off (`AgentBallotCollector` turns it off for any ballot it casts), and the strategy stores the retained answer through `IMemoryCoordinator`; a task without a retained answer stores nothing. When that store fails, the task fails, as in the other modes. See [Memory system](../architecture/memory-system.md#a-crews-memory-provider-and-scope).

**Cost**: a round costs N executions plus N ballots — one short LLM call per voter, whose prompt holds the answers under review, so its input grows with N × the answers' length (a long answer is truncated to fit a task description). Three agents that agree cost 3 executions + 3 ballots per task. A task that never reaches consensus costs `MaxVotingRounds` × (N + N) calls, plus one manager ballot under `ManagerDecision`; `AcceptBestScore` re-runs nothing. The crew's input variables reach every execution and every ballot; earlier tasks' winning outputs are passed as context.

```yaml
name: review-board
process: consensual
managerAgent: chair             # the arbiter of FallbackStrategy: ManagerDecision
```

### Available consensus types

The voting mechanism is **selectable via configuration**: `Orkeon:Consensus:VotingOptions:ConsensusType` (.NET `appsettings.json`) picks the strategy through `IVotingStrategyFactory`. Default: `Majority`. Every type also requires the quorum (`QuorumPercent`) and a single leading answer; a share is counted among the ballots that could name the answer.

| Type | Resolved strategy | Consensus rule | Default threshold |
|------|-------------------|----------------|------------------|
| `Majority` | `MajorityVotingStrategy` | Leading share strictly above 50% | 50% |
| `SuperMajority` | `SuperMajorityVotingStrategy` | Leading share ≥ `ConsensusThreshold` | 66.7% |
| `Unanimity` | `UnanimityVotingStrategy` | Every ballot that could name the winner named it | 100% |
| `WeightedConsensus` | `WeightedConsensusStrategy` | Role weight (`RoleWeights`) × confidence, share above `ConsensusThreshold` | 66.7% |
| `BordaCount` | `BordaCountStrategy` | Best Borda score over the full rankings; no consensus on a tie at the top | N/A |

### Configuration (.NET, `Orkeon:Consensus` section)

```json
{
  "Orkeon": {
    "Consensus": {
      "MaxVotingRounds": 3,
      "VotingOptions": {
        "ConsensusType": "SuperMajority",
        "ConsensusThreshold": 75,
        "UseWeightedVotes": true,
        "QuorumPercent": 50,
        "AllowAbstention": true
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
| `MaxVotingRounds` | 3 | Rounds before the fallback; a round is N executions + N ballots |
| `VotingOptions:ConsensusType` | `Majority` | Voting strategy |
| `VotingOptions:ConsensusThreshold` | 66.7 | Threshold of `SuperMajority` / `WeightedConsensus`, as a **percentage (0–100)** — 75% is written `75` |
| `VotingOptions:UseWeightedVotes` | `false` | Weight × the ballot's confidence instead of one vote per ballot |
| `VotingOptions:QuorumPercent` | 50 | Minimum share, in percent, of expressed ballots (not abstentions) among the voters; below it, no consensus |
| `VotingOptions:AllowAbstention` | `true` | `false`: an abstention counts as a vote against every answer — it stays in every share's denominator and breaks unanimity; with `BordaCount`, any abstention prevents consensus |
| `EnableDiscussion` | `true` | Rounds after the first see the other agents' answers |
| `FallbackStrategy` | `AcceptBestScore` | `AcceptBestScore` (the last count's leader, nothing re-run), `Fail`, `ManagerDecision` (the crew's manager agent chooses; required) |
| `RoleWeights` | empty | Role → weight, read by `WeightedConsensus` |

### Advantages

- **Several independent attempts** per task, run concurrently
- **A vote on the answers**: every agent ranks the others' answers, anonymised; a failed attempt is never retained
- **Configurable voting**: 5 strategies, quorum, abstention, role weighting
- **Discussion**: agents see each other's answers between rounds

### Drawbacks

- **High LLM cost**: N executions + N ballots per task and round (+ one manager ballot under `ManagerDecision`)
- **The judges are LLMs**: a ballot is a model's opinion; a blind spot every agent's model shares is not corrected by the vote
- **Two agents always tie**: the vote decides from three agents up
- **Configuration complexity**: many parameters

### When to use it

- Tasks where independent attempts by several agents are worth their cost
- Crews where one agent's failure should be absorbed by the others (a failed answer is never a candidate)

### When not to use it

- Factual tasks with a single correct answer
- LLM budget constraints (multiplied by 2 × N agents × R rounds: answers and ballots)
- High-frequency workflows (too slow)

---

## 5. Graph — State graph with controlled retry

### Principle

The crew runs through a **typed state graph** (`StateGraph<CrewGraphState>`) with a fixed topology: `execute_task` runs the next pending task, `route` puts a failed task that still has retries left back at the head of the queue — it is retried before the next task runs — and loops back while work remains. A **3-mechanism circuit breaker** bounds the loop. Conditional edges and arbitrary topologies are available through the Domain `StateGraph<TState>` API in C#; the YAML mode does not declare its own graph.

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
| `FailedTaskIds` | `Queue<TaskId>` | Failed tasks the `route` node puts back at the head of the queue |
| `RetryCounts` | `Dictionary<string, int>` | Per-task retry counter |
| `MaxRetryCycles` | `int` | Retries per failed task (default: 2) |
| `ApplicationOutputs` / `DomainResults` | `IReadOnlyList<…>` | Accumulated outputs, one per attempt |
| `TotalTokensUsed` | `int` | Consumed tokens (propagated to the `CrewOutput` metadata under `totalTokens`) |
| `PromptTokensUsed` / `CompletionTokensUsed` | `int` | Prompt/completion split (0 when the provider does not report it) |
| `CacheHitTokensUsed` / `CacheMissTokensUsed` | `long` | Prompt-cache split (0 when unreported) |
| `AgentIndex` | `int` | Round-robin cursor for tasks without an agent |

### Circuit breaker — 3 protection mechanisms

| Mechanism | Computed (default) | Strict preset | Default preset | Permissive preset |
|-----------|--------|--------|---------|------------|
| `MaxTransitions` (node executions) | 2 × visits + 1 | 50 | 100 | 1000 |
| `MaxStateVisits` (visits of one node) | tasks × (1 + `maxRetryCycles`) | 5 | 10 | 50 |
| `MaxTotalDuration` | the preset's (10 min under Strict) | 10 min | 30 min | 2 h |

Every task attempt is one visit of `execute_task` (and one of `route`). Without an explicit `maxStateVisits` / `maxTransitions`, both are **computed from the crew**: `tasks × (1 + maxRetryCycles)` visits — the most a crew can make, every task spending all its retries — and twice that plus one transitions, so a healthy crew is never cut short whatever its size, and a real loop still trips the breaker. An explicit value wins; the preset (`circuitBreakerPreset`, Strict when absent) then only supplies what is not computed, the total duration first. The FSM's fourth mechanism, the per-state timeout, is not checked by the graph runner. A tripped breaker returns a failed `CrewOutput` ("Graph execution stopped by circuit breaker: …") with the outputs produced so far.

### YAML configuration

```yaml
name: "review-loop"
goal: "Graph demo"
process: graph
graphConfig:
  circuitBreakerPreset: "strict"   # or "default", "permissive" — supplies the duration
  maxRetryCycles: 3
  # Individual overrides possible (visits and transitions are computed otherwise):
  maxTransitions: 75
  maxStateVisits: 20
  maxTotalDurationSeconds: 1800
```

### Advantages

- **Built-in retry**: a failed task is retried before the next one runs, up to `maxRetryCycles`
- **Safety**: a circuit breaker bounds executions, visits and duration
- **Observability**: `OnNodeCompleted` / `OnCircuitBroken` events, logged by the strategy
- **Bounds sized to the crew**: visits and transitions computed from the task count and the retries

### Drawbacks

- **10-minute default duration**: the Strict preset's `MaxTotalDuration` still bounds the run — raise `maxTotalDurationSeconds` for a long crew
- **Fixed topology in YAML**: conditional routing needs the C# `StateGraph<TState>` API

### When to use it

- Pipelines with flaky steps worth retrying (web scraping, unstable APIs)
- Runs that must be bounded in executions and duration

### When not to use it

- Simple linear pipelines with no step worth retrying (Sequential)
- Independent tasks (Parallel)

> **See also**: [Graph orchestration](./graph.md) for the full details.

---

## 6. Autonomous — Self-organization with budget

### Principle

For each task, the manager LLM (`LlmBasedManager`, as in Hierarchical) picks the agent that **claims** it. When that agent's execution fails and the agent allows delegation, the task is **delegated to a peer** over the A2A channel (`IAgentChannel`), under a derived child budget. With no peer in the crew, the failure stands. A **multi-dimensional budget** (5 axes) bounds the run. The API is experimental (`ORKEXP002`, see [experimental APIs](../reference/experimental-apis.md)).

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
- **Budget exhausted = run cut short**: the remaining tasks are not executed, and the crew fails naming the exhausted dimension and every task it never reached
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

**Graph + FSM**: `GraphProcessStrategy` and the [FSM](./fsm.md) engine share `CircuitBreakerPolicy` and its presets. The FSM is a generic Domain engine (`orkeon forge` runs on it); no strategy runs a task through a state machine — the agent loop bounds each task.

**Scripting DSL**: a `.ork.ts` crew declares its mode with `crewBuilder().process("…")` (the same six values). The declarative shape (`globalThis.crew = crew`) runs through the strategies above; the procedural shape (`await crew.run()`) runs agent bodies in declaration order and only tags the mode — see [Scripting](../architecture/scripting.md).

---

## Cost and performance summary

| ProcessType | Executions (N tasks, M agents) | Extra LLM calls | Latency | Predictability |
|-------------|------------------|-----------------|---------|---------------|
| Sequential | N | — | Σ(durations) | ⭐⭐⭐⭐⭐ |
| Hierarchical | N × (1 to 3) | N assignments + N × (1 to 3) reviews | Σ(durations) × 1.5–3 | ⭐⭐⭐⭐ |
| Parallel | N | — | Σ(longest task of each wave) | ⭐⭐⭐⭐ |
| Consensual | N × M × rounds | N × M × rounds ballots (+ N manager ballots under `ManagerDecision`) | Σ((max(durations) + max(ballot durations)) × rounds) | ⭐⭐⭐ |
| Graph | N × (1 + retries) | — | Σ(durations of the attempts) | ⭐⭐⭐⭐ |
| Autonomous | N (+ delegations) | N assignments | Bounded by the wall time | ⭐⭐ |

---

## Cross references

| Topic | Document |
|-------|----------|
| Architecture and core concepts | [Overview](../getting-started/overview.md) |
| Detailed features | [YAML and Builders](../getting-started/yaml-and-builders.md) |
| YAML schema (`process`, `graphConfig`) | [YAML schema](../architecture/yaml-schema.md) |
| FSM (generic Domain engine) | [./fsm.md](./fsm.md) |
| Graph orchestration | [./graph.md](./graph.md) |
| Autonomous orchestration | [./autonomous.md](./autonomous.md) |
| Blueprint for a new ProcessType | [../guides/blueprint.md](../guides/blueprint.md) |
