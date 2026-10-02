> 🇫🇷 [Version française](../fr/architecture/yaml-schema.md)

# YAML reference — Complete schema

This document is the **single source of truth** for the Orkeon YAML schema. The specialized documents (FSM, Graph, Autonomous) refer back here for the schema.

## Complete configuration schema

The YAML structure follows this schema. **Key naming**: the loader (`YamlDotNetSerializer`)
matches keys in camelCase (the canonical form, the one `YamlCrewExporter` writes) and falls back
to snake_case for the same property — `expectedOutput` and `expected_output`, `llm_override` and
`llmOverride` are equivalent everywhere. Keys that match no property are **ignored silently**
(a misspelt key reads as absent), except inside `knowledge:` entries, which warn. The same models
serve the single-file layout and the multi-file layouts (`crew.yaml` + `agents.yaml` +
`tasks.yaml`, or `config.yaml` + `agents/` + `tasks/`) described in
[YAML and Builders](../getting-started/yaml-and-builders.md); a top-level `anchors:` mapping of
named multi-line strings is expanded before parsing (`YamlAnchorPreprocessor`).

```yaml
# Complete CrewYamlConfig schema
name: string              # Crew identifier (required); scopes the crew's long-term memory
goal: string              # Goal (required)
process: string           # "sequential" (default) | "hierarchical" | "parallel" | "consensual" | "graph" | "autonomous" — case-insensitive; an unknown value fails the load
verbose: bool             # default: false
memory: bool              # default: false. true: the crew stores the result of each task and recalls the closest ones before each task (embedder required at kickoff); false: nothing is stored or recalled
memoryProvider: string    # Requires memory: true (refused otherwise). "InMemory" | "Redis" | "Sqlite" | "ChromaDb" | "Pinecone" | "LanceDb" — case-insensitive (aliases "in-memory", "chroma", "lance"); unknown → in-memory with a warning. The TYPE only: the connection comes from the host section (Orkeon:Redis, Orkeon:Sqlite, …). Unset: the host's default store (Memory:Provider)
planning: bool            # default: false. true: before the first task, a planner (the host's default profile) writes a step-by-step plan per task, which the task reads in its prompt, in every mode; it changes neither the order nor the agents
managerAgent: string      # Hierarchical: the manager (omitted → the first agent manages, warning). Consensual: the arbiter of the ManagerDecision fallback
graphConfig: {…}          # Graph mode settings, the crew's only circuit-breaker setting (see the dedicated section)

llm:                      # Crew-default LLM, merged FIELD BY FIELD under each agent's own llm: (same shape as agents.<id>.llm)
  profile: string         # Host LLM profile (Llm:Profiles:<name>) for every agent — unknown → the load fails, listing the host's profiles
  model: string
  temperature: float

links:                    # EventHub ACL (optional) — who this crew may talk to on the hub
  - to: string            # The target crew's name:, verbatim — or "client:<name>" for an external peer (e.g. Studio)
    direction: string     # "outbound" (default) | "inbound" | "bidirectional" (alias "both") — an unreadable value drops the entry with a warning
    allowed_topics: [string] # Omitted or empty = every topic

mounts:                   # The virtual roots the crew uses (optional, VFS-90) — selects and validates, never restricts
  - /output               # a root a settings entry (or a --mount) must provide; refused in one line when nothing does
  - 01J9Z3K4M5N6P7Q8R9S0T1V2W3|/data   # a root pinned to ONE settings entry by its id, when several entries declare it

rag:                      # Crew-level RAG configuration (optional)
  provider: string        # Recorded on RagCrewConfig, not consumed yet — the store is Orkeon:Rag:Provider (or the ambient memory provider)
  collections:
    <collection_name>:
      sources: [string]   # Ingestion sources (file globs or directories), ingested when the crew is created
      chunking:           # Omitted → the ingestion pipeline's defaults
        strategy: string  # default: "recursive"
        max_tokens: int   # default: 512 — tokens per chunk (×4 characters)
        overlap: int      # default: 64 — token overlap between chunks
  defaults:
    profile: string       # Recorded on RagCrewConfig, not consumed yet

agents:
  <agent_id>:             # Key = unique agent identifier
    role: string          # Agent role (defaults to the agent key)
    goal: string          # Agent's personal goal (required)
    backstory: string     # Context and expertise (multi-line recommended)
    tools: [string]       # Tool names registered in IToolRegistry (an unknown name fails the load under Orkeon:CrewFactory:StrictTools, the runners' default)
    allowDelegation: bool # default: true — allows delegation to other agents
    maxIter: int          # default: 20 — maximum iterations before timeout
    maxRpm: int           # default: 10 — requests per minute (rate limiting)
    verbose: bool         # default: false — detailed logs for this agent
    llm:                  # Omitted (and no crew llm:) → the runner's Llm settings section
      profile: string     # Host LLM profile (Llm:Profiles:<name>): the provider this agent runs on; "default" = the Llm section
      model: string       # LLM model id — omitted → the profile's own model
      temperature: float  # default: 0.7 when the block is present
      maxTokens: int      # Output token pin — omitted = the model's documented maximum (LLM-10)
      topP: float         # Nucleus sampling, default: 1.0
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
      preset: string      # "analysis" | "strict" | "creative"
      header: string      # Section header — used only without a preset (every preset brings its own)
      rules: [string]     # Global numbered rules
      toolRules:          # Rules rendered only when the agent has the tool
        <tool_name>: [string]
    knowledge:            # Knowledge (RAG) collections attached to the agent (optional)
      - string            # Short form: collection name with default options
      - collection: string       # Long form (required key)
        top_k: int               # default: 5 — chunks retained per query
        min_score: float         # Minimum relevance score in [0, 1]
        profile: string          # Recorded on KnowledgeAttachment, not consumed by the augmenter yet
        max_context_tokens: int  # default: 2000 — cap on injected context tokens

tasks:
  <task_id>:              # Key = unique task identifier
    description: string   # Detailed task description (required)
    expectedOutput: string # Expected result format/content (required)
    agent: string         # Key of the agent assigned to the task (a key matching no agent leaves the task unassigned)
    dependencies: [string] # Keys of prerequisite tasks (guarantees ordering; an unknown key is ignored; a cycle fails the load)
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
      max_tokens: int
      top_p: float
      thinking: {enabled, effort, budget_tokens}
    guardrails:           # Task-level guardrails, same shape as the agent block (optional)
      preset: string      # "analysis" | "strict" | "creative"
      header: string
      rules: [string]
      toolRules:
        <tool_name>: [string]
```

