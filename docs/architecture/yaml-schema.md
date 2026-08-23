> 🇫🇷 [Version française](../fr/architecture/yaml-schema.md)

# YAML reference — Complete schema

This document is the **single source of truth** for the Orkeon YAML schema. The specialized documents (FSM, Graph, Autonomous) refer back here for the schema.

## Complete configuration schema

The YAML structure follows this schema:

```yaml
# Complete CrewYamlConfig schema
name: string              # Crew identifier
goal: string              # Goal (required)
process: string           # "sequential" | "hierarchical" | "parallel" | "consensual" | "graph" | "autonomous"
verbose: bool             # default: false
memory: bool              # default: false
memoryProvider: string    # "InMemory" | "Redis" | "Sqlite" | "ChromaDb" | "Pinecone" | "LanceDb"
planning: bool            # default: false
managerAgent: string      # Required if process = "hierarchical"

llm:                      # Crew-default LLM, applied to agents without their own (same shape as agents.<id>.llm)
  model: string
  temperature: float

links:                    # EventHub ACL (optional) — who may talk to whom on the hub
  - to: string            # "agent:<id>" | "crew:<id>" | "topic:<name>" | "client:<name>"
    direction: string     # "send" | "receive" | "both"
    allowed_topics: [string]

rag:                      # Crew-level RAG configuration (optional)
  provider: string        # Memory/vector store for the collections ("InMemory" | "Redis" | "Sqlite" | "ChromaDb" | "Pinecone" | "LanceDb")
  collections:
    <collection_name>:
      sources: [string]   # Ingestion sources (file globs or directories), resolved at kickoff
      chunking:
        strategy: string  # default: "recursive"
        max_tokens: int   # default: 512 — tokens per chunk
        overlap: int      # default: 64 — token overlap between chunks
  defaults:
    profile: string       # Default query profile for knowledge attachments without one

agents:
  <agent_id>:             # Key = unique agent identifier
    role: string          # Agent role (required)
    goal: string          # Agent's personal goal (required)
    backstory: string     # Context and expertise (multi-line recommended)
    tools: [string]       # Tool names registered in IToolRegistry
    allowDelegation: bool # default: true — allows delegation to other agents
    maxIter: int          # default: 20 — maximum iterations before timeout
    maxRpm: int           # default: 10 — requests per minute (rate limiting)
    verbose: bool         # default: false — detailed logs for this agent
    llm:
      model: string       # LLM model ("gpt-4", "claude-3-opus", etc.)
      temperature: float  # Creativity (0.0-1.0)
      maxTokens: int      # Output token limit
      topP: float         # Nucleus sampling
      thinking:           # Reasoning control (capability-gated per provider)
        enabled: bool
        effort: string    # "low" | "medium" | "high"
        budget_tokens: int
      responseFormat: string # "json_object" | "json_schema" (capability-gated)
      responseSchema:     # With responseFormat: json_schema
        name: string
        schema: {…}       # Inline JSON Schema
        strict: bool
      cache:              # Explicit prompt caching (Anthropic)
        system: bool
        tools: bool
        ttl: string
    guardrails:           # Operational rules injected into the agent's system prompt (optional)
      preset: string      # "analysis" | "strict" | "creative"
      header: string      # Section header (overrides the preset header)
      rules: [string]     # Global numbered rules
      toolRules:          # Rules rendered only when the agent has the tool
        <tool_name>: [string]
    knowledge:            # Knowledge (RAG) collections attached to the agent (optional)
      - string            # Short form: collection name with default options
      - collection: string       # Long form (required key)
        top_k: int               # default: 5 — chunks retained per query
        min_score: float         # Minimum relevance score in [0, 1]
        profile: string          # Query profile ("fast" | "balanced" | "quality", free-form)
        max_context_tokens: int  # Cap on injected context tokens

tasks:
  <task_id>:              # Key = unique task identifier
    description: string   # Detailed task description (required)
    expectedOutput: string # Expected result format/content (required)
    agent: string         # ID of the agent assigned to the task
    dependencies: [string] # IDs of prerequisite tasks (guarantees ordering)
    asyncExecution: bool  # default: false — asynchronous execution
    humanInput: bool      # default: false — requests human intervention
    context: {key: value} # Additional context data
    tools: [string]       # Task-scoped tool names (added to the agent's for this task)
    deliverable:          # The framework writes the output file (agent never touches the disk)
      path: string        # Virtual path, e.g. "/output/report.md"
      source: string      # "final" (default) | "raw"
      format: string      # "text" | "json" | "markdown"
      sanitize: bool
      schema_path: string # JSON Schema file validating a JSON deliverable
      schema_inline: {…}  # Inline schema alternative
    llm_override:         # Task-level LLM override (cascade crew → agent → task)
      response_format: string
      response_schema: {name, schema, strict}
      temperature: float
      max_tokens: int
      top_p: float
      thinking: {enabled, effort, budget_tokens}
    circuitBreaker:       # FSM / circuit breaker configuration (optional)
      preset: string      # "strict" | "permissive" | "default"
      maxTransitions: int # Max transitions before trip
      stateTimeoutSeconds: int  # Per-state timeout (seconds)
      maxStateVisits: int       # Max visits of the same state (cycles)
      maxTotalDurationSeconds: int # Max total duration (seconds)
      useDegradedMode: bool     # true = Degraded, false = exception
      maxRetries: int           # Retries after failure
      maxToolCallsPerRound: int # Max tool calls per round
      maxValidationRetries: int # Max validation loops
    guardrails:           # Task-level guardrails, same shape as the agent block (optional)
      preset: string      # "analysis" | "strict" | "creative"
      header: string
      rules: [string]
      toolRules:
        <tool_name>: [string]
```

