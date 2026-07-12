> 🇬🇧 [English version](../../orchestration/graph.md)

> **Voir aussi** : [Guide comparatif ProcessTypes](./process-types.md) · [Orchestration FSM](./fsm.md) · [Schéma YAML](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)

# Orchestration par graphe d'état type (StateGraph)

## Vue d'ensemble

Orkeon fournit un moteur de graphe d'état type (`StateGraph<TState>`) dans la couche Domain, inspiré de LangGraph. Contrairement à la FSM générique (voir [Orchestration FSM](./fsm.md)) qui pilote les transitions au sein d'une tâche, le StateGraph orchestre le flux entre tâches au niveau Crew, avec des edges conditionnels et des cycles contrôlés.

Le StateGraph est configurable via YAML (champ `graphConfig`) et s'intègre comme un `IProcessStrategy` alternatif (`GraphProcessStrategy`) sélectionnable via `process: "graph"` dans le config.yaml.

### Positionnement par rapport aux autres stratégies

| Stratégie | Granularité | Cycles | Routing conditionnel | Circuit breaker |
|-----------|-------------|--------|----------------------|-----------------|
| Sequential | Crew | Non | Non | Non (via FSM par task) |
| Hierarchical | Crew | Non | Manager LLM | Non (via FSM par task) |
| Parallel | Crew | Non | Non | Non (via FSM par task) |
| **Graph** | **Crew** | **Oui (contrôlés)** | **Oui (edges conditionnels)** | **Oui (intégré)** |
| FSM | Task | Oui (guards) | Oui (events/guards) | Oui (intégré) |

Le Graph et la FSM sont complémentaires : le Graph orchestre la séquence des tâches, la FSM orchestre l'exécution interne de chaque tâche.

## Architecture

### Couche Domain — Moteur générique

Les composants du moteur se trouvent dans `Orkeon.Domain.Graph` :

| Classe | Rôle |
|--------|------|
| `StateGraph<TState>` | Définition du graphe : nœuds, edges fixes et conditionnels, compilation |
| `GraphNode<TState>` | Nœud de traitement : `Func<TState, CancellationToken, Task<TState>>` |
| `IGraphEdge<TState>` | Interface pour le routage (fixe ou conditionnel) |
| `FixedEdge<TState>` | Edge inconditionnel vers un nœud cible |
| `ConditionalEdge<TState>` | Edge avec fonction de routage `Func<TState, string>` |
| `GraphRunner<TState>` | Moteur d'exécution avec circuit breaker et observabilité |
| `GraphExecutionResult<TState>` | Résultat : état final, trace, transitions, durée |
| `GraphCircuitBrokenException` | Exception quand le circuit breaker trip |

### Couche Domain — Configuration

| Classe | Rôle |
|--------|------|
| `GraphConfig` | DTO immutable pour la configuration graph depuis YAML |

Le champ `GraphConfig?` est ajouté dans `CrewConfiguration` (`Orkeon.Domain.Configuration`).

### Couche Infrastructure — Integration

| Classe | Rôle |
|--------|------|
| `GraphProcessStrategy` | Implémente `IProcessStrategy`, construit et exécute le StateGraph |
| `CrewGraphState` | État type traversant le graphe (tâches, agents, résultats, retries) |
| `GraphYamlConfig` | Modèle YAML pour la section `graphConfig` |

### Couche Infrastructure — Enregistrement

| Classe | Modification |
|--------|-------------|
| `ProcessStrategyFactory` | Ajout du case `"Graph"` → `GraphProcessStrategy` |
| `YamlCrewDefinitionLoader` | Ajout de `GraphYamlConfig`, `MapGraphConfig()`, `"graph"` dans `ParseProcessType` |
| `ProcessType` | Ajout de `ProcessType.Graph` |

## Topologie du graphe

```
START ──► [execute_task] ──► [route] ──┬──► [execute_task]   (tâches restantes ou retry)
                                       │
                                       └──► END              (tout est terminé)
```

Le nœud `execute_task` dequeue une tâche, la fait exécuter par un agent (round-robin ou affectation explicite), et accumule le résultat dans l'état type `CrewGraphState`.

Le nœud `route` inspecte l'état :

- S'il reste des tâches en file → reboucle vers `execute_task`
- Si des tâches ont échoué et que le compteur de retries n'est pas épuisé → reenqueue les échecs et reboucle
- Sinon → route vers END

Le circuit breaker coupe automatiquement si le graphe dépasse les seuils configurés.

## Circuit breaker

Le circuit breaker est intégré dans `GraphRunner<TState>` et vérifie trois conditions avant chaque nœud :

### Mécanismes de protection

| Mécanisme | Paramètre | Description |
|-----------|-----------|-------------|
| Max transitions | `MaxTransitions` | Nombre total d'exécutions de nœuds autorisées |
| Détection de cycles | `MaxStateVisits` | Nombre max de visites d'un même nœud |
| Durée totale | `MaxTotalDuration` | Durée de vie maximale du graphe |

