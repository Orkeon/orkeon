> 🇬🇧 [English version](../../orchestration/graph.md)

> **Voir aussi** : [Guide comparatif ProcessTypes](./process-types.md) · [Orchestration FSM](./fsm.md) · [Schéma YAML](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)

# Orchestration par graphe d'états typé (StateGraph)

## Vue d'ensemble

Orkeon fournit un moteur de graphe d'états typé (`StateGraph<TState>`) dans la couche Domain, inspiré de LangGraph : des nœuds transforment un état typé, des arêtes fixes et conditionnelles routent entre eux, et les cycles sont permis sous un circuit breaker.

Le moteur sert deux publics :

- **Le mode de process Graph** — `process: graph` en YAML (ou `ProcessType.Graph`) exécute la crew via `GraphProcessStrategy`, qui construit un graphe fixe à deux nœuds : exécuter la tâche suivante, puis router (relancer les tâches échouées, boucler ou terminer). Il se configure par le bloc `graphConfig`.
- **Vos propres workflows C#** — construisez n'importe quel `StateGraph<TState>` avec vos nœuds et vos arêtes conditionnelles, compilez-le et exécutez-le.

### Positionnement par rapport aux autres stratégies

| Stratégie | Granularité | Cycles | Routage conditionnel | Circuit breaker |
|-----------|-------------|--------|----------------------|-----------------|
| Sequential | Crew | Non | Non | Non |
| Hierarchical | Crew | Boucle de revue (3 revues) | LLM manager | Non |
| Parallel | Crew | Non | Non | Non |
| **Graph** | **Crew** | **Oui (cycles de retry)** | **Fixe en YAML ; libre avec l'API C#** | **Oui (intégré)** |
| FSM | Tâche (brique Domain) | Oui (guards) | Oui (événements/guards) | Oui (intégré) |

La FSM modélise le cycle interne d'une tâche mais n'est branchée dans aucune stratégie aujourd'hui (voir [Orchestration FSM](./fsm.md)) ; le mode Graph est le consommateur de `CircuitBreakerPolicy` à l'exécution.

## Architecture

### Couche Domain — Moteur générique

Les composants du moteur se trouvent dans `Orkeon.Domain.Graph` :

| Classe | Rôle |
|--------|------|
| `StateGraph<TState>` | Définition du graphe : `AddNode`, `AddEdge`, `AddConditionalEdge`, `Compile` ; sentinelles `StartNode` (`"__start__"`) et `EndNode` (`"__end__"`) |
| `GraphNode<TState>` | Nœud de traitement : `Func<TState, CancellationToken, Task<TState>>` |
| `IGraphEdge<TState>` | Interface de routage (fixe ou conditionnel) |
| `FixedEdge<TState>` (interne) | Arête inconditionnelle vers un nœud cible — construite via `AddEdge` |
| `ConditionalEdge<TState>` (interne) | Arête avec une fonction de routage `Func<TState, string>` — construite via `AddConditionalEdge` |
| `GraphRunner<TState>` | Moteur d'exécution avec circuit breaker et observabilité (`RunAsync`) |
| `GraphExecutionResult<TState>` | Résultat : `FinalState`, `Trace`, `TotalTransitions`, `Duration` |
| `NodeCompletedEventArgs<TState>`, `GraphCircuitBrokenEventArgs` | Charges utiles de `OnNodeCompleted` / `OnCircuitBroken` |
| `GraphCircuitBrokenException` | Exception levée quand le circuit breaker se déclenche (`NodeName`, `TransitionCount`, `Trace`) |

`TState` doit être un type référence (`where TState : class`) ; les nœuds mutent et renvoient généralement la même instance. `new StateGraph<TState>()` sans policy utilise `CircuitBreakerPolicy.Strict`.

`Compile()` valide la structure : une arête doit partir de `StartNode`, chaque nœud a exactement une arête sortante, et les arêtes fixes doivent viser un nœud enregistré (ou `EndNode`). Une arête conditionnelle qui déclare ses `possibleTargets` lève à l'exécution si le routeur renvoie un nom hors de cette liste.

