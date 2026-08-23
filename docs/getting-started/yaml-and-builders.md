> 🇫🇷 [Version française](../fr/getting-started/yaml-and-builders.md)

# YAML, Builders and CrewFactory

> **See also**: [Bootstrap](./bootstrap.md) · [YAML schema reference](../architecture/yaml-schema.md) · [Back to the index](../INDEX.md)

## Fluent Builders

The three main entities are built via fluent builders defined in the Domain layer:

- `AgentBuilder` (`Orkeon.Domain.Agent`): configures role, goal, backstory, tools, execution constraints, prompt templates, tool access policy
- `CrewTaskBuilder` (`Orkeon.Domain.Task`): configures description, expected output, priority, dependencies, output JSON schema, async mode, human intervention
- `CrewBuilder` (`Orkeon.Domain.Crew`): configures goal, process type, agents, tasks, planning, memory, callbacks, dynamic agents

Each builder internally delegates to the factory methods `Agent.Create()`, `CrewTask.Create()`, `Crew.Create()` and throws a `BuilderValidationException` if the required fields are missing.

## YAML configuration

Orkeon supports full crew configuration via YAML. The `YamlCrewDefinitionLoader` loader (`Orkeon.Infrastructure.Configuration`) converts YAML files into domain objects.

### Configuration schema (abridged)

The YAML structure follows this schema:

```yaml
# CrewYamlConfig schema — most-used keys (see docs/architecture/yaml-schema.md for the full surface:
# crew-level llm:/rag:/links:, agent knowledge:, task tools:/deliverable:/llm_override:, llm thinking/responseFormat/cache)
name: string              # Crew identifier
goal: string              # Goal (required)
process: string           # "sequential" | "hierarchical" | "parallel" | "consensual" | "graph" | "autonomous"
verbose: bool             # default: false
memory: bool              # default: false
memoryProvider: string    # "InMemory" | "Redis" | "Sqlite" | "ChromaDb" | "Pinecone" | "LanceDb"
planning: bool            # default: false
managerAgent: string      # Required when process = "hierarchical"

agents:
  <agent_id>:             # Key = unique agent identifier
    role: string          # Agent role (required)
    goal: string          # Agent's personal goal (required)
    backstory: string     # Context and expertise (multi-line recommended)
    tools: [string]       # Names of tools registered in IToolRegistry
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
    expectedOutput: string # Expected output format/content (required)
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
    guardrails:           # Task-level guardrails (optional) — same shape as at agent level
      preset: string      # "analysis" | "strict" | "creative"
      header: string
      rules: [string]
      toolRules:
        <tool_name>: [string]
```

The `circuitBreaker` block can also be used at the root level of the YAML (default for all tasks). See [FSM Orchestration](../orchestration/fsm.md) for the full details.