Le StateGraph réutilise `CircuitBreakerPolicy` de `Orkeon.Domain.Common.StateMachine` (même record que la FSM).

Quand une condition est violée, une `GraphCircuitBrokenException` est levée avec la trace complète du graphe. Le `GraphProcessStrategy` capture cette exception et retourne un `CrewOutput.CreateFailure()` au lieu de propager.

### Presets

Les presets sont les mêmes que pour la FSM :

| Preset | MaxTransitions | StateTimeout | MaxStateVisits | MaxTotalDuration |
|--------|---------------|-------------|----------------|------------------|
| `Strict` | 50 | 2 min | 5 | 10 min |
| `Default` | 100 | 5 min | 10 | 30 min |
| `Permissive` | 1000 | 30 min | 50 | 2 h |

### Observabilité

Le `GraphRunner<TState>` expose deux événements :

- `OnNodeCompleted` : émis après chaque nœud, avec `NodeCompletedEventArgs` (nom du nœud, état courant, ordinal, trace snapshot)
- `OnCircuitBroken` : émis quand le circuit trip, avec `GraphCircuitBrokenEventArgs` (raison, nœud, compteur, trace)

Le `GraphProcessStrategy` s'abonne à ces événements pour le logging structuré via `ILogger` (source-generated `LoggerMessage`).

## Retry contrôlé

Le `GraphProcessStrategy` ajoute un mécanisme de retry au-dessus du circuit breaker :

| Paramètre | Champ | Description |
|-----------|-------|-------------|
| Retry cycles | `MaxRetryCycles` | Nombre de cycles de retry pour les tâches échouées |

Fonctionnement :

1. Quand une tâche échoue, elle est ajoutée à `FailedTaskIds` avec un compteur
2. Quand `PendingTaskIds` est vide, le nœud `route` promeut les tâches échouées éligibles
3. Les tâches ayant dépassé `MaxRetryCycles` sont abandonnées
4. Le circuit breaker coupe si le total de transitions dépasse les seuils

## Configuration YAML

### Schema `graphConfig`

Le bloc `graphConfig` est utilisable au niveau racine du YAML :

```yaml
name: "my-crew"
process: "graph"

graphConfig:
  maxRetryCycles: int           # default: 2 — cycles de retry pour tâches échouées
  circuitBreakerPreset: string  # "strict" | "permissive" | "default"
  maxTransitions: int           # Surcharge le preset
  maxStateVisits: int           # Détection de cycles (surcharge le preset)
  maxTotalDurationSeconds: int  # Durée totale en secondes (surcharge le preset)

agents:
  <agent_id>:
    # ... même schéma que sequential
tasks:
  <task_id>:
    # ... même schéma que sequential
```

### Hierarchie de resolution

```
1. graphConfig champs explicites     (priorité haute)
2. graphConfig.circuitBreakerPreset  (base de valeurs)
3. CircuitBreakerPolicy.Strict       (fallback si rien n'est configuré)
```

### Modèles YAML

| Modèle C# | Classe YAML | Fichier |
|-----------|-------------|---------|
| `GraphConfig` | `GraphYamlConfig` | `YamlCrewDefinitionLoader.cs` |

Le mapping est effectué par `YamlCrewDefinitionLoader.MapGraphConfig()`.

## Utilisation en code C#

### Utilisation directe du StateGraph (framework générique)

```csharp
using Orkeon.Domain.Graph;
using Orkeon.Domain.Common.StateMachine;

// Définir un état type
class PipelineState
{
    public Queue<string> Pending { get; set; } = new();
    public List<string> Results { get; set; } = [];
    public int RetryCount { get; set; }
}

// Construire le graphe
var graph = new StateGraph<PipelineState>(CircuitBreakerPolicy.Strict)
    .AddNode("process", async (state, ct) =>
    {
        var item = state.Pending.Dequeue();
        var result = await ProcessItemAsync(item, ct);
        state.Results.Add(result);
        return state;
    })
    .AddNode("decide", (state, _) => Task.FromResult(state))
    .AddEdge(StateGraph<PipelineState>.StartNode, "process")
    .AddEdge("process", "decide")
    .AddConditionalEdge("decide",
        state => state.Pending.Count > 0
            ? "process"
            : StateGraph<PipelineState>.EndNode,
        ["process", StateGraph<PipelineState>.EndNode]);

// Compiler et exécuter
var runner = graph.Compile();

runner.OnNodeCompleted += (_, args) =>
    Console.WriteLine($"Node '{args.NodeName}' done (#{args.TransitionOrdinal})");

runner.OnCircuitBroken += (_, args) =>
    Console.WriteLine($"CIRCUIT BROKEN at '{args.NodeName}': {args.Reason}");

var result = await runner.RunAsync(new PipelineState
{
    Pending = new Queue<string>(["a", "b", "c"])
});

Console.WriteLine($"Trace: {string.Join(" -> ", result.Trace)}");
Console.WriteLine($"Total transitions: {result.TotalTransitions}");
```

