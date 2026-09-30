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

### Schéma de configuration (abrégé)

La structure YAML suit ce schéma :

```yaml
# Schéma CrewYamlConfig — clés les plus utilisées (surface complète dans docs/architecture/yaml-schema.md :
# llm:/rag:/links:/mounts: au niveau crew, knowledge: agent, tools:/deliverable:/llm_override: tâche, llm thinking/responseFormat/cache)
name: string              # Identifiant de la crew
goal: string              # Objectif (requis)
process: string           # "sequential" | "hierarchical" | "parallel" | "consensual" | "graph" | "autonomous"
verbose: bool             # default: false
memory: bool              # default: false
memoryProvider: string    # "InMemory" | "Redis" | "Sqlite" | "ChromaDb" | "Pinecone" | "LanceDb"
planning: bool            # default: false
managerAgent: string      # Requis si process = "hierarchical"
mounts: [string]          # Racines virtuelles utilisées par la crew ("/output", ou "<id>|/output" pour épingler une entrée des settings)

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
    asyncExecution: bool  # default: false — ENREGISTRÉ, honoré par aucun mode (utiliser process: parallel)
    humanInput: bool      # default: false — demande intervention humaine
    context: {key: value} # Données additionnelles de contexte
    circuitBreaker:       # Analysé, PAS appliqué à l'exécution aujourd'hui (voir ci-dessous)
      preset: string      # "strict" | "permissive" | "default"
      maxTransitions: int # Transitions max avant trip
      stateTimeoutSeconds: int  # Timeout par état (secondes)
      maxStateVisits: int       # Visites max d'un même état (cycles)
      maxTotalDurationSeconds: int # Durée totale max (secondes)
      useDegradedMode: bool     # true = Degraded, false = exception
      maxRetries: int           # Retries après échec
      maxToolCallsPerRound: int # Tool calls max par round
      maxValidationRetries: int # Boucles validation max
    guardrails:           # Guardrails au niveau tâche (optionnel) — même forme qu'au niveau agent
      preset: string      # "analysis" | "strict" | "creative"
      header: string
      rules: [string]
      toolRules:
        <tool_name>: [string]
```

