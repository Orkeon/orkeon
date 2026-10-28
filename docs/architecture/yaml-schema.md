> 🇫🇷 [Version française](../fr/architecture/yaml-schema.md)

# YAML reference — Complete schema

This document is the **single source of truth** for the Orkeon YAML schema. The specialized documents (FSM, Graph, Autonomous) refer back here for the schema.

## Complete configuration schema

The YAML structure follows this schema. **Key naming**: the loader (`YamlDotNetSerializer`)
matches keys in camelCase (the canonical form, the one `YamlCrewExporter` writes — see
[Export](#export)) and falls back to snake_case for the same property — `expectedOutput` and `expected_output`, `llm_override` and
`llmOverride` are equivalent everywhere. Every key is read or refused at load: a key no model carries (or a retired one) fails the
load with its file, line and path, and the nearest key (`Did you mean 'expectedOutput'?`);
`CrewYamlKeyCheck` runs before the deserialization. Only the names you choose stay free: the ids under
`agents:` and `tasks:`, the tool names under `toolRules:`, and the keys and values of `context:`. A merge
key (`<<:`) is refused, and so is an anchor on an unknown key: share text under `anchors:`. The same models
serve the single-file layout and the multi-file layouts (`crew.yaml` + `agents.yaml` +
`tasks.yaml`, or `config.yaml` + `agents/` + `tasks/`) described in
[YAML and Builders](../getting-started/yaml-and-builders.md); a top-level `anchors:` mapping of
named multi-line strings is expanded before parsing (`YamlAnchorPreprocessor`).

```yaml
# Complete CrewYamlConfig schema
name: string              # Crew identifier (required); scopes the crew's long-term memory
goal: string              # Goal (required)
process: string           # "sequential" (default) | "hierarchical" | "parallel" | "consensual" | "graph" | "autonomous" — case-insensitive; an unknown value fails the load
verbose: bool             # default: false — every agent of the run logs its turns (tool calls, tool results, model responses) at Information, category Orkeon.Verbose
memory: bool              # default: false. true: the crew stores the result of each task and recalls the closest ones before each task (embedder required at kickoff); false: nothing is stored or recalled
memoryProvider: string    # Requires memory: true (refused otherwise). "InMemory" | "Redis" | "Sqlite" | "ChromaDb" | "Pinecone" | "LanceDb" — case-insensitive (aliases "in-memory", "chroma", "lance"); unknown → in-memory with a warning. The TYPE only: the connection comes from the host section (Orkeon:Redis, Orkeon:Sqlite, …). Unset: the host's default store (Memory:Provider)
planning: bool            # default: false. true: before the first task, a planner (the host's default profile) writes a step-by-step plan per task, which the task reads in its prompt, in every mode; it changes neither the order nor the agents
maxRpm: int               # default: none — at most N model requests per minute for the whole crew, all its agents and its manager together (parallel waves included); one more waits its turn. CrewAI's max_rpm (max_rpm: is accepted). 0 or less fails the load
budget: {tokens, wallTimeSeconds, toolCalls, delegationDepth, spawnedAgents}  # process: autonomous only — see Autonomous budget below; default: no token or time cap, 50 / 4 / 10
managerAgent: string      # The key of one of the crew's agents (any other name fails the load, listing them). Hierarchical: the manager — required (omitted, the load fails); it assigns and reviews on its own llm: block, profile and model. Consensual: the arbiter of the ManagerDecision fallback (idle, with a warning at the start of the run, under another fallback). Any other process: refused at load
graphConfig: {…}          # Graph mode settings, the crew's only circuit-breaker setting (see the dedicated section)

llm:                      # Crew-default LLM, merged FIELD BY FIELD under each agent's own llm: (same shape as agents.<id>.llm)
  profile: string         # Host LLM profile (Llm:Profiles:<name>) for every agent — unknown → the load fails, listing the host's profiles
  model: string
  max_tokens: int         # Positive; omitted = the inherited cap, 0 or less fails the load naming the crew
  temperature: float

links:                    # EventHub ACL (optional) — who this crew may talk to on the hub
  - to: string            # The target crew's name:, verbatim — or "client:<name>" for an external peer (e.g. Studio)
    direction: string     # "outbound" (default) | "inbound" | "bidirectional" (alias "both") — an unreadable value drops the entry with a warning
    allowed_topics: [string] # Omitted or empty = every topic

mounts:                   # The virtual roots the crew uses (optional, VFS-90) — selects and validates, never restricts
  - /output               # a root a settings entry (or a --mount) must provide; refused in one line when nothing does
  - 01J9Z3K4M5N6P7Q8R9S0T1V2W3|/data   # a root pinned to ONE settings entry by its id, when several entries declare it

rag:                      # Crew-level RAG configuration (optional)
  provider: string        # Removed: the load warns and ignores it — the store is the host's, Orkeon:Rag:Provider
  collections:
    <collection_name>:
      sources: [string]   # Files, globs, directories or URLs — relative to the crew's folder unless absolute — ingested when the crew is created
      chunking:           # Omitted → the ingestion pipeline's defaults
        strategy: string  # default: "recursive"
        max_tokens: int   # default: 512 — tokens per chunk (×4 characters)
        overlap: int      # default: 64 — token overlap between chunks
  defaults:
    profile: string       # The retrieval profile of every knowledge attachment that names none (see Knowledge & RAG configuration)

agents:
  <agent_id>:             # Key = unique agent identifier
    role: string          # Agent role (defaults to the agent key)
    goal: string          # Agent's personal goal (required)
    backstory: string     # Context and expertise (multi-line recommended)
    tools: [string]       # Tool names registered in IToolRegistry (an unknown name fails the load under Orkeon:CrewFactory:StrictTools, the runners' default)
    allowDelegation: bool # default: true — allows delegation to other agents
    maxIter: int          # default: 20 — the turns the agent may take on a task (C# and .ork.ts too). 0 or less fails the load
    maxRpm: int           # default: none — at most N model requests per minute for this agent; one more waits its turn. The host's RateLimiting:AgentRequestsPerMinute bounds it too, the stricter winning. 0 or less fails the load
    maxExecutionTime: int # default: none — the seconds a task may take on this agent (alias max_execution_time); 0 or less, or above 4294967 (about 49.7 days), fails the load, and so does a limit on the hierarchical manager
    working_root: string  # default: none — a virtual path (alias workingRoot, C# AgentBuilder.WorkingRoot) under a mount that confines this agent's file tools and shell_command to it; refused at run start, naming the root, when no mount holds it
    verbose: bool         # default: false — detailed logs for this agent (tool calls, tool results, model responses, at Information, category Orkeon.Verbose)
    llm:                  # Omitted (and no crew llm:) → the runner's Llm settings section
      profile: string     # Host LLM profile (Llm:Profiles:<name>): the provider this agent runs on; "default" = the Llm section
      model: string       # LLM model id — omitted → the profile's own model
      temperature: float  # Sent whatever its value — omitted = the profile's, else none is sent and the model applies its own (GAP-36)
      maxTokens: int      # Output token pin, positive — omitted = the model's documented maximum (LLM-10); 0 or less fails the load
      topP: float         # Nucleus sampling, sent whatever its value — omitted = the profile's, else none is sent (Mistral writes 1)
      thinking:           # Reasoning control (capability-gated per provider)
        enabled: bool
        effort: string    # "low" | "medium" | "high" | "max" (provider-dependent)
        budget_tokens: int # Honoured by Qwen only; other providers warn
      responseFormat: string # "text" (= provider default) | "json_object" | "json_schema" — any other value is forwarded as-is with a warning
      responseSchema:     # With responseFormat: json_schema (a schema alone implies json_schema)
        name: string      # default: "response"
        schema: string    # The JSON Schema as a JSON string, e.g. '{"type":"object"}'
        strict: bool      # default: true
      cache:              # Explicit prompt caching (Anthropic); off unless a breakpoint is asked
        system: bool      # default: false
        tools: bool       # default: false
        ttl: string       # e.g. "1h"; omitted = vendor default
    guardrails:           # Operational rules injected into the agent's system prompt (optional)
      preset: string      # "analysis" | "strict" | "creative" — any other name fails the load
      header: string      # Section header — used only without a preset (every preset brings its own)
      rules: [string]     # Global numbered rules
      toolRules:          # Rules rendered only when the agent has the tool; a tool name ignores case, and one written under two keys fails the load
        <tool_name>: [string]
    knowledge:            # Knowledge (RAG) collections attached to the agent (optional); an unknown key of an entry is refused, a malformed value stays a warning
      - string            # Short form: collection name with default options
      - collection: string       # Long form (required key)
        top_k: int               # default: 5 — chunks retained per query
        min_score: float         # Minimum relevance score in [0, 1]
        profile: string          # This attachment's retrieval profile — else rag.defaults.profile, else Orkeon:Rag:Profile (see Knowledge & RAG configuration)
        max_context_tokens: int  # default: 2000 — cap on injected context tokens

tasks:
  <task_id>:              # Key = unique task identifier
    description: string   # Detailed task description (required)
    expectedOutput: string # Expected result format/content (required)
    agent: string         # Key of the agent assigned to the task (a key matching no agent fails the load, listing the crew's agents)
    dependencies: [string] # Keys of prerequisite tasks (guarantees ordering; an unknown key fails the load, listing the crew's tasks; so does a cycle)
    asyncExecution: bool  # default: false — sequential: runs alongside the next tasks, a dependant waits for it; parallel: no effect of its own; other modes: true fails the load
    humanInput: bool      # default: false — requests human intervention
    context: {key: value} # Additional context data
    tools: [string]       # Tools ADDED to the agent's own for this task only (never replacing them) — resolved like an agent's tools: an unknown name fails the load under StrictTools
    deliverable:          # Output-file contract (ignored without a path)
      path: string        # Virtual path, e.g. "/output/report.md"
      source: string      # "tool_call" (default — the agent is told to write the path with file_write) | "final_message" (the framework writes the final answer) | "structured_output" (JSON written by the framework, constrained by the schema as json_schema where the provider declares it, as a GBNF grammar where Llm:Grammar is on, and always parse-checked) | "none" — any other value fails the load
      format: string      # "markdown" (default) | "json" | "text"
      sanitize: bool      # default: true — strips trailing template tokens (final_message)
      schema_path: string # Virtual path of a JSON Schema file (structured_output needs it or schema_inline)
      schema_inline: string # The JSON Schema as a JSON string — alternative to schema_path
    llm_override:         # Task-level LLM override (cascade crew → agent → task)
      profile: string     # This task runs on another host LLM profile than its agent's
      response_format: string  # Same values as llm.responseFormat
      response_schema: {name, schema, strict} # schema is a JSON string
      temperature: float
      max_tokens: int     # Positive; omitted = the inherited cap, 0 or less fails the load naming the task
      top_p: float
      thinking: {enabled, effort, budget_tokens}
      cache: {system, tools, ttl}  # Same shape as the agent's cache: — replaces the agent's block whole; a block with no breakpoint turns the agent's cache off for this task
    guardrails:           # Task-level guardrails, same shape as the agent block (optional)
      preset: string      # "analysis" | "strict" | "creative" — any other name fails the load
      header: string
      rules: [string]
      toolRules:
        <tool_name>: [string]
    decision:             # Makes the task a decision task: no agent, answered by the decision model — see Decision tasks and conditions below. Together with agent: refused at load
      questions:          # Required, at least one; the key is the question's name
        <question>:
          choice: {<option>: string}  # One option among up to 255, each with its description
          score: [string]             # An ordered scale of up to 10 levels
          noul: string                # A yes-no question: the statement
          instructions: string        # Optional, for choice and score
      minConfidence: float   # default: 0.5, in [0, 1] — under it, an option condition selects nothing
      state: [string]        # Keys of the tasks whose output the model judges; default: the outputs of the dependencies that ran
      longContext: bool      # default: false — asks for the model's long context
      fallback: {<question>: string}  # Answers used without a request when the decision circuit is off
    when:                 # Runs the task only when a decision task answered as written (one decision task per when:)
      <task>.<question>: [string] | ">= 0.7"  # An option list (choice), or a threshold (yes-no, score)
      <task>.<question>.<option>: ">= 0.25"   # The probability of one option of a choice
      <task>: otherwise                       # Alone: holds when no sibling condition on that task holds
```

Validation happens at load time (`CrewDefinitionValidator`): a crew needs a `name`, a `goal`, at least one agent (each
with a goal) and at least one task (each with a `description` and an `expectedOutput`); circular
`dependencies`, a `mounts:` item listed twice or two ids selecting the same root fail the load. So
does a `circuitBreaker:` key, at the root or on a task: the block was removed (see "Removed: `circuitBreaker`" below), and the load names `graphConfig`
instead of ignoring it. And so does a task's `asyncExecution: true` outside `process: sequential` and
`process: parallel`: the four other modes order their tasks themselves, and the load names every task
that asks for it rather than ignore the flag (see [Asynchronous tasks](../orchestration/process-types.md#asynchronous-tasks-asyncexecution)).
A `maxRpm` of 0 or less — on the crew or on an agent — and a `maxIter` of 0 or less fail the load too,
naming the crew or the agent: leave `maxRpm:` out for no limit, `maxIter:` out for the default (20). They
used to be replaced, by 10 and 15, without a word. A `max_tokens` of 0 or less — on the crew's `llm`, an agent's
`llm` or a task's `llm_override` — fails the load too, naming the crew, the agent or the task: omit the key to keep
the inherited cap. The host's own caps on model calls — which bound an
agent's `maxRpm`, the stricter winning — are the settings of
[`RateLimiting`](../reference/configuration.md#ratelimiting).
An agent's `maxExecutionTime` (or `max_execution_time`) is whole seconds: 0 or less, a decimal, a value above the
ceiling (4294967 s, about 49.7 days) and a limit on the `managerAgent` of a hierarchical crew fail the load, naming
the agent. A task past its limit ends `ExecutionTimeExceeded` at the latest at the end of the call in progress;
time spent waiting for a human does not count.

A message names an agent or a task by its key — `Agent 'researcher' must have a goal.` —, a
configuration built in code by its identifier.

## Decision tasks and conditions

A task that carries a `decision:` block is a **decision task**: it has no agent, no LLM call and
writes no text. At run time it sends its state and all its questions to the decision model in one
request (see [Decision models](./decision-models.md#decision-tasks-and-conditional-fan-out)), and
its output is the answers. A task that carries a `when:` block runs only when a decision task
answered as written, and is skipped otherwise.

```yaml
agents:
  reader: { role: Reader, goal: Read tickets }
  engineer: { role: Engineer, goal: Fix defects }
  finance: { role: Finance, goal: Handle refunds }
tasks:
  ticket:
    description: Read the support ticket
    expectedOutput: A summary of the ticket
    agent: reader
  triage:
    description: Sort the ticket
    expectedOutput: The category and whether a refund is asked
    dependencies: [ticket]
    decision:
      questions:
        category:
          choice:
            bug: a defect in the product
            feature: a request for a new capability
            billing: a question about an invoice
        refund_requested:
          noul: the customer asks for a refund
      minConfidence: 0.6
  fix_bug:
    description: Fix the defect or plan the feature
    expectedOutput: A fix or a plan
    agent: engineer
    when:
      triage.category: [bug, feature]
  refund:
    description: Handle the refund
    expectedOutput: A refund decision
    agent: finance
    when:
      triage.category: [billing]
      triage.refund_requested: ">= 0.7"
  manual_triage:
    description: Route the ticket to a person
    expectedOutput: A hand-over note
    agent: reader
    when:
      triage: otherwise
  summary:
    description: Summarise what was done
    expectedOutput: A short report
    agent: reader
    dependencies: [fix_bug, refund, manual_triage]
```

**The `decision:` block.**

- `questions` is required and holds at least one question. A question declares exactly one kind:
  `choice` (a map from option to description, up to 255 options), `score` (an ordered list of up to
  10 levels) or `noul` (the statement of a yes-no question, Jev's name for it). `instructions` is
  optional on a choice and a score. Question names are unique.
- `minConfidence` is the confidence floor, a number in [0, 1]; it defaults to 0.5. `min_confidence`
  is read the same way, as every key of the file.
- `state` lists the keys of the tasks whose output the model judges; without it the model reads the
  outputs of the task's dependencies that ran, in order. A listed task is added to the task's
  `dependencies` when it is not there.
- `longContext` (default `false`) asks for the model's long context; it is never requested on its
  own.
- `fallback` maps a question to the answer used when the decision circuit is off: an option of a
  choice, a level of a score, `yes` or `no` of a yes-no (quote `"no"`, which YAML reads as a
  boolean). A question without a fallback answer selects no option. See
  [the fallback](./decision-models.md#the-decision-circuit-and-the-fallback).
- A decision task still needs a `description` and an `expectedOutput` like any task, and the crew
  still needs one agent, even when no task uses it.

**The `when:` block.** A `when:` names one decision task; every clause is `<task>`, `<task>.<question>`
or `<task>.<question>.<option>`, where `<task>` is the key of the decision task. Several clauses are
written in one block and **all must hold**. The grammar:

| Clause | Holds when |
|---|---|
| `<task>.<question>: [a, b]` | The question is a choice, the chosen option is one of the list and the confidence is at or above `minConfidence`. |
| `<task>.<question>: ">= 0.7"` | The question is a yes-no (its probability of a yes) or a score (the model's score) and the comparison holds. The floor does not apply. |
| `<task>.<question>.<option>: ">= 0.25"` | The probability of that option of a choice satisfies the comparison, even when the option is not the chosen one. The floor does not apply. |
| `<task>: otherwise` | No sibling condition on that decision task holds, including when the confidence is under the floor. |

A threshold is an operator (`>=`, `>`, `<=`, `<`) and a number in [0, 1], written as a quoted string.
Several tasks whose conditions hold for the same decision all run (a fan-out); a task that depends on
several branches runs when at least one of them ran. The decision task named by a `when:` is added to
the task's `dependencies` when it is not there. A task whose condition does not hold, or whose every
dependency was skipped by a condition, is skipped: it leaves no output, and the crew still succeeds.

**Where it runs.** Decision tasks and `when:` are supported in `process: sequential`, `parallel` and
`graph`. The other processes refuse them at load (`Task 'triage' declares a decision or a condition,
which the Hierarchical process does not support.`).

**What is refused at load.** A task is named by its declared key, the key of the YAML map, in every
message, in the form `Task '<key>' <part> …` where the part is `decision`,
`decision.questions.<name>`, `decision.fallback` or `when`. The load fails on:

- a task with both an `agent:` and a `decision:`, a decision with no question, a choice of more than
  255 options, a score of more than 10 levels, a `minConfidence` outside [0, 1] or that is not a
  number, a `longContext` that is not a boolean;
- a question that declares more than one of `choice`, `score` and `noul`;
- a fallback that names an unknown question, or an answer the question does not have;
- a `when:` on a task that is not a decision task, or that names an unknown question or option, or
  with a threshold outside [0, 1] or not written as an operator and a number;
- `otherwise` written beside other clauses, a second `otherwise` task on one decision task, an
  `otherwise` with no sibling condition on that decision task;
- an option clause with a list on an option (`triage.category.bug: [bug]`), which takes a threshold
  string only;
- a decision or a `when:` in a process other than the three above.

With the decision circuit off, the run is also refused before its first task when a decision task has
neither a `fallback` nor an `otherwise` task.

## Guardrails configuration

Guardrails are operational rules rendered into the executing agent's **system prompt**. They can be
declared on an **agent** (apply to every task the agent runs) and/or on a **task** (apply only to that
task). When both are present, **both apply — the agent's guardrails render first, then the task's** as a
separate section. `preset` (`analysis` / `strict` / `creative`) seeds a base set of rules; explicit
`rules`/`toolRules` are merged on top, and `toolRules` for a given tool are only emitted when the
executing agent actually holds that tool for the task — its own tools plus the task's `tools:`. A `preset` header takes precedence over a custom `header`.
A `preset` other than these three — a typo — fails the load, naming the agent or the task by its key and
the three presets; so does a tool written under two keys of `toolRules` (`file_write` and `File_Write`):
a tool name ignores case.

## Knowledge & RAG configuration

Two complementary blocks (RAG-03/C4). The crew-level `rag:` block declares the **collections**
(ingestion sources, chunking) and crew-wide defaults. The agent-level `knowledge:` block
**attaches** collections to an agent with its retrieval options. At execution-context assembly
time each attached collection is queried with the task input through the retrieval half of its
**profile** pipeline — the attachment's `profile`, else `rag.defaults.profile`, else
`Orkeon:Rag:Profile` — and the results are injected into the agent's user prompt as numbered,
cited excerpts (`IKnowledgeContextAugmenter`, honouring `top_k`, `min_score` and
`max_context_tokens`). Both halves need the RAG subsystem (`AddOrkeonRag(configuration)`), which
every runner host registers (`orkeon run` in all its forms, `orkeon-host`): when the crew is
**created** (`CrewFactory`), the declared collections are ingested incrementally
(`IRagCollectionsBootstrapper` — an unchanged source is not re-embedded;
`CrewFactoryOptions.PrepareRagCollections = false` skips it, as `--validate` does). On a host
without the subsystem, a declared `rag:` block and each agent's `knowledge:` log a warning and
inject nothing. Parsing the YAML itself never triggers ingestion. The document store is the
host's (`Orkeon:Rag:Provider`, see [RAG pipeline](./rag-pipeline.md)); the former `rag.provider`
key is gone and is refused at load, with the sentence that points to `Orkeon:Rag:Provider`.

What a `sources` entry means (GAP-27): a path that does not start with `/` is relative to the
crew's folder — `/crew` under `orkeon run crew.yaml` or a crew directory, so
`./data/faq.md` next to the crew file is `/crew/data/faq.md` — and an absolute one is a virtual
path (`/kb/faq.md`, a mount of yours). A glob (`*`, `**`, `?`) is expanded through the VFS the
way `rag_ingest`, `orkeon rag ingest` and `rag.ingest` expand it; a directory stands for every
file below it; an `http(s)://` address and a plain file reach the loaders as written. A glob or a
directory that yields no file, a relative source of a crew read from a string (no folder to be
relative to) and a glob outside every mount are each a load warning naming the collection —
the crew still loads; a file that is not there is reported by the ingestion. A file two entries
name is ingested once.

Short form — attach collections with default options:

```yaml
rag:
  collections:
    produits:
      sources: ["./data/catalogue/**/*.pdf", "./data/faq.md"]

agents:
  support:
    role: "Customer support agent"
    goal: "Answer product questions"
    knowledge: [produits, procedures]
```

Long form — per-collection retrieval options (mixable with the short form in the same list):

```yaml
rag:
  collections:
    procedures:
      sources: ["./docs/procedures/"]
      chunking: { strategy: recursive, max_tokens: 512, overlap: 64 }
  defaults: { profile: balanced }

agents:
  expert:
    role: "Domain expert"
    goal: "Provide sourced answers"
    knowledge:
      - collection: procedures
        profile: quality
        top_k: 8
        min_score: 0.35
        max_context_tokens: 1500
```

Keys accept snake_case (the form used above) and camelCase. A malformed `knowledge` entry (missing
`collection`, non-numeric `top_k`, …) is skipped or downgraded with a warning — it never
crashes the loader. The fluent equivalent is `AgentBuilder.WithKnowledge("produits")` /
`WithKnowledge("produits", opts => { opts.TopK = 8; opts.Profile = "quality"; })` (cumulative).

## Graph configuration

When `process: "graph"` is used, an additional `graphConfig` block configures the state graph engine:

```yaml
graphConfig:
  maxRetryCycles: int           # default: 2 — retry cycles for failed tasks
  circuitBreakerPreset: string  # "strict" (default) | "permissive" | "default"
  maxTransitions: int           # Default: computed, 2 × visits + 1
  maxStateVisits: int           # Task-attempt cap — default: computed, tasks × (1 + maxRetryCycles)
  maxTotalDurationSeconds: int  # Total duration in seconds (overrides the preset)
```

`graphConfig` is the only circuit-breaker setting a crew carries (`CircuitBreakerPolicyFactory.ResolveGraph`).
The graph engine enforces three limits only — transitions, visits of one state, total duration
(`GraphRunner`); a state timeout or a degraded mode has no effect on a graph run.

## Removed: `circuitBreaker`

The `circuitBreaker:` block — at the crew root and on a task — is gone, and a crew that still
writes one is **refused at load** with a message naming `graphConfig`. The task block configured a
task state machine that no execution path ran, along with three guard limits (`maxRetries`,
`maxToolCallsPerRound`, `maxValidationRetries`) read by nothing. The crew block was read by the
Graph mode alone, where `graphConfig` carries the same preset and the same three effective limits.

What bounds a task is its agent loop: the agent's `maxIter`, the stop after three identical
consecutive tool errors, and the output-validation retries. What bounds a graph run is
`graphConfig` (above). The generic state-machine engine and `CircuitBreakerPolicy` stay — the
Graph mode, `orkeon forge` and the corrective RAG graph use them (see [FSM](../orchestration/fsm.md)).

## Autonomous budget

`budget:` bounds the run of a `process: autonomous` crew (#218):

```yaml
process: autonomous
budget:
  tokens: 400000          # running sum of prompt + completion of every call; none by default
  wallTimeSeconds: 1800   # none by default
  toolCalls: 80           # tasks handed out; 50 by default
  delegationDepth: 2      # delegations in the run; 4 by default
  spawnedAgents: 3        # agents spawned through spawn_agent; 10 by default
```

Every key is optional, and a key left out takes its default: a crew without `budget:` has no token
cap and no wall time, so an ordinary crew is never cut short by a figure it did not choose. A limit of
0 or less, or a `budget:` on a crew of another process — the only one that spends it is
`autonomous` — fails the load, naming the crew. The export writes the block back, the wall time in
whole seconds. The same budget is `CrewBuilder.Budget(new CrewBudget { ... })` in C# and
`crewBuilder().budget({ tokens, wallTime, toolCalls, delegationDepth, spawnedAgents })` in `.ork.ts`
(see the [Autonomous orchestration guide](../orchestration/autonomous.md)).

## Export

`YamlCrewExporter` — which `AddOrkeonYaml()` registers — writes a `CrewConfiguration` back to YAML:
`ExportToString`, `ExportToFileAsync`, or `ExportToDirectoryAsync` for the flat `crew.yaml` +
`agents.yaml` + `tasks.yaml` triplet. Loaded again, the file gives the same configuration — every key
the loader reads — and its own export is the same text, byte for byte. Keys are written in camelCase,
and a key nothing sets is not written at all.

- **Under the author's keys.** A crew loaded from YAML keeps the key of each agent and task
  (`researcher`, `collect`): the export writes each one under it, and every reference — `agent:`,
  `dependencies:`, `managerAgent:` — by it. A configuration built in C# or by a `.ork.ts` script has no
  keys: its agents and tasks are written under their identifiers. Two entries under one name, or a
  reference that names no entry, fail the export with an `InvalidOperationException` that names them:
  the loader would refuse the file.
- **What the loader made of your file, not your file.** The export writes what the configuration
  carries: a guardrails `preset:` comes back as the `header`, `rules` and `toolRules` it brought, the
  crew's `llm:` as the block each agent got from it, under each agent, and `anchors:` as the text they
  stood for. A knowledge attachment that only names its collection is written in the short form, any
  other in the long form.
- **A relative source follows the file.** A `rag:` source is written as it was, so `./data/faq.md` is
  read against the folder the exported file is loaded from (see
  [Knowledge & RAG configuration](#knowledge--rag-configuration)): export next to the data, or write
  absolute sources. The folder the crew was read from is not a key, and is not written; neither is
  anything the schema above has no key for.

## YAML models

The YAML models include:
All live in `Orkeon.Infrastructure.Configuration` (`src/core/Orkeon.Infrastructure/Configuration/Yaml/YamlConfigModels.cs`); `YamlCrewMapper` turns them into the Domain `CrewConfiguration` and `CrewDefinitionValidator` checks it.

- `CrewYamlConfig` (complete crew definition) and `CrewSettingsYamlConfig` (the crew settings file of the multi-file layouts — `crew.yaml` or `config.yaml`)
- `AgentYamlConfig` (role, goal, backstory, tools, limits)
- `TaskYamlConfig` (description, expected output, dependencies, tools, deliverable, LLM override, guardrails)
- `LlmYamlConfig` (profile, model, temperature, max tokens, topP, thinking, responseFormat/responseSchema, cache) with `ThinkingYamlConfig`, `ResponseSchemaYamlConfig`, `CacheYamlConfig`
- `LlmOverrideYamlConfig` (the task-level `llm_override:` block)
- `DeliverableYamlConfig` (the task-level `deliverable:` block)
- `GuardrailsYamlConfig` (agent- and task-level `guardrails:`)
- `LinkYamlConfig` (the crew-level `links:` ACL)
- `CrewYamlConfig.Mounts` / `CrewSettingsYamlConfig.Mounts` (the crew-level `mounts:` block — `/root` or `<ulid>|/root` items) → `CrewConfiguration.Mounts` (`MountReference`, VFS-90)
- `GraphYamlConfig` (maxRetryCycles, circuitBreakerPreset, overrides)
- `RagYamlConfig` (collections + sources/chunking, defaults — with `RagCollectionYamlConfig`, `RagChunkingYamlConfig`, `RagDefaultsYamlConfig`) → `RagCrewConfig`
- `AgentYamlConfig.Knowledge` (short/long form entries) → `KnowledgeAttachment`

There is no framework-level "predefined configuration" object: a host composes its own settings through `AddOrkeonInfrastructure` / `AddOrkeonApplication` and its `appsettings.json`.

---

> **See also**: [YAML and Builders](../getting-started/yaml-and-builders.md) · [FSM orchestration](../orchestration/fsm.md) · [Graph orchestration](../orchestration/graph.md) · [Autonomous orchestration](../orchestration/autonomous.md) · [Back to index](../INDEX.md)