### Utilisation via YAML (ProcessType.Graph)

```csharp
using Orkeon.Infrastructure.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;

// Charger la config YAML
var loader = serviceProvider.GetRequiredService<ICrewDefinitionLoader>();
var config = await loader.LoadFromFileAsync("config.yaml");

// La factory crée automatiquement le GraphProcessStrategy
var factory = serviceProvider.GetRequiredService<IProcessStrategyFactory>();
var strategy = factory.CreateStrategy(ProcessType.Graph);

// Exécuter
var result = await strategy.ExecuteSequentialAsync(crew, plan, inputVariables);
```

### Construction d'un graphe custom avec plus de nœuds

```csharp
var graph = new StateGraph<MyState>(new CircuitBreakerPolicy
    {
        MaxTransitions = 100,
        MaxStateVisits = 5,
        MaxTotalDuration = TimeSpan.FromMinutes(15)
    })
    .AddNode("fetch", async (s, ct) => { /* ... */ return s; })
    .AddNode("validate", async (s, ct) => { /* ... */ return s; })
    .AddNode("transform", async (s, ct) => { /* ... */ return s; })
    .AddNode("error_handler", async (s, ct) => { /* ... */ return s; })
    .AddEdge(StateGraph<MyState>.StartNode, "fetch")
    .AddEdge("fetch", "validate")
    .AddConditionalEdge("validate",
        s => s.IsValid ? "transform" : "error_handler",
        ["transform", "error_handler"])
    .AddEdge("transform", StateGraph<MyState>.EndNode)
    .AddConditionalEdge("error_handler",
        s => s.RetryCount < 3 ? "fetch" : StateGraph<MyState>.EndNode,
        ["fetch", StateGraph<MyState>.EndNode]);

var runner = graph.Compile();
var result = await runner.RunAsync(initialState);
```

## Exemple 102

L'exemple `examples/09-experimental/102-graph-orchestration/` démontre l'intégration complète avec :

- `process: "graph"` pour activer le `GraphProcessStrategy`
- `graphConfig` avec `maxRetryCycles: 2` et preset "strict"
- 4 agents (Data Collector, Data Validator, Insight Analyst, Report Writer)
- 4 tâches avec dépendances linéaires
- Retry automatique des tâches échouées (transient failures du web scraping)

Voir le fichier `config.yaml` et `README.md` de l'exemple pour la syntaxe complete.

## Relation avec l'existant

### FSM TaskExecutionStateMachine

Le StateGraph et la FSM opèrent à des niveaux différents :

- **FSM** : gère l'exécution interne d'une tâche (Assigned → Executing → ToolCalling → Validating → Completed)
- **StateGraph** : gère le flux entre tâches (quelle tâche exécuter, quand retrier, quand terminer)

Les deux sont complémentaires et coexistent. Quand le `GraphProcessStrategy` exécute une tâche via `IAgentExecutionService.ExecuteTaskAsync()`, la FSM gère le cycle interne de cette tâche.

### SequentialCrewOrchestrator

L'orchestrateur existant n'est pas remplacé. Le `GraphProcessStrategy` est une alternative au `SequentialProcessStrategy` dans le même framework `IProcessStrategy`. L'orchestrateur utilise la `ProcessStrategyFactory` pour choisir la bonne stratégie selon `ProcessType`.

### Process Strategies existantes

Le `GraphProcessStrategy` coexiste avec les stratégies existantes :

```
ProcessStrategyFactory.CreateStrategy(processType) switch
{
    "Sequential"   → SequentialProcessStrategy
    "Hierarchical" → HierarchicalProcessStrategy
    "Parallel"     → ParallelProcessStrategy
    "Graph"        → GraphProcessStrategy
}
```

L'ajout n'impacte pas les strategies existantes. Le code client choisit via `ProcessType.Graph` ou `process: "graph"` dans le YAML.

## Tests

30+ tests unitaires couvrent le moteur et la stratégie :

| Fichier de test | Couverture |
|-----------------|-----------|
| `StateGraphTests.cs` | Flux linéaire, routing conditionnel, boucles contrôlées, circuit breaker (max transitions, détection cycles), observabilité (OnNodeCompleted, OnCircuitBroken), cancellation, validation du graphe, mutation d'état |
| `GraphProcessStrategyTests.cs` | Happy path (0 tasks, 1 task, N tasks), retry contrôlé (succès après retry, abandon après max), circuit breaker integration, process types non supportés, tâches manquantes, agents manquants |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/ --filter "FullyQualifiedName~StateGraph"
dotnet test tests/core/Orkeon.Infrastructure.Tests/ --filter "FullyQualifiedName~GraphProcessStrategy"
```