### Couche Domain — Configuration

| Classe | Rôle |
|--------|------|
| `GraphConfig` | DTO immuable du bloc `graphConfig` (`MaxRetryCycles` = 2, `CircuitBreakerPreset` = `"strict"`, surcharges nullables) |

`GraphConfig?` est porté par `CrewConfiguration.GraphConfig` et par l'agrégat `Crew` (`Crew.GraphConfig`, réglable avec `CrewBuilder.WithGraphConfig(...)`).

### Couche Infrastructure — Intégration

| Classe | Rôle |
|--------|------|
| `GraphProcessStrategy` | Implémente `IProcessStrategy` (`ExecuteSequentialAsync`), construit et exécute le graphe de la crew |
| `CrewGraphState` | État typé qui traverse le graphe (tâches, agents, sorties, retries, compteurs de tokens) |
| `GraphYamlConfig` | Modèle YAML de la section `graphConfig` |
| `CircuitBreakerPolicyFactory.ResolveGraph` | Résout la policy effective à partir de `graphConfig`, du `circuitBreaker` de la crew et du repli |

### Où le mode est branché

| Endroit | Rôle |
|--------|-------------|
| `ProcessType.Graph` | Le membre du value object (`"Graph"`) |
| `YamlCrewMapper.ParseProcessType` | `process: graph` (insensible à la casse) |
| `ProcessStrategyFactory` | `"Graph"` → `GraphProcessStrategy` |
| `SequentialCrewOrchestrator` | Dirige `"Graph"` vers `ExecuteSequentialAsync` |
| `AddOrkeonInfrastructure()` | Enregistre `GraphProcessStrategy` (scoped) |

## Topologie du graphe

```
START ──► [execute_task] ──► [route] ──┬──► [execute_task]   (tâches en attente, ou une échouée remise en tête)
                                       │
                                       └──► END              (plus rien)
```

