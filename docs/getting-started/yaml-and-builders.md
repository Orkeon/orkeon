> 🇫🇷 [Version française](../fr/getting-started/yaml-and-builders.md)

# YAML, Builders and CrewFactory

> **See also**: [Bootstrap](./bootstrap.md) · [YAML schema reference](../architecture/yaml-schema.md) · [Back to the index](../INDEX.md)

## Fluent Builders

The three main entities are built via fluent builders defined in the Domain layer:

- `AgentBuilder` (`Orkeon.Domain.Agent`): configures role, goal, backstory, tools, execution constraints, prompt templates, tool access policy
- `CrewTaskBuilder` (`Orkeon.Domain.Task`): configures description, expected output, priority, dependencies, output JSON schema, async mode (`.Async()` — honoured by a sequential crew, refused by `CrewBuilder.Build()` outside Sequential and Parallel), human intervention
- `CrewBuilder` (`Orkeon.Domain.Crew`): configures name, goal, process type, agents, tasks, planning, memory, callbacks, dynamic agents

Each builder internally delegates to the factory methods `Agent.Create()`, `CrewTask.Create()`, `Crew.Create()` and throws a `BuilderValidationException` if the required fields are missing.

An agent's model settings: `WithLlmConfig(config)` sets them whole; the sugar `Thinking(enabled, effort)`
and `MaxOutputTokens(n)` merges into them and, on an agent that has none, starts from a configuration
that names no model (`LlmConfig.OnProfile()`) — the agent then runs on the host's model, whatever vendor
the host runs. To pin a model, start from `LlmConfig.Create(model)`; to run on one of the host's
[named profiles](#one-provider-per-agent-profiles), from `LlmConfig.OnProfile("claude")`.

## YAML configuration

Orkeon supports full crew configuration via YAML. The `YamlCrewDefinitionLoader` loader (`Orkeon.Infrastructure.Configuration`) converts YAML files into domain objects.

### Configuration schema (abridged)

The YAML structure follows this schema:

```yaml
# CrewYamlConfig schema — most-used keys (see docs/architecture/yaml-schema.md for the full surface:
# crew-level llm:/rag:/links:/mounts:, agent knowledge:, task tools:/deliverable:/llm_override:, llm thinking/responseFormat/cache)
name: string              # Crew identifier; scopes the crew's long-term memory (the crews of one name share it)
goal: string              # Goal (required)
process: string           # "sequential" | "hierarchical" | "parallel" | "consensual" | "graph" | "autonomous"
verbose: bool             # default: false
memory: bool              # default: false. true: stores each task's result and recalls the closest ones before each task (needs an embedder)
memoryProvider: string    # needs memory: true. "InMemory" | "Redis" | "Sqlite" | "ChromaDb" | "Pinecone" | "LanceDb" — the type; the host section (Orkeon:Redis, …) gives the connection; unset: the host's default store
planning: bool            # default: false
managerAgent: string      # Required when process = "hierarchical"
mounts: [string]          # Virtual roots the crew uses ("/output", or "<id>|/output" to pin one settings entry)

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
      profile: string     # Host LLM profile (Llm:Profiles:<name>) — unset = the host's default provider
      model: string       # LLM model ("gpt-4", "claude-3-opus", etc.) — unset = the profile's own model
      temperature: float  # Creativity (0.0-1.0)
      maxTokens: int      # Output token limit

tasks:
  <task_id>:              # Key = unique task identifier
    description: string   # Detailed task description (required)
    expectedOutput: string # Expected output format/content (required)
    agent: string         # ID of the agent assigned to the task
    dependencies: [string] # IDs of prerequisite tasks (guarantees ordering)
    asyncExecution: bool  # default: false — sequential: runs alongside the next tasks, a dependant waits for it; parallel: no effect of its own; other modes: true fails the load
    humanInput: bool      # default: false — requests human intervention
    context: {key: value} # Additional context data
    tools: [string]       # Tools ADDED to the agent's own for this task only — never replacing them
    guardrails:           # Task-level guardrails (optional) — same shape as at agent level
      preset: string      # "analysis" | "strict" | "creative"
      header: string
      rules: [string]
      toolRules:
        <tool_name>: [string]
```

### One provider per agent (profiles)

A host offers its providers as named **profiles** (`Llm:Profiles:<name>` in its settings, the
`Llm` section being the default one — see [Configuration](../reference/configuration.md#named-profiles-llmprofiles)).
A crew picks one **by name** — at crew level (every agent), on an agent, or on a task's
`llm_override` (that task only); the most specific wins, and `profile: default` brings an agent or
a task back to the host's default. A crew file never carries a key or an endpoint.

```yaml
llm:
  profile: deepseek          # crew level: every agent, unless it says otherwise
agents:
  planner:
    role: Planner
    goal: Plan the article
    llm:
      profile: claude        # this agent runs on the host's "claude" profile…
      model: claude-opus-5   # …on another model than the profile's own
  writer:
    role: Writer
    goal: Write the article  # inherits the crew's "deepseek" profile, on its own model
tasks:
  review:
    description: Review the article
    expectedOutput: A review
    agent: writer
    llm_override:
      profile: claude        # this task alone moves the writer to "claude"
```

A profile the host does not define fails the crew load, and the message lists the profiles it
offers — exactly like an unknown tool. A `llm:` block that names no `model` runs on the profile's
own model (the host's for the default profile), never on a framework default. Only the agents'
turns change provider: the hierarchical manager, the planner, the Guardian and the RAG pipelines
stay on the default profile. In `.ork.ts` the same choice is `agentBuilder().llm(llm.profile("claude"))`.

A task's `tools:` add to its agent's own for that task only: a writer that holds `file_read` and runs a task declaring `tools: [file_write]` can read and write during that task, and only read during the others. There is no `circuitBreaker:` block — a crew that writes one is refused at load. What bounds a task at run time is the agent loop (`maxIter`, a stop after 3 consecutive identical tool errors, and the output-validation retries); a Graph run is bounded by `graphConfig` (below).

Guardrails may be declared on an agent (all its tasks) and/or on a task (that task only). When both
exist, both apply — agent rules first, then the task's — injected into the executing agent's system
prompt. See [YAML schema — Guardrails configuration](../architecture/yaml-schema.md#guardrails-configuration).

When `process: "graph"` is used, an additional `graphConfig` block configures the state graph engine:

```yaml
graphConfig:
  maxRetryCycles: int           # default: 2 — retry cycles for failed tasks
  circuitBreakerPreset: string  # "strict" | "permissive" | "default"
  maxTransitions: int           # Default: computed, 2 × visits + 1
  maxStateVisits: int           # Task-attempt cap — default: computed, tasks × (1 + maxRetryCycles)
  maxTotalDurationSeconds: int  # Total duration in seconds (overrides the preset)
```

See [Graph Orchestration](../orchestration/graph.md) for the full details.

### YAML models

The YAML models include:
- `CrewYamlConfig` (complete definition of a crew)
- `AgentYamlConfig` (role, goal, backstory, tools, limits)
- `TaskYamlConfig` (description, expected output, dependencies, tools, deliverable, guardrails)
- `LlmYamlConfig` (profile, model, temperature, max tokens)
- `GraphYamlConfig` (maxRetryCycles, circuitBreakerPreset, overrides — see [Graph Orchestration](../orchestration/graph.md))

There is no framework-level "predefined configuration" object: a host composes its own settings through `AddOrkeonInfrastructure` / `AddOrkeonApplication` and its `appsettings.json`.

### Loading modes

The `YamlCrewDefinitionLoader` loader supports the modes below. It reads through the
virtual file system (`IFileSystemService`), so every path it takes is a **virtual** path
under a declared mount — `/crews/...` below assumes a mount such as
`./crews:/crews:ro` ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)); a
disk path finds no mount.

**Single-file mode**: contains agents and tasks in a single file

```csharp
// loader: ICrewDefinitionLoader (YamlCrewDefinitionLoader implementation) resolved via DI
var config = await loader.LoadFromFileAsync("/crews/research_crew.yaml", ct);
var crew = await crewFactory.CreateFromConfigAsync(config, ct);
```

**Multi-file mode**: separate agents.yaml, tasks.yaml, and crew.yaml in a directory

```csharp
var config = await loader.LoadFromDirectoryAsync("/crews/research/", ct);
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
var config = await loader.LoadFromDirectoryAsync("/crews/research/", ct);
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
2. **CrewYamlConfig → CrewConfiguration**: Mapping of the YAML models to the application DTOs with basic validation (errors throw `InvalidOperationException`, warnings are logged)
3. **RAG collections**: the collections a `rag:` block declares are ingested (incrementally) — when the RAG subsystem is registered; without it, a Warning and no ingestion
4. **Tool resolution**: Tool names (strings) resolved via `IToolRegistry.GetToolByNameAsync(name)` into `IBaseTool` instances
5. **Agent creation**: `Agent` instances built with `AgentBuilder`, resolved tools and their `llm:` block — every profile an agent or a task names must be one the host offers, or the load fails listing them
6. **Task creation**: `CrewTask` instances built with `CrewTaskBuilder` and validated dependencies
7. **Dependency validation**: Circular dependency detection and order validation
8. **Crew creation**: `Crew` instance built with `CrewBuilder`, process strategy applied
9. **Persistence**: Agents, Tasks, Crew stored in the repositories — always, so `ICrewOrchestrationService.KickoffAsync(crew.Id, …)` finds them
10. **Links**: the crew's identity and `links:` block are handed to the EventHub ACL (a declared `links:` block without `AddOrkeonEventHubAcl()` logs a Warning: nothing enforces it)

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
/// Per-entity layout (config.yaml + agents/ + tasks/) or flat crew.yaml + agents.yaml + tasks.yaml
/// </summary>
public async Task<Crew> CreateFromDirectoryAsync(
    string directoryPath,
    CancellationToken ct = default)
{
    // Loads the directory's layout → calls CreateFromConfigAsync
}
```

### Tool resolution

The tool names specified in `agents[].tools[]` are resolved at construction time via `IToolRegistry.GetToolByNameAsync(toolName)`. What happens when a tool is missing depends on `CrewFactoryOptions.StrictTools` (config key `Orkeon:CrewFactory:StrictTools`): the runners (`orkeon run`) default it to **true** and `CrewFactory` then throws with the list of missing tools; the **library default is false** — the tool is skipped with a Warning log and the crew loads without it.

Example error (strict mode):

```
Crew configuration references unknown tool(s): web_scraper, custom_analyzer. Available tools: file_read, file_write, http_api, ...
```
