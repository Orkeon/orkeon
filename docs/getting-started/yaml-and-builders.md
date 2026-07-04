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

### Complete configuration schema

The YAML structure follows this schema:

```yaml
# Schéma complet CrewYamlConfig
name: string              # Identifiant de la crew
goal: string              # Objectif (requis)
process: string           # "sequential" | "hierarchical" | "parallel" | "consensual" | "graph" | "autonomous"
verbose: bool             # default: false
memory: bool              # default: false
memoryProvider: string    # "InMemory" | "Redis" | "Sqlite" | "ChromaDb" | "Pinecone" | "LanceDb"
planning: bool            # default: false
managerAgent: string      # Requis si process = "hierarchical"

agents:
  <agent_id>:             # Clé = identifiant unique de l'agent
    role: string          # Rôle de l'agent (requis)
    goal: string          # Objectif personnel de l'agent (requis)
    backstory: string     # Contexte et expertise (multi-ligne recommandé)
    tools: [string]       # Noms d'outils enregistrés dans IToolRegistry
    allowDelegation: bool # default: true — permet la délégation à d'autres agents
    maxIter: int          # default: 20 — itérations maximales avant timeout
    maxRpm: int           # default: 10 — requêtes par minute (rate limiting)
    verbose: bool         # default: false — logs détaillés pour cet agent
    llm:
      model: string       # Modèle LLM ("gpt-4", "claude-3-opus", etc.)
      temperature: float  # Créativité (0.0-1.0)
      maxTokens: int      # Limite de tokens en sortie

tasks:
  <task_id>:              # Clé = identifiant unique de la tâche
    description: string   # Description détaillée de la tâche (requis)
    expectedOutput: string # Format/contenu attendu en résultat (requis)
    agent: string         # ID de l'agent assigné à la tâche
    dependencies: [string] # IDs des tâches prérequises (garantit l'ordre)
    asyncExecution: bool  # default: false — exécution asynchrone
    humanInput: bool      # default: false — demande intervention humaine
    context: {key: value} # Données additionnelles de contexte
    circuitBreaker:       # Configuration FSM / circuit breaker (optionnel)
      preset: string      # "strict" | "permissive" | "default"
      maxTransitions: int # Transitions max avant trip
      stateTimeoutSeconds: int  # Timeout par état (secondes)
      maxStateVisits: int       # Visites max d'un même état (cycles)
      maxTotalDurationSeconds: int # Durée totale max (secondes)
      useDegradedMode: bool     # true = Degraded, false = exception
      maxRetries: int           # Retries après échec
      maxToolCallsPerRound: int # Tool calls max par round
      maxValidationRetries: int # Boucles validation max
```

The `circuitBreaker` block can also be used at the root level of the YAML (default for all tasks). See [FSM Orchestration](../orchestration/fsm.md) for the full details.

When `process: "graph"` is used, an additional `graphConfig` block configures the state graph engine:

```yaml
graphConfig:
  maxRetryCycles: int           # default: 2 — cycles de retry pour tâches échouées
  circuitBreakerPreset: string  # "strict" | "permissive" | "default"
  maxTransitions: int           # Surcharge le preset
  maxStateVisits: int           # Détection de cycles (surcharge le preset)
  maxTotalDurationSeconds: int  # Durée totale en secondes (surcharge le preset)
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
var crew = await YamlCrewDefinitionLoader.LoadFromFileAsync(
    filePath: "crews/research_crew.yaml",
    cancellationToken: ct
);
```

**Multi-file mode**: separate agents.yaml, tasks.yaml, and crew.yaml in a directory

```csharp
var crew = await YamlCrewDefinitionLoader.LoadFromDirectoryAsync(
    directoryPath: "crews/research/",
    cancellationToken: ct
);
// Charge automatiquement : crew.yaml, agents.yaml, tasks.yaml
```

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
/// Crée une Crew à partir d'une CrewConfiguration déjà désérialisée.
/// Utilisé après LoadFromFile/LoadFromDirectory.
/// </summary>
public async Task<Crew> CreateFromConfigAsync(
    CrewConfiguration config,
    CancellationToken ct = default)
{
    // Valide la config, résout les outils, crée agents/tasks/crew
}

/// <summary>
/// Crée une Crew directement à partir d'un fichier YAML.
/// Mode fichier unique (agents + tasks dans le même fichier).
/// </summary>
public async Task<Crew> CreateFromFileAsync(
    string yamlFilePath,
    CancellationToken ct = default)
{
    // Désérialise YAML → CrewYamlConfig → appelle CreateFromConfigAsync
}

/// <summary>
/// Crée une Crew à partir d'un répertoire YAML.
/// Mode multi-fichier : crew.yaml + agents.yaml + tasks.yaml
/// </summary>
public async Task<Crew> CreateFromDirectoryAsync(
    string directoryPath,
    CancellationToken ct = default)
{
    // Charge agents.yaml, tasks.yaml, crew.yaml → appelle CreateFromConfigAsync
}
```

### Tool resolution

The tool names specified in `agents[].tools[]` are resolved at construction time via `IToolRegistry.GetToolByNameAsync(toolName)`. If a tool does not exist in the registry, `CrewFactory` throws a validation exception with the list of missing tools.

Example error:

```
CrewFactory Error: Tools not found in registry:
  - web_scraper
  - custom_analyzer
Available tools: file_read, file_write, http_api, ...
```
