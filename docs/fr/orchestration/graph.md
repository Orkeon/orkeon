> 🇬🇧 [English version](../../orchestration/graph.md)

> **Voir aussi** : [Guide comparatif ProcessTypes](./process-types.md) · [Orchestration FSM](./fsm.md) · [Schéma YAML](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)

# Orchestration par graphe d'états typé (StateGraph)

## Vue d'ensemble

Orkeon fournit un moteur de graphe d'états typé (`StateGraph<TState>`) dans la couche Domain, inspiré de LangGraph : des nœuds transforment un état typé, des arêtes fixes et conditionnelles routent entre eux, et les cycles sont permis sous un circuit breaker.

Le moteur sert deux publics :

- **Le mode de process Graph** — `process: graph` en YAML (ou `ProcessType.Graph`) exécute la crew via `GraphProcessStrategy`, qui construit un nœud par tâche, enchaînés dans l'ordre d'exécution (les tentatives d'une tâche sont son propre `retry:`, pas une boucle de son nœud). Il est décrit et borné par le bloc `graph:` (nœuds, arêtes, budgets ; [ADR-018](../adr/ADR-018-graph-engines-shared-topology-and-yaml-graph-block.md)).
- **Vos propres workflows C#** — construisez n'importe quel `StateGraph<TState>` avec vos nœuds et vos arêtes conditionnelles, compilez-le et exécutez-le.

### Positionnement par rapport aux autres stratégies

| Stratégie | Granularité | Cycles | Routage conditionnel | Circuit breaker |
|-----------|-------------|--------|----------------------|-----------------|
| Sequential | Crew | Non | Non | Non |
| Hierarchical | Crew | Boucle de revue (3 revues) | LLM manager | Non |
| Parallel | Crew | Non | Non | Non |
| **Graph** | **Crew** | **Oui (une arête qui revient sur un nœud, bornée par `maxVisits`)** | **Arêtes de décision (`when`) en YAML ; tout routeur avec l'API C#** | **Oui (intégré)** |
| FSM | Quelconque (moteur Domain générique) | Oui (guards) | Oui (événements/guards) | Oui (intégré) |

La FSM est un moteur générique (`orkeon forge` tourne dessus) qu'aucune stratégie de crew n'utilise (voir [FSM](./fsm.md)) ; le mode Graph partage sa `CircuitBreakerPolicy`.

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
| `NodeEnteredEventArgs`, `NodeCompletedEventArgs<TState>`, `GraphCircuitBrokenEventArgs` | Charges utiles de `OnNodeEntered` / `OnNodeCompleted` / `OnCircuitBroken` |
| `GraphCircuitBrokenException` | Exception levée quand le circuit breaker se déclenche (`NodeName`, `TransitionCount`, `Trace`) |

`TState` doit être un type référence (`where TState : class`) ; les nœuds mutent et renvoient généralement la même instance. `new StateGraph<TState>()` sans policy utilise `CircuitBreakerPolicy.Strict`.

`Compile()` valide la structure : une arête doit partir de `StartNode`, chaque nœud a exactement une arête sortante, et les arêtes fixes doivent viser un nœud enregistré (ou `EndNode`). Une arête conditionnelle qui déclare ses `possibleTargets` lève à l'exécution si le routeur renvoie un nom hors de cette liste.

### Couche Domain — Configuration

| Classe | Rôle |
|--------|------|
| `GraphConfig` | DTO immuable du bloc `graph:` : `Start`, `Nodes` (`GraphNodeConfig` : id et clé de tâche), `Edges` (`GraphEdgeConfig` : from, to, `TaskCondition` facultative), `CircuitBreakerPreset` = `"strict"`, budgets nullables |

`GraphConfig?` est porté par `CrewConfiguration.GraphConfig` et par l'agrégat `Crew` (`Crew.GraphConfig`, réglable avec `CrewBuilder.WithGraphConfig(...)`).

### Couche Infrastructure — Intégration

| Classe | Rôle |
|--------|------|
| `GraphProcessStrategy` | Implémente `IProcessStrategy` (`ExecuteSequentialAsync`), construit et exécute le graphe de la crew |
| `CrewGraphState` | État typé qui traverse le graphe (tâches, agents, sorties, retries, compteurs de tokens) |
| `GraphYamlConfig` | Modèle YAML du bloc `graph:` (avec `GraphNodeYamlConfig` et `GraphEdgeYamlConfig`) |
| `CircuitBreakerPolicyFactory.ResolveGraph` | Résout la policy effective à partir de `graph:`, sinon le repli |

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
START ──► [first] ──► [second] ──► … ──► [last] ──► END
```

Une crew en process Graph construit un nœud par tâche (ADR-018). L'id d'un nœud est le nom de sa tâche (l'id de la tâche quand le nom manque ou est pris). Sans nœuds ni arêtes dans `graph:`, les nœuds sont enchaînés dans l'ordre topologique stable des `dependencies` déclarées (`CrewTaskSequencer`), et le dernier mène à END. Une crew sans tâche va de START à END.

Un nœud choisit son agent — l'`agent:` de la tâche s'il est déclaré, sinon le sélecteur configuré (round-robin par défaut, voir la [sélection d'agent](./process-types.md#qui-exécute-une-tâche-qui-ne-nomme-aucun-agent)) — exécute la tâche et accumule le résultat dans `CrewGraphState`. Les agents avec `allowDelegation: true` reçoivent les outils de délégation. Une tâche sautée par ses dépendances ou sa condition `when` passe quand même par son nœud.

Sans nœuds ni arêtes dans `graph:`, l'arête qui sort d'un nœud est simple : elle mène au nœud suivant, ou à END. Avec eux, la topologie est celle déclarée (voir le [schéma `graph:`](#schéma-graph)). Le graphe ne relance pas une tâche échouée : ses tentatives sont son propre `retry:`, appliqué sous la stratégie.

La sortie de la crew est le dernier résultat produit. Une tâche qui échoue encore après ses retries reste dans les sorties comme échouée et **fait échouer la crew**, comme dans tous les modes : `Success = false`, une erreur qui la nomme, un hook `Failed`, code de sortie 2. Les tâches qui en dépendent sont **sautées**. Le `retry:` d'une tâche s'exécute pendant son propre tour, avant que la suivante démarre : une dépendante ne voit donc jamais qu'une dépendance réussie ou abandonnée.

## Circuit breaker

Le circuit breaker est intégré dans `GraphRunner<TState>` et vérifie trois conditions avant chaque nœud :

### Mécanismes de protection

| Mécanisme | Paramètre | Description |
|-----------|-----------|-------------|
| Transitions max | `MaxTransitions` | Nombre total d'exécutions de nœuds |
| Détection de cycles | `MaxStateVisits` | Nombre maximal de visites d'un même nœud (0 le désactive) |
| Durée totale | `MaxTotalDuration` | Durée de vie maximale du run (`TimeSpan.Zero` la désactive) |

Le StateGraph réutilise `CircuitBreakerPolicy` de `Orkeon.Domain.Common.StateMachine` (le même record que la FSM) ; ses `StateTimeout` et `UseDegradedMode` ne sont pas utilisés par le runner du graphe.

En mode Graph, chaque entrée dans un nœud est une visite (les tentatives du `retry:` d'une tâche s'y déroulent), donc :

- `MaxStateVisits` plafonne les **tentatives d'une même tâche** (sa première exécution et ses retries) ;
- `MaxTransitions` plafonne les exécutions de nœuds sur tout le run : chaque tentative de chaque tâche.

Sans `maxVisits` / `maxTransitions` explicites, les deux sont **calculés depuis la crew** : la somme sur les tâches de `1 + retries` visites (les tentatives du `retry:` de chaque tâche comptent ; `tâches × (1 + retries)` quand elles réessaient toutes pareil) — le maximum qu'une crew peut faire — et `2 × visites + 1` transitions. Une crew saine n'est jamais coupée, quelle que soit sa taille ; une vraie boucle déclenche encore le disjoncteur. Une valeur explicite dans `graph:` l'emporte. `MaxTotalDuration` reste celle du preset — 10 minutes en Strict, le défaut — une borne de coût que `maxTotalDurationSeconds` surcharge. Quand une condition est violée, une `GraphCircuitBrokenException` est levée avec la trace complète ; `GraphProcessStrategy` l'intercepte et renvoie un `CrewOutput` en échec (« Graph execution stopped by circuit breaker: … ») qui conserve les sorties et l'usage de tokens produits jusque-là.

### Presets

Le preset fournit ce qui n'est pas calculé — la durée totale d'abord. Ses plafonds de visites et de transitions ne s'appliquent que là où la stratégie reçoit une politique fixe (`GraphProcessStrategy.CircuitPolicy`, en C#) et où la crew ne porte aucune configuration.

| Preset | MaxTransitions | MaxStateVisits | MaxTotalDuration |
|--------|---------------|----------------|------------------|
| `Strict` (défaut) | 50 | 5 | 10 min |
| `Default` | 100 | 10 | 30 min |
| `Permissive` | 1000 | 50 | 2 h |

### Limite des vérifications de topologie

Les deux moteurs vérifient la forme d'un graphe avec `GraphTopologyValidator` à sa construction (ADR-018). Une limite : une arête dont les cibles sont inconnues, une arête fonction d'un script ou une arête conditionnelle de `StateGraph` déclarée sans `possibleTargets`, est supposée atteindre END **et chaque nœud déclaré**. C'est au-delà de l'ADR-018 3.6, qui dit seulement qu'une telle arête atteint END. Dès qu'une arête fonction est atteignable depuis START, la vérification des orphelins et celle du chemin vers END sont donc inertes pour ce graphe : elles ne peuvent pas trouver un nœud qu'elle n'atteindrait jamais, puisque l'arête peut y mener. Les vérifications qui restent exactes sont les ids inconnus, les nœuds sans arête sortante, les ids réservés ou répétés et la règle de l'arête fixe. Déclarez `possibleTargets` sur une arête conditionnelle pour garder les vérifications complètes.

### Observabilité

Le `GraphRunner<TState>` expose trois événements :

- `OnNodeEntered` : levé quand un nœud va s'exécuter, une fois le disjoncteur franchi, avec `NodeEnteredEventArgs` (`NodeName`, `Visit` : combien de fois le nœud a été entré, cette entrée comprise). C'est la source de l'événement de run `graph.node.entered`
- `OnNodeCompleted` : levé après chaque nœud, avec `NodeCompletedEventArgs<TState>` (`NodeName`, `State`, `TransitionOrdinal`, `TraceSnapshot`)
- `OnCircuitBroken` : levé quand le circuit se déclenche, avec `GraphCircuitBrokenEventArgs` (`Reason`, `NodeName`, `TransitionCount`, `Trace`)

`GraphProcessStrategy` s'abonne aux deux pour une journalisation structurée (`LoggerMessage` généré). La stratégie informe aussi l'`IGraphNodeObserver` de l'hôte, s'il y en a un, de chaque nœud entré (`GraphNodeEntered` : le nœud, sa visite, l'id de la tâche) ; `orkeon run --events jsonl` l'écrit en ligne [`graph.node.entered`](../architecture/run-event-bus.md). La stratégie annonce en direct le démarrage de chaque tâche via `ICrewExecutionHook`, et rapporte les fins de tâches quand le graphe se termine.

## Retry

Les tentatives d'une tâche sont son propre bloc `retry:` (`attempts`, `backoffMs`, `backoff`), appliqué par un seul décorateur sous chaque stratégie, dans les six modes : voir [Retry d'une tâche](../architecture/yaml-schema.md#retry-dune-tâche). Le graphe n'a **aucune boucle de retry propre** : une tâche qui échoue après sa dernière tentative reste en échec dans la liste des tâches du run, fait échouer la crew, et ses dépendantes sont sautées.

| Quoi | Où |
|------|----|
| Tentatives d'une tâche | le `retry:` de la tâche (`attempts` exécutions au total, la première comprise) |
| Attente entre tentatives | `backoffMs` et `backoff` (`fixed` ou `exponential`), sur le `TimeProvider` de l'hôte, annulée avec le run |
| Visites d'un nœud | une par entrée dans le nœud (une boucle y revient) : les tentatives d'une tâche se font dans sa visite, mais comptent dans le budget par défaut (ci-dessous) |

Les bornes par défaut du breaker additionnent les tentatives : une crew dont les tâches réessaient n'est donc jamais coupée par elles : `maxVisits` vaut par défaut la somme sur les tâches de `1 + retries` (`retries` = `attempts - 1`), `maxTransitions` le double plus un. Un bloc `graphConfig` est refusé au chargement, avec un renvoi vers `graph:` ; une clé `maxRetryCycles` ou `maxCycles` est refusée comme clé qu'Orkeon ne lit pas : une borne sur les cycles est `maxVisits`. Chaque tentative rejouée d'une tâche est un `task.started` qui porte son `attempt`.

## Configuration YAML

### Schéma `graph:`

Le bloc `graph:` se place à la racine du fichier de crew (lu avec `process: graph` seulement : ailleurs il est refusé au chargement) :

```yaml
name: "my-crew"
goal: "…"
process: "graph"

graph:
  start: triage              # facultatif : le premier nœud par défaut
  nodes:                     # facultatif : absent = un nœud implicite par tâche, son id le nom de la tâche
    - { id: triage,   task: triage }
    - { id: fix,      task: fix }
    - { id: escalate, task: escalate }
  edges:
    - { from: triage, to: fix, when: { triage.category: [bug] } }
    - { from: triage, to: escalate }          # sans `when` : le « sinon »
    - { from: fix,      to: END }
    - { from: escalate, to: END }
  circuitBreakerPreset: strict   # "strict" (défaut) | "permissive" | "default"
  maxTransitions: 50             # défaut : calculé, 2 × visites + 1
  maxVisits: 5                   # visites d'UN nœud — défaut : calculé, somme sur les tâches de 1 + leurs retries
  maxTotalDurationSeconds: 600   # durée totale (surcharge le preset)
```

- **Nœuds.** Un nœud est `{id, task}` : `task` nomme une tâche de la crew, `START` et `END` sont refusés comme ids. Une tâche est portée par un nœud au plus ; quand `nodes` est donné, une tâche qu'aucun nœud ne nomme est refusée. `edges` sans `nodes` donne un nœud implicite par tâche. Un nœud est revisité par une arête qui y revient : à la revisite, la dernière sortie et la dernière décision l'emportent. Un nœud dont la tâche échoue arrête le run : il n'y a pas de routage sur échec (un retry est le `retry:` de la tâche).
- **Arêtes.** Plusieurs `edges` depuis un même `from` forment une seule arête conditionnelle, évaluée dans l'ordre de déclaration : la première dont le `when` tient l'emporte, et l'arête sans `when` est le « sinon ». Un `from` qui porte des arêtes `when` est une [tâche de décision](../architecture/decision-models.md) et a exactement une arête sans `when` ; une tâche ordinaire a exactement une arête. Le `when` garde la grammaire de celui de la tâche, avec son préfixe `<tâche>.` : `<tâche>.<question>: [option, ...]`, `<tâche>.<question>: ">= 0.7"`, `<tâche>.<question>.<option>: ">= 0.25"` ; la tâche est celle du `from`, et ses questions sont vérifiées au chargement. `otherwise` ne s'écrit pas : une arête sans `when` en tient lieu.
- **Refusé au chargement**, chaque message nommant le nœud : un `when` sur un `from` qui n'est pas une décision, une décision sans arête sans `when`, deux arêtes sans `when` depuis un même nœud, plusieurs arêtes sur une tâche ordinaire, un nœud orphelin, un nœud sans chemin vers `END`, une arête vers un nœud inconnu, un `graph:` hors de `process: graph`, et un `when:` ou des `dependencies:` au niveau tâche dans une crew qui a des nœuds ou des arêtes (les arêtes ordonnent les tâches, un second routeur les contredirait). Les fautes de topologie viennent de `GraphTopologyValidator`, toutes à la fois, et l'export vérifie les mêmes règles avant d'écrire.
- **Budgets.** `maxVisits` est le `MaxStateVisits` du Domain, compté par nœud : un nœud entré plus souvent que cela déclenche le disjoncteur. Non renseigné, `maxVisits` vaut la somme sur les tâches de `1 + retries` et `maxTransitions` deux fois plus un ; une valeur explicite l'emporte.
- **Clés refusées.** `graphConfig:` est refusé au chargement, avec un renvoi vers `graph:` ; `maxRetryCycles` et `maxCycles` sont refusés au chargement comme clés qu'Orkeon ne lit pas (une borne sur les cycles est `maxVisits` ; un retry est le `retry:` de la tâche).
- **Exécution.** Le moteur Domain exécute toute crew du process Graph, avec ou sans bloc `graph:` ; `JsStateGraph` n'exécute que le `stateGraph(...)` d'un script `.ork.ts`. Un run repris parcourt de nouveau le graphe : les exécutions d'une tâche déjà terminée sont servies depuis le checkpoint, clé `<tâche>#<n>` selon leur ordre, donc un nœud entré deux fois a deux entrées. L'export réécrit `graph:`, si bien qu'import, export, import donnent le même graphe.

Les `agents:` et `tasks:` suivent le schéma sequential ; une tâche de décision est une tâche avec un bloc `decision:` ([tâches de décision](../architecture/decision-models.md)).

### Hiérarchie de résolution

```
Avec un bloc graph :
1. Champs explicites de graph              (priorité la plus haute)
2. graph.circuitBreakerPreset            (valeurs de base ; "strict" si absent ou inconnu)

Sans bloc graph :
3. CircuitBreakerPolicy.Strict            (repli : ne fournit que la durée)
```

`graph:` est le seul réglage de circuit breaker d'une crew : un bloc `circuitBreaker:`, à la racine de la crew ou sur une tâche, est refusé au chargement. Quel que soit le niveau, `maxVisits` et `maxTransitions` non fixés sont **calculés depuis la crew** plutôt que pris dans le preset (voir [Circuit breaker](#circuit-breaker)) ; le preset fournit la durée. Une tâche échouée est relancée par son propre `retry:` (voir [Retry](#retry)), jamais par le graphe : un bloc `graphConfig` est refusé au chargement.

### Flux de configuration jusqu'à l'exécution

Le bloc `graph:` voyage jusqu'au graphe en cours d'exécution :

1. `YamlCrewMapper` mappe le YAML dans `CrewConfiguration.GraphConfig` (le loader désérialise et délègue ; `CrewYamlConfig` en mono-fichier et `CrewSettingsYamlConfig` en multi-fichiers portent tous deux le bloc).
2. `CrewFactory` le reporte sur l'agrégat domaine `Crew` (`Crew.GraphConfig`), pour qu'il survive
   jusqu'à l'exécution.
3. À l'exécution, `GraphProcessStrategy` lit la config **sur l'argument crew** et résout la
   `CircuitBreakerPolicy` effective via `CircuitBreakerPolicyFactory.ResolveGraph`.
   Lire depuis la crew (et non depuis l'instance de stratégie scoped partagée) empêche les réglages
   d'une crew de fuir entre exécutions concurrentes.

Quand la crew ne porte pas de bloc `graph:`, la stratégie calcule les bornes de visites et de transitions sur la durée du preset Strict ; sa propriété `CircuitPolicy`, nulle par défaut, remplace cette politique calculée quand un appelant C# la fixe.

### Modèles YAML

| Modèle C# | Classe YAML | Fichier |
|-----------|-------------|---------|
| `GraphConfig` | `GraphYamlConfig` | `Configuration/Yaml/YamlConfigModels.cs` |

Le mapping est effectué par `YamlCrewMapper.MapGraph()` (privée, `Configuration/Yaml/YamlCrewMapper.Graph.cs`), qui refuse d'un coup toutes les fautes du bloc ; l'export (`YamlCrewExporter`) le réécrit après la même vérification de topologie.

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

// Charger la crew (process: graph + graph:) — le chemin est un chemin VFS
var crewFactory = serviceProvider.GetRequiredService<ICrewFactory>();
var crew = await crewFactory.CreateFromFileAsync("/workspace/config.yaml");

// L'orchestrateur résout GraphProcessStrategy via le ProcessStrategyFactory
var orchestrator = serviceProvider.GetRequiredService<ICrewOrchestrationService>();
var output = await orchestrator.KickoffAsync(crew.Id, CrewInput.Empty());

Console.WriteLine(output.Succeeded ? output.FinalOutput : output.Error);
```

L'équivalent Fluent Builder est `new CrewBuilder().Process(ProcessType.Graph).WithGraphConfig(new GraphConfig { MaxStateVisits = 20 })…`.

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

Le même moteur pilote le pipeline RAG correctif (`CorrectiveRagPipeline` sur `StateGraph<RagGraphState>`, voir [Pipeline RAG](../architecture/rag-pipeline.md)). Le DSL de scripting a son propre littéral `stateGraph()` (`graph.d.ts`), une implémentation JavaScript distincte configurée par un `graphConfig` de limites de transitions (la clé propre au script, sans rapport avec le bloc `graph:` d'une crew) — voir [Scripting](../architecture/scripting.md).

## Exemple 102

L'exemple [102-graph-orchestration](https://github.com/orkeon/orkeon/blob/main/examples/09-experimental/102-graph-orchestration/) démontre l'intégration complète avec :

- `process: "graph"` pour activer `GraphProcessStrategy`
- un bloc `graph:` (nœuds, arêtes, preset « strict ») et un `retry:` de tâche
- 4 agents (Data Collector, Data Validator, Insight Analyst, Report Writer)
- 4 tâches aux dépendances linéaires
- Retry automatique des tâches échouées (échecs transitoires de web scraping)

Voir les fichiers `config.yaml` et `README.md` de l'exemple pour la syntaxe complète.

## Relation avec l'existant

### Les tâches dans le graphe

Le StateGraph modélise le flux entre tâches (quelle tâche exécuter, quand relancer, quand terminer) ; il ne modélise pas l'exécution propre d'une tâche. Quand `GraphProcessStrategy` exécute une tâche, il appelle `IAgentExecutionService.ExecuteTaskAsync()`, dont la boucle d'agent borne la tâche (`maxIter`, arrêt sur erreurs identiques, reprises de validation de sortie). Le [moteur FSM](./fsm.md) générique partage la `CircuitBreakerPolicy` du graphe mais n'exécute aucune tâche.

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
| `Orkeon.Infrastructure.Tests/Strategies/GraphProcessStrategy/GraphProcessStrategyTests.cs` (16) | Chemin nominal (0, 1, N tâches), retry contrôlé (succès après retry, abandon après le max), intégration du circuit breaker, points d'entrée non supportés, tâches manquantes, agents manquants |
| `Orkeon.Infrastructure.Tests/Configuration/CircuitBreakerPolicyFactoryGraphTests.cs` (4) | `ResolveGraph` : presets, surcharges, repli |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --filter "FullyQualifiedName~StateGraph"
dotnet test tests/core/Orkeon.Infrastructure.Tests/Orkeon.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GraphProcessStrategy"
```