The `circuitBreaker` block can also be used at the root level of the YAML (default for all tasks).

## Guardrails configuration

Guardrails are operational rules rendered into the executing agent's **system prompt**. They can be
declared on an **agent** (apply to every task the agent runs) and/or on a **task** (apply only to that
task). When both are present, **both apply — the agent's guardrails render first, then the task's** as a
separate section. `preset` (`analysis` / `strict` / `creative`) seeds a base set of rules; explicit
`rules`/`toolRules` are merged on top, and `toolRules` for a given tool are only emitted when the
executing agent actually holds that tool. A `preset` header takes precedence over a custom `header`.

## Knowledge & RAG configuration

Two complementary blocks (RAG-03/C4). The crew-level `rag:` block declares the **collections**
(provider, ingestion sources, chunking, crew-wide defaults). The agent-level `knowledge:` block
**attaches** collections to an agent with its retrieval options. At execution-context assembly
time the attached collections are queried with the task input and the results are injected into
the agent's prompts (prompt wiring ships in a later lot; parsing is fully functional today —
no ingestion is triggered at load time).

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
  provider: Sqlite
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

Keys accept snake_case (canonical) and camelCase. A malformed `knowledge` entry (missing
`collection`, non-numeric `top_k`, …) is skipped or downgraded with a warning — it never
crashes the loader. The fluent equivalent is `AgentBuilder.WithKnowledge("produits")` /
`WithKnowledge("produits", opts => { opts.TopK = 8; opts.Profile = "quality"; })` (cumulative).

## Graph configuration

When `process: "graph"` is used, an additional `graphConfig` block configures the state graph engine:

```yaml
graphConfig:
  maxRetryCycles: int           # default: 2 — retry cycles for failed tasks
  circuitBreakerPreset: string  # "strict" | "permissive" | "default"
  maxTransitions: int           # Overrides the preset
  maxStateVisits: int           # Cycle detection (overrides the preset)
  maxTotalDurationSeconds: int  # Total duration in seconds (overrides the preset)
```

## Circuit Breaker configuration

The `circuitBreaker` block with all available parameters:

```yaml
circuitBreaker:
  preset: string                # "strict" | "permissive" | "default"
  maxTransitions: int           # Max transitions before trip
  stateTimeoutSeconds: int      # Per-state timeout (seconds)
  maxStateVisits: int           # Max visits of the same state (cycles)
  maxTotalDurationSeconds: int  # Max total duration (seconds)
  useDegradedMode: bool         # true = Degraded, false = exception
  maxRetries: int               # Retries after failure
  maxToolCallsPerRound: int     # Max tool calls per round
  maxValidationRetries: int     # Max validation loops
```

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
- `CrewYamlConfig` (complete crew definition) and `CrewSettingsYamlConfig` (the multi-file `crew.yaml` variant)
- `AgentYamlConfig` (role, goal, backstory, tools, limits)
- `TaskYamlConfig` (description, expected output, dependencies, tools, deliverable, circuit breaker)
- `LlmYamlConfig` (model, temperature, max tokens, topP, thinking, responseFormat/responseSchema, cache) with `ThinkingYamlConfig`, `ResponseSchemaYamlConfig`, `CacheYamlConfig`
- `LlmOverrideYamlConfig` (the task-level `llm_override:` block)
- `DeliverableYamlConfig` (the task-level `deliverable:` block)
- `GuardrailsYamlConfig` (agent- and task-level `guardrails:`)
- `LinkYamlConfig` (the crew-level `links:` ACL)
- `CircuitBreakerYamlConfig` (preset, thresholds, guards)
- `GraphYamlConfig` (maxRetryCycles, circuitBreakerPreset, overrides)
- `RagYamlConfig` (provider, collections + sources/chunking, defaults — with `RagCollectionYamlConfig`, `RagChunkingYamlConfig`, `RagDefaultsYamlConfig`) → `RagCrewConfig`
- `AgentYamlConfig.Knowledge` (short/long form entries) → `KnowledgeAttachment`

Predefined configurations are available via `OrkeonConfig`: `Default`, `Development` (debug enabled, InMemory), `Production` (debug disabled, Redis).

---

> **See also**: [YAML and Builders](../getting-started/yaml-and-builders.md) · [FSM orchestration](../orchestration/fsm.md) · [Graph orchestration](../orchestration/graph.md) · [Autonomous orchestration](../orchestration/autonomous.md) · [Back to index](../INDEX.md)