Le nœud `execute_task` retire la tâche suivante (dans l'ordre résolu depuis le plan ou les `dependencies` déclarées), choisit son agent — l'`agent:` de la tâche s'il est déclaré, sinon le sélecteur configuré (round-robin par défaut, voir la [sélection d'agent](./process-types.md#qui-exécute-une-tâche-qui-ne-nomme-aucun-agent)) — l'exécute et accumule le résultat dans `CrewGraphState`. Les agents avec `allowDelegation: true` reçoivent les outils de délégation.

Le nœud `route` inspecte l'état :

- Si la tâche qui vient de tourner a échoué avec des retries restants → la remet en tête de file, pour qu'elle soit relancée avant la tâche suivante, et reboucle
- S'il reste des tâches dans la file → reboucle vers `execute_task`
- Sinon → route vers END

La sortie de la crew est le dernier résultat produit. Une tâche qui échoue encore après ses retries reste dans les sorties comme échouée et **fait échouer la crew**, comme dans tous les modes : `Success = false`, une erreur qui la nomme, un hook `Failed`, code de sortie 2. Les tâches qui en dépendent sont **sautées**. Comme une tâche échouée est relancée avant la suivante, une dépendante ne voit jamais qu'une dépendance réussie ou abandonnée.

## Circuit breaker

Le circuit breaker est intégré dans `GraphRunner<TState>` et vérifie trois conditions avant chaque nœud :

### Mécanismes de protection

| Mécanisme | Paramètre | Description |
|-----------|-----------|-------------|
| Transitions max | `MaxTransitions` | Nombre total d'exécutions de nœuds |
| Détection de cycles | `MaxStateVisits` | Nombre maximal de visites d'un même nœud (0 le désactive) |
| Durée totale | `MaxTotalDuration` | Durée de vie maximale du run (`TimeSpan.Zero` la désactive) |

Le StateGraph réutilise `CircuitBreakerPolicy` de `Orkeon.Domain.Common.StateMachine` (le même record que la FSM) ; ses `StateTimeout` et `UseDegradedMode` ne sont pas utilisés par le runner du graphe.

En mode Graph, chaque tentative de tâche est une visite de `execute_task` (et une de `route`), donc :

- `MaxStateVisits` plafonne le nombre de **tentatives de tâches** sur tout le run ;
- `MaxTransitions` les plafonne à la moitié de sa valeur (deux exécutions de nœuds par tentative).

Sans `maxStateVisits` / `maxTransitions` explicites, les deux sont **calculés depuis la crew** : `tâches × (1 + maxRetryCycles)` visites — le maximum qu'une crew peut faire, chaque tâche épuisant ses retries — et `2 × visites + 1` transitions. Une crew saine n'est jamais coupée, quelle que soit sa taille ; une vraie boucle déclenche encore le disjoncteur. Une valeur explicite (dans `graphConfig`, sinon dans un `circuitBreaker` de crew) l'emporte. `MaxTotalDuration` reste celle du preset — 10 minutes en Strict, le défaut — une borne de coût que `maxTotalDurationSeconds` surcharge. Quand une condition est violée, une `GraphCircuitBrokenException` est levée avec la trace complète ; `GraphProcessStrategy` l'intercepte et renvoie un `CrewOutput` en échec (« Graph execution stopped by circuit breaker: … ») qui conserve les sorties et l'usage de tokens produits jusque-là.

### Presets

Le preset fournit ce qui n'est pas calculé — la durée totale d'abord. Ses plafonds de visites et de transitions ne s'appliquent que là où la stratégie reçoit une politique fixe (`GraphProcessStrategy.CircuitPolicy`, en C#) et où la crew ne porte aucune configuration.

| Preset | MaxTransitions | MaxStateVisits | MaxTotalDuration |
|--------|---------------|----------------|------------------|
| `Strict` (défaut) | 50 | 5 | 10 min |
| `Default` | 100 | 10 | 30 min |
| `Permissive` | 1000 | 50 | 2 h |

### Observabilité

Le `GraphRunner<TState>` expose deux événements :

- `OnNodeCompleted` : levé après chaque nœud, avec `NodeCompletedEventArgs<TState>` (`NodeName`, `State`, `TransitionOrdinal`, `TraceSnapshot`)
- `OnCircuitBroken` : levé quand le circuit se déclenche, avec `GraphCircuitBrokenEventArgs` (`Reason`, `NodeName`, `TransitionCount`, `Trace`)

`GraphProcessStrategy` s'abonne aux deux pour une journalisation structurée (`LoggerMessage` généré). Les nœuds d'un graphe ne sont pas des tâches : la stratégie annonce en direct le démarrage de chaque tâche via `ICrewExecutionHook`, et rapporte les fins de tâches quand le graphe se termine.

## Retry contrôlé

`GraphProcessStrategy` ajoute un mécanisme de retry au-dessus du circuit breaker :

| Paramètre | Champ | Description |
|-----------|-------|-------------|
| Cycles de retry | `MaxRetryCycles` | Retries par tâche échouée (défaut 2) |

Fonctionnement :

1. Quand une tâche échoue, son compteur dans `RetryCounts` augmente ; tant qu'il reste ≤ `MaxRetryCycles`, la tâche va dans `FailedTaskIds`
2. Le nœud `route` la replace en **tête** de `PendingTaskIds` : elle est relancée avant la tâche suivante
3. Une tâche dont le compteur dépasse `MaxRetryCycles` est abandonnée : sa sortie en échec reste, elle fait échouer la crew, et ses dépendantes sont sautées
4. Chaque tentative est enregistrée (une sortie et une entrée d'usage par tentative) et compte pour le circuit breaker

## Configuration YAML

### Schéma `graphConfig`

Le bloc `graphConfig` se place à la racine du fichier de crew :

```yaml
name: "my-crew"
goal: "…"
process: "graph"

graphConfig:
  maxRetryCycles: int           # défaut : 2 — retries par tâche échouée
  circuitBreakerPreset: string  # "strict" (défaut) | "permissive" | "default"
  maxTransitions: int           # Défaut : calculé, 2 × visites + 1
  maxStateVisits: int           # Plafond de tentatives — défaut : calculé, tâches × (1 + maxRetryCycles)
  maxTotalDurationSeconds: int  # Durée totale en secondes (surcharge le preset)

agents:
  <agent_id>:
    # ... même schéma qu'en sequential
tasks:
  <task_id>:
    # ... même schéma qu'en sequential
```

### Hiérarchie de résolution

```
Avec un bloc graphConfig :
1. Champs explicites de graphConfig       (priorité la plus haute)
2. graphConfig.circuitBreakerPreset       (valeurs de base ; "strict" si absent ou inconnu)
   — un circuitBreaker de niveau crew est alors ignoré

Sans graphConfig :
3. circuitBreaker de niveau crew          (son preset + ses surcharges, voir FSM)
4. CircuitBreakerPolicy.Strict            (repli quand rien n'est configuré)
```

Quel que soit le niveau, `maxStateVisits` et `maxTransitions` non fixés sont **calculés depuis la crew** plutôt que pris dans le preset (voir [Circuit breaker](#circuit-breaker)) ; le preset fournit la durée. `maxRetryCycles` ne vient que de `graphConfig` (2 sinon).

### Flux de configuration jusqu'à l'exécution

Le bloc `graphConfig` voyage jusqu'au graphe en cours d'exécution :

1. `YamlCrewMapper` mappe le YAML dans `CrewConfiguration.GraphConfig` (le loader désérialise et délègue ; `CrewYamlConfig` en mono-fichier et `CrewSettingsYamlConfig` en multi-fichiers portent tous deux le bloc).
2. `CrewFactory` le reporte (ainsi que tout `circuitBreaker` de niveau crew) sur l'agrégat domaine `Crew`
   (`Crew.GraphConfig` / `Crew.CircuitBreaker`), pour qu'il survive jusqu'à l'exécution.
3. À l'exécution, `GraphProcessStrategy` lit la config **sur l'argument crew** et résout la
   `CircuitBreakerPolicy` effective + `MaxRetryCycles` via `CircuitBreakerPolicyFactory.ResolveGraph`.
   Lire depuis la crew (et non depuis l'instance de stratégie scoped partagée) empêche les réglages
   d'une crew de fuir entre exécutions concurrentes.

Quand la crew ne porte aucun des deux blocs, la stratégie utilise son propre `MaxRetryCycles` (2) et calcule les bornes de visites et de transitions sur la durée du preset Strict ; sa propriété `CircuitPolicy`, nulle par défaut, remplace cette politique calculée quand un appelant C# la fixe.

### Modèles YAML

| Modèle C# | Classe YAML | Fichier |
|-----------|-------------|---------|
| `GraphConfig` | `GraphYamlConfig` | `Configuration/Yaml/YamlConfigModels.cs` |

Le mapping est effectué par `YamlCrewMapper.MapGraphConfig()` (privée, `Configuration/Yaml/`).

## Utilisation en code C#

### Utilisation directe de StateGraph (framework générique)

```csharp
using Orkeon.Domain.Graph;
using Orkeon.Domain.Common.StateMachine;

// Définir un type d'état
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
using Orkeon.Application.Interfaces;           // ICrewFactory
using Orkeon.Application.Interfaces.Services;  // ICrewOrchestrationService, CrewInput

// Charger la crew (process: graph + graphConfig) — le chemin est un chemin VFS
var crewFactory = serviceProvider.GetRequiredService<ICrewFactory>();
var crew = await crewFactory.CreateFromFileAsync("/workspace/config.yaml");

// L'orchestrateur résout GraphProcessStrategy via le ProcessStrategyFactory
var orchestrator = serviceProvider.GetRequiredService<ICrewOrchestrationService>();
var output = await orchestrator.KickoffAsync(crew.Id, CrewInput.Empty());

Console.WriteLine(output.Succeeded ? output.FinalOutput : output.Error);
```

L'équivalent Fluent Builder est `new CrewBuilder().Process(ProcessType.Graph).WithGraphConfig(new GraphConfig { MaxRetryCycles = 3 })…`.

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

Le même moteur pilote le pipeline RAG correctif (`CorrectiveRagPipeline` sur `StateGraph<RagGraphState>`, voir [Pipeline RAG](../architecture/rag-pipeline.md)). Le DSL de scripting a son propre littéral `stateGraph()` (`graph.d.ts`), une implémentation JavaScript distincte configurée par un `graphConfig` de limites de transitions — voir [Scripting](../architecture/scripting.md).

## Exemple 102

L'exemple [102-graph-orchestration](https://github.com/orkeon/orkeon/blob/main/examples/09-experimental/102-graph-orchestration/) démontre l'intégration complète avec :

- `process: "graph"` pour activer `GraphProcessStrategy`
- `graphConfig` avec `maxRetryCycles: 2` et le preset « strict »
- 4 agents (Data Collector, Data Validator, Insight Analyst, Report Writer)
- 4 tâches aux dépendances linéaires
- Retry automatique des tâches échouées (échecs transitoires de web scraping)

Voir les fichiers `config.yaml` et `README.md` de l'exemple pour la syntaxe complète.

## Relation avec l'existant

### FSM TaskExecutionStateMachine

Le StateGraph et la FSM modélisent des niveaux différents :

- **FSM** : l'exécution interne d'une tâche (Assigned → Executing → ToolCalling → Validating → Completed) — une brique Domain, pilotée par aucune stratégie aujourd'hui
- **StateGraph** : le flux entre tâches (quelle tâche exécuter, quand relancer, quand terminer)

Quand `GraphProcessStrategy` exécute une tâche, il appelle `IAgentExecutionService.ExecuteTaskAsync()`, dont la boucle d'agent borne la tâche (`maxIter`, arrêt sur erreurs identiques).

### SequentialCrewOrchestrator

`GraphProcessStrategy` est l'une des six implémentations de `IProcessStrategy`. L'orchestrateur utilise le `ProcessStrategyFactory` pour choisir la stratégie d'après le `ProcessType`, puis appelle son point d'entrée `ExecuteSequentialAsync` :

```
ProcessStrategyFactory.CreateStrategy(processType) switch
{
    "Sequential"   → SequentialProcessStrategy
    "Hierarchical" → HierarchicalProcessStrategy
    "Parallel"     → ParallelProcessStrategy
    "Consensual"   → ConsensualProcessStrategy
    "Graph"        → GraphProcessStrategy
    "Autonomous"   → AutonomousProcessStrategy
}
```

Le code client choisit via `ProcessType.Graph` ou `process: "graph"` dans le YAML.

## Tests

| Fichier de test | Couverture |
|-----------------|-----------|
| `Orkeon.Domain.Tests/Graph/StateGraphTests.cs` (18) | Flux linéaire, routage conditionnel, boucles contrôlées, circuit breaker (transitions max, détection de cycles), observabilité (OnNodeCompleted, OnCircuitBroken), annulation, validation du graphe, mutation d'état |
| `Orkeon.Infrastructure.Tests/Strategies/GraphProcessStrategy/GraphProcessStrategyTests.cs` (17) | Chemin nominal (0, 1, N tâches), retry contrôlé (succès après retry, abandon après le max), intégration du circuit breaker, points d'entrée non supportés, tâches manquantes, agents manquants |
| `Orkeon.Infrastructure.Tests/Configuration/CircuitBreakerPolicyFactoryGraphTests.cs` (5) | Priorité de `ResolveGraph` (graphConfig, circuitBreaker de la crew, repli) |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --filter "FullyQualifiedName~StateGraph"
dotnet test tests/core/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GraphProcessStrategy"
```
