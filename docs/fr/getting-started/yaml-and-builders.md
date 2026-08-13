> 🇬🇧 [English version](../../getting-started/yaml-and-builders.md)

# YAML, Builders et CrewFactory

> **Voir aussi** : [Bootstrap](./bootstrap.md) · [Schéma YAML de référence](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)

## Fluent Builders

Les trois entités principales sont construites via des builders fluides définis dans la couche Domain :

- `AgentBuilder` (`Orkeon.Domain.Agent`) : configure rôle, objectif, backstory, outils, contraintes d'exécution, templates de prompt, politique d'accès outils
- `CrewTaskBuilder` (`Orkeon.Domain.Task`) : configure description, résultat attendu, priorité, dépendances, schéma JSON de sortie, mode async, intervention humaine
- `CrewBuilder` (`Orkeon.Domain.Crew`) : configure objectif, process type, agents, tasks, planification, mémoire, callbacks, agents dynamiques

Chaque builder délègue en interne aux méthodes factory `Agent.Create()`, `CrewTask.Create()`, `Crew.Create()` et lève une `BuilderValidationException` si les champs obligatoires sont absents.

## Configuration YAML

Orkeon supporte la configuration complète des crews via YAML. Le loader `YamlCrewDefinitionLoader` (`Orkeon.Infrastructure.Configuration`) convertit les fichiers YAML en objets domaine.

### Schéma de configuration complet

La structure YAML suit ce schéma :

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

Le bloc `circuitBreaker` est également utilisable au niveau racine du YAML (défaut pour toutes les tâches). Voir [Orchestration FSM](../orchestration/fsm.md) pour le détail complet.

Quand `process: "graph"` est utilisé, un bloc `graphConfig` supplémentaire configure le moteur de graphe d'état :

```yaml
graphConfig:
  maxRetryCycles: int           # default: 2 — cycles de retry pour tâches échouées
  circuitBreakerPreset: string  # "strict" | "permissive" | "default"
  maxTransitions: int           # Surcharge le preset
  maxStateVisits: int           # Détection de cycles (surcharge le preset)
  maxTotalDurationSeconds: int  # Durée totale en secondes (surcharge le preset)
```

Voir [Orchestration Graph](../orchestration/graph.md) pour le détail complet.

### Modèles YAML

Les modèles YAML incluent :
- `CrewYamlConfig` (définition complète d'une crew)
- `AgentYamlConfig` (rôle, objectif, backstory, outils, limites)
- `TaskYamlConfig` (description, résultat attendu, dépendances, circuit breaker)
- `LlmYamlConfig` (modèle, température, max tokens)
- `CircuitBreakerYamlConfig` (preset, seuils, guards — voir [Orchestration FSM](../orchestration/fsm.md))
- `GraphYamlConfig` (maxRetryCycles, circuitBreakerPreset, surcharges — voir [Orchestration Graph](../orchestration/graph.md))

Des configurations prédéfinies sont disponibles via `OrkeonConfig` : `Default`, `Development` (debug activé, InMemory), `Production` (debug désactivé, Redis).

### Modes de chargement

Le loader `YamlCrewDefinitionLoader` supporte deux modes :

**Mode fichier unique** : contient agents et tasks dans un seul fichier

```csharp
// loader : ICrewDefinitionLoader (implémentation YamlCrewDefinitionLoader) résolu via DI
var config = await loader.LoadFromFileAsync("crews/research_crew.yaml", ct);
var crew = await crewFactory.CreateFromConfigAsync(config, ct);
```

**Mode multi-fichier** : séparation agents.yaml, tasks.yaml, et crew.yaml dans un répertoire

```csharp
var config = await loader.LoadFromDirectoryAsync("crews/research/", ct);
var crew = await crewFactory.CreateFromConfigAsync(config, ct);
// Charge automatiquement : crew.yaml, agents.yaml, tasks.yaml
```

**Mode répertoire par entité** : les réglages de la crew dans `config.yaml` (ou
`crew.yaml`), un agent par fichier sous `agents/`, une task par fichier sous
`tasks/`. Le **nom du fichier (sans extension) est l'identifiant de l'entité** —
c'est-à-dire la clé de dictionnaire utilisée dans les formats plats.
`LoadFromDirectoryAsync` sélectionne ce mode automatiquement dès qu'un
sous-répertoire `agents/` ou `tasks/` est présent.

```
crews/research/
├── config.yaml          # réglages de la crew (name, goal, process, llm, …) — ou crew.yaml
├── agents/
│   ├── researcher.yaml  # → agent "researcher"
│   └── writer.yaml      # → agent "writer"
└── tasks/
    ├── collect.yaml     # → task "collect"
    └── report.yaml      # → task "report"
```

```csharp
// Même appel : la disposition est détectée automatiquement.
var config = await loader.LoadFromDirectoryAsync("crews/research/", ct);
var crew = await crewFactory.CreateFromConfigAsync(config, ct);
```

Notes :

- `config.yaml` est préféré à `crew.yaml` quand les deux sont présents ; un fichier
  de réglages absent lève `FileNotFoundException` (parité avec le mode plat).
- Un dossier `agents/`/`tasks/` vide ou absent ne produit simplement aucun agent ni
  aucune task — l'erreur de validation « au moins un agent/une task » remonte alors
  via `loader.Validate(config)`.
- Mélanger les deux dispositions (par exemple `agents.yaml` **et** un répertoire
  `agents/`) lève `InvalidOperationException` au lieu d'appliquer une précédence
  silencieuse.
- **Limite** : les ancres YAML ne peuvent pas traverser les fichiers — chaque fichier
  est prétraité indépendamment (c'était déjà le cas entre les trois fichiers plats).

Côté CLI, `orkeon run <dossier>` accepte directement ces répertoires
(Orkeon >= 0.9.2-beta) — voir
[Trois façons d'exécuter Orkeon](./three-ways-to-run-orkeon.md#exécuter).

## CrewFactory — Du YAML aux objets domaine

La pipeline de création transforme la configuration YAML en objets domaine opérationnels via `CrewFactory` (`Orkeon.Infrastructure.Configuration`), qui implémente `ICrewFactory` (`Orkeon.Application.Interfaces`).

### Pipeline de création

1. **YAML → CrewYamlConfig** : Désérialisation YAML en modèles de configuration
2. **CrewYamlConfig → CrewConfiguration** : Mapping des modèles YAML vers les DTOs application avec validation basique
3. **Résolution des outils** : Noms d'outils (strings) résolus via `IToolRegistry.GetToolByNameAsync(name)` en instances `IBaseTool`
4. **Création des agents** : Instances `Agent` construites avec `AgentBuilder` et outils résolus
5. **Création des tasks** : Instances `CrewTask` construites avec `CrewTaskBuilder` et dépendances validées
6. **Validation des dépendances** : Détection des cycles (circular dependency detection) et validation de l'ordre
7. **Création de la Crew** : Instance `Crew` construite avec `CrewBuilder`, process strategy appliquée
8. **Persistance** : Agents, Tasks, Crew persists dans les repositories (facultatif)

### Points d'entrée

`CrewFactory` expose trois méthodes publiques :

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

### Résolution des outils

Les noms d'outils spécifiés dans `agents[].tools[]` sont résolus à la construction via `IToolRegistry.GetToolByNameAsync(toolName)`. Si un outil n'existe pas en registre, `CrewFactory` lève une exception de validation avec la liste des outils manquants.

Exemple d'erreur :

```
CrewFactory Error: Tools not found in registry:
  - web_scraper
  - custom_analyzer
Available tools: file_read, file_write, http_api, ...
```