Guardrails may be declared on an agent (all its tasks) and/or on a task (that task only). When both
exist, both apply — agent rules first, then the task's — injected into the executing agent's system
prompt. See [YAML schema — Guardrails configuration](../architecture/yaml-schema.md#guardrails-configuration).

When `process: "graph"` is used, an additional `graphConfig` block configures the state graph engine:

```yaml
graphConfig:
  maxRetryCycles: int           # default: 2 — retry cycles for failed tasks
  circuitBreakerPreset: string  # "strict" | "permissive" | "default"
  maxTransitions: int           # Overrides the preset
  maxStateVisits: int           # Cycle detection (overrides the preset)
  maxTotalDurationSeconds: int  # Total duration in seconds (overrides the preset)
```

See [Graph Orchestration](../orchestration/graph.md) for the full details.

### YAML models

The YAML models include:
- `CrewYamlConfig` (complete definition of a crew)
- `AgentYamlConfig` (role, goal, backstory, tools, limits)
- `TaskYamlConfig` (description, expected output, dependencies, circuit breaker)
- `LlmYamlConfig` (model, temperature, max tokens)
- `CircuitBreakerYamlConfig` (preset, thresholds, guards — see [FSM Orchestration](../orchestration/fsm.md))
- `GraphYamlConfig` (maxRetryCycles, circuitBreakerPreset, overrides — see [Graph Orchestration](../orchestration/graph.md))

Predefined configurations are available via `OrkeonConfig`: `Default`, `Development` (debug enabled, InMemory), `Production` (debug disabled, Redis).

### Loading modes

The `YamlCrewDefinitionLoader` loader supports two modes:

**Single-file mode**: contains agents and tasks in a single file

```csharp
// loader: ICrewDefinitionLoader (YamlCrewDefinitionLoader implementation) resolved via DI
var config = await loader.LoadFromFileAsync("crews/research_crew.yaml", ct);
var crew = await crewFactory.CreateFromConfigAsync(config, ct);
```

**Multi-file mode**: separate agents.yaml, tasks.yaml, and crew.yaml in a directory

```csharp
var config = await loader.LoadFromDirectoryAsync("crews/research/", ct);
var crew = await crewFactory.CreateFromConfigAsync(config, ct);
// Automatically loads: crew.yaml, agents.yaml, tasks.yaml
```

**Per-entity directory mode**: crew settings in `config.yaml`
(or `crew.yaml`), one agent per file under `agents/`, one task per file under `tasks/`.
The **file-name stem is the entity id** — i.e. the dictionary key used in the flat formats.
`LoadFromDirectoryAsync` selects this mode automatically as soon as an `agents/` or `tasks/`
sub-directory is present.

```
crews/research/
├── config.yaml          # crew settings (name, goal, process, llm, …) — or crew.yaml
├── agents/
│   ├── researcher.yaml  # → agent id "researcher"
│   └── writer.yaml      # → agent id "writer"
└── tasks/
    ├── collect.yaml     # → task id "collect"
    └── report.yaml      # → task id "report"
```

```csharp
// Same call: the layout is detected automatically.
var config = await loader.LoadFromDirectoryAsync("crews/research/", ct);
var crew = await crewFactory.CreateFromConfigAsync(config, ct);
```

Notes:

- `config.yaml` is preferred over `crew.yaml` when both are present; a missing settings file
  raises `FileNotFoundException` (parity with flat mode).
- Empty or absent `agents/`/`tasks/` folders simply yield no agents/tasks — the usual
  "at least one agent/task" validation error then surfaces via `loader.Validate(config)`.
- Mixing the two layouts (e.g. both `agents.yaml` *and* an `agents/` directory) raises
  `InvalidOperationException` rather than picking a silent precedence.
- **Limitation:** YAML anchors cannot span files — each file is preprocessed independently
  (this was already true across the three flat files).

On the CLI side, `orkeon run <directory>` accepts these directories directly
— see
[Three ways to run Orkeon](./three-ways-to-run-orkeon.md#run).

## CrewFactory — From YAML to domain objects

The creation pipeline transforms the YAML configuration into operational domain objects via `CrewFactory` (`Orkeon.Infrastructure.Configuration`), which implements `ICrewFactory` (`Orkeon.Application.Interfaces`).

### Creation pipeline

1. **YAML → CrewYamlConfig**: YAML deserialization into configuration models
2. **CrewYamlConfig → CrewConfiguration**: Mapping of the YAML models to the application DTOs with basic validation
3. **Tool resolution**: Tool names (strings) resolved via `IToolRegistry.GetToolByNameAsync(name)` into `IBaseTool` instances
4. **Agent creation**: `Agent` instances built with `AgentBuilder` and resolved tools
5. **Task creation**: `CrewTask` instances built with `CrewTaskBuilder` and validated dependencies
6. **Dependency validation**: Circular dependency detection and order validation
7. **Crew creation**: `Crew` instance built with `CrewBuilder`, process strategy applied
8. **Persistence**: Agents, Tasks, Crew persisted in the repositories (optional)

### Entry points

`CrewFactory` exposes three public methods:

```csharp
/// <summary>
/// Creates a Crew from an already deserialized CrewConfiguration.
/// Used after LoadFromFile/LoadFromDirectory.
/// </summary>
public async Task<Crew> CreateFromConfigAsync(
    CrewConfiguration config,
    CancellationToken ct = default)
{
    // Validates the config, resolves the tools, creates agents/tasks/crew
}

/// <summary>
/// Creates a Crew directly from a YAML file.
/// Single-file mode (agents + tasks in the same file).
/// </summary>
public async Task<Crew> CreateFromFileAsync(
    string yamlFilePath,
    CancellationToken ct = default)
{
    // Deserializes YAML → CrewYamlConfig → calls CreateFromConfigAsync
}

/// <summary>
/// Creates a Crew from a YAML directory.
/// Multi-file mode: crew.yaml + agents.yaml + tasks.yaml
/// </summary>
public async Task<Crew> CreateFromDirectoryAsync(
    string directoryPath,
    CancellationToken ct = default)
{
    // Loads agents.yaml, tasks.yaml, crew.yaml → calls CreateFromConfigAsync
}
```

### Tool resolution

The tool names specified in `agents[].tools[]` are resolved at construction time via `IToolRegistry.GetToolByNameAsync(toolName)`. What happens when a tool is missing depends on `CrewFactoryOptions.StrictTools` (config key `Orkeon:CrewFactory:StrictTools`): the runners (`orkeon run`) default it to **true** and `CrewFactory` then throws with the list of missing tools; the **library default is false** — the tool is skipped with a Warning log and the crew loads without it.

Example error (strict mode):

```
Crew configuration references unknown tool(s): web_scraper, custom_analyzer. Available tools: file_read, file_write, http_api, ...
```
