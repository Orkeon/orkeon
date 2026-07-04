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

tasks:
  <task_id>:              # Key = unique task identifier
    description: string   # Detailed task description (required)
    expectedOutput: string # Expected result format/content (required)
    agent: string         # ID of the agent assigned to the task
    dependencies: [string] # IDs of prerequisite tasks (guarantees ordering)
    asyncExecution: bool  # default: false — asynchronous execution
    humanInput: bool      # default: false — requests human intervention
    context: {key: value} # Additional context data
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
```

The `circuitBreaker` block can also be used at the root level of the YAML (default for all tasks).

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

When `process: "autonomous"` is used, an optional `autonomousBudget` block configures the multi-dimensional execution budget:

```yaml
autonomousBudget:              # ← optional, defaults if absent
  maxToolCalls: int            # Max number of tool calls (default: 15)
  maxDelegationDepth: int      # Max recursive delegation depth (default: 2)
  maxWallTime: string          # Maximum wall-clock time in HH:MM:SS format (default: "00:05:00")
  maxTokensConsumed: int       # Total tokens — prompt + completion (default: 16000)
  maxSpawnedAgents: int        # Max number of spawned sub-agents (default: 3)
  preset: string               # "strict" | "default" | "permissive" (overrides the values above)
```

**Predefined presets**:

- **strict**: MaxToolCalls=8, MaxDelegationDepth=1, MaxWallTime=2min, MaxTokens=8000, MaxSpawns=1
- **default**: MaxToolCalls=15, MaxDelegationDepth=2, MaxWallTime=5min, MaxTokens=16000, MaxSpawns=3
- **permissive**: MaxToolCalls=50, MaxDelegationDepth=4, MaxWallTime=15min, MaxTokens=64000, MaxSpawns=10

> **Note**: YAML parsing of `autonomousBudget` is not yet implemented in the loader. When this block is absent, `AgentExecutionBudget.Default` is used. The presets are functional through the C# API (`AgentExecutionBudget.Strict`, `.Default`, `.Permissive`).

## YAML models

The YAML models include:
- `CrewYamlConfig` (complete crew definition)
- `AgentYamlConfig` (role, goal, backstory, tools, limits)
- `TaskYamlConfig` (description, expected output, dependencies, circuit breaker)
- `LlmYamlConfig` (model, temperature, max tokens)
- `CircuitBreakerYamlConfig` (preset, thresholds, guards)
- `GraphYamlConfig` (maxRetryCycles, circuitBreakerPreset, overrides)
- `AutonomousBudgetYamlConfig` (presets, multi-dimensional limits)

Predefined configurations are available via `OrkeonConfig`: `Default`, `Development` (debug enabled, InMemory), `Production` (debug disabled, Redis).

---

> **See also**: [YAML and Builders](../getting-started/yaml-and-builders.md) · [FSM orchestration](../orchestration/fsm.md) · [Graph orchestration](../orchestration/graph.md) · [Autonomous orchestration](../orchestration/autonomous.md) · [Back to index](../INDEX.md)