Validation happens at load time (`CrewDefinitionValidator`): a crew needs a `name`, a `goal`, at least one agent (each
with a goal) and at least one task (each with a `description` and an `expectedOutput`); circular
`dependencies`, a `mounts:` item listed twice or two ids selecting the same root fail the load. So
does a `circuitBreaker:` key, at the root or on a task: the block was removed (see "Removed: `circuitBreaker`" below), and the load names `graphConfig`
instead of ignoring it. And so does a task's `asyncExecution: true` outside `process: sequential` and
`process: parallel`: the four other modes order their tasks themselves, and the load names every task
that asks for it rather than ignore the flag (see [Asynchronous tasks](../orchestration/process-types.md#asynchronous-tasks-asyncexecution)).

## Guardrails configuration

Guardrails are operational rules rendered into the executing agent's **system prompt**. They can be
declared on an **agent** (apply to every task the agent runs) and/or on a **task** (apply only to that
task). When both are present, **both apply — the agent's guardrails render first, then the task's** as a
separate section. `preset` (`analysis` / `strict` / `creative`) seeds a base set of rules; explicit
`rules`/`toolRules` are merged on top, and `toolRules` for a given tool are only emitted when the
executing agent actually holds that tool for the task — its own tools plus the task's `tools:`. A `preset` header takes precedence over a custom `header`.

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
key is gone and draws a warning at load.

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

## Autonomous Budget configuration

> **⚠️ Not implemented in YAML.** There is **no `autonomousBudget` key** in the
> YAML schema today: the loader does not parse one, and no corresponding YAML
> model exists. With `process: "autonomous"`, `AgentExecutionBudget.Permissive`
> is always used (50 tool calls, depth 4, 15 min, 64 000 tokens, 10 spawns). The multi-dimensional budget is configurable **through the
> C# API only** — `AgentExecutionBudget.Strict` / `.Default` / `.Permissive`
> presets or custom values (see the
> [Autonomous orchestration guide](../orchestration/autonomous.md)):
>
> - **Strict**: MaxToolCalls=8, MaxDelegationDepth=1, MaxWallTime=2min, MaxTokens=8000, MaxSpawns=1
> - **Default**: MaxToolCalls=15, MaxDelegationDepth=2, MaxWallTime=5min, MaxTokens=16000, MaxSpawns=3
> - **Permissive**: MaxToolCalls=50, MaxDelegationDepth=4, MaxWallTime=15min, MaxTokens=64000, MaxSpawns=10

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
- `RagYamlConfig` (provider, collections + sources/chunking, defaults — with `RagCollectionYamlConfig`, `RagChunkingYamlConfig`, `RagDefaultsYamlConfig`) → `RagCrewConfig`
- `AgentYamlConfig.Knowledge` (short/long form entries) → `KnowledgeAttachment`

There is no framework-level "predefined configuration" object: a host composes its own settings through `AddOrkeonInfrastructure` / `AddOrkeonApplication` and its `appsettings.json`.

---

> **See also**: [YAML and Builders](../getting-started/yaml-and-builders.md) · [FSM orchestration](../orchestration/fsm.md) · [Graph orchestration](../orchestration/graph.md) · [Autonomous orchestration](../orchestration/autonomous.md) · [Back to index](../INDEX.md)