Le bloc `circuitBreaker` est accepté au niveau tâche et à la racine du YAML, mais **aucun des deux n'est un défaut appliqué à toutes les tâches** : le bloc de niveau tâche est analysé puis inutilisé par tous les chemins d'exécution, et le bloc de niveau crew n'est lu que par le [mode Graph](../orchestration/graph.md), quand aucun `graphConfig` n'est déclaré. Ce qui borne une tâche à l'exécution, c'est la boucle de l'agent (`maxIter`, et un arrêt après 3 erreurs d'outil identiques consécutives) — voir « Ce qui s'exécute aujourd'hui » dans [Orchestration FSM](../orchestration/fsm.md#vue-densemble).

Les guardrails peuvent être déclarés sur un agent (toutes ses tâches) et/ou sur une tâche
(cette tâche seulement). Quand les deux existent, les deux s'appliquent — les règles de
l'agent d'abord, puis celles de la tâche — injectées dans le prompt système de l'agent
exécutant. Voir [Schéma YAML — Configuration Guardrails](../architecture/yaml-schema.md#configuration-guardrails).

Quand `process: "graph"` est utilisé, un bloc `graphConfig` supplémentaire configure le moteur de graphe d'état :

```yaml
graphConfig:
  maxRetryCycles: int           # default: 2 — cycles de retry pour tâches échouées
  circuitBreakerPreset: string  # "strict" | "permissive" | "default"
  maxTransitions: int           # Défaut : calculé, 2 × visites + 1
  maxStateVisits: int           # Plafond de tentatives — défaut : calculé, tâches × (1 + maxRetryCycles)
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

Il n'existe pas d'objet « configuration prédéfinie » au niveau du framework : un hôte compose ses réglages via `AddOrkeonInfrastructure` / `AddOrkeonApplication` et son `appsettings.json`.

### Modes de chargement

Le loader `YamlCrewDefinitionLoader` supporte les modes ci-dessous. Il lit à travers le
système de fichiers virtuel (`IFileSystemService`) : chaque chemin qu'il reçoit est donc un
chemin **virtuel** sous un montage déclaré — `/crews/...` ci-dessous suppose un montage tel
que `./crews:/crews:ro` ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)) ;
un chemin disque ne trouve aucun montage.

**Mode fichier unique** : contient agents et tasks dans un seul fichier

```csharp
// loader : ICrewDefinitionLoader (implémentation YamlCrewDefinitionLoader) résolu via DI
var config = await loader.LoadFromFileAsync("/crews/research_crew.yaml", ct);
var crew = await crewFactory.CreateFromConfigAsync(config, ct);
```

**Mode multi-fichier** : séparation agents.yaml, tasks.yaml, et crew.yaml dans un répertoire

```csharp
var config = await loader.LoadFromDirectoryAsync("/crews/research/", ct);
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
var config = await loader.LoadFromDirectoryAsync("/crews/research/", ct);
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
— voir
[Trois façons d'exécuter Orkeon](./three-ways-to-run-orkeon.md#exécuter).

## CrewFactory — Du YAML aux objets domaine

La pipeline de création transforme la configuration YAML en objets domaine opérationnels via `CrewFactory` (`Orkeon.Infrastructure.Configuration`), qui implémente `ICrewFactory` (`Orkeon.Application.Interfaces`).

### Pipeline de création

1. **YAML → CrewYamlConfig** : Désérialisation YAML en modèles de configuration
2. **CrewYamlConfig → CrewConfiguration** : Mapping des modèles YAML vers les DTOs application avec validation basique (les erreurs lèvent `InvalidOperationException`, les avertissements sont journalisés)
3. **Collections RAG** : les collections déclarées par un bloc `rag:` sont ingérées (de façon incrémentale) — quand le sous-système RAG est enregistré ; sans lui, un Warning et aucune ingestion
4. **Résolution des outils** : Noms d'outils (strings) résolus via `IToolRegistry.GetToolByNameAsync(name)` en instances `IBaseTool`
5. **Création des agents** : Instances `Agent` construites avec `AgentBuilder` et outils résolus
6. **Création des tasks** : Instances `CrewTask` construites avec `CrewTaskBuilder` et dépendances validées
7. **Validation des dépendances** : Détection des cycles (circular dependency detection) et validation de l'ordre
8. **Création de la Crew** : Instance `Crew` construite avec `CrewBuilder`, process strategy appliquée
9. **Persistance** : Agents, Tasks, Crew rangés dans les repositories — toujours, pour que `ICrewOrchestrationService.KickoffAsync(crew.Id, …)` les trouve
10. **Liens** : l'identité de la crew et son bloc `links:` sont remis à l'ACL de l'EventHub (un bloc `links:` déclaré sans `AddOrkeonEventHubAcl()` journalise un Warning : rien ne l'applique)

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
/// Disposition par entité (config.yaml + agents/ + tasks/) ou plate crew.yaml + agents.yaml + tasks.yaml
/// </summary>
public async Task<Crew> CreateFromDirectoryAsync(
    string directoryPath,
    CancellationToken ct = default)
{
    // Charge la disposition du répertoire → appelle CreateFromConfigAsync
}
```

### Résolution des outils

Les noms d'outils spécifiés dans `agents[].tools[]` sont résolus à la construction via `IToolRegistry.GetToolByNameAsync(toolName)`. Le sort d'un outil manquant dépend de `CrewFactoryOptions.StrictTools` (clé `Orkeon:CrewFactory:StrictTools`) : les runners (`orkeon run`) le mettent à **true** par défaut et `CrewFactory` lève alors une exception listant les outils manquants ; le **défaut bibliothèque est false** — l'outil est sauté avec un log Warning et la crew se charge sans lui.

Exemple d'erreur (mode strict) :

```
Crew configuration references unknown tool(s): web_scraper, custom_analyzer. Available tools: file_read, file_write, http_api, ...
```
