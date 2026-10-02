> 🇬🇧 [English version](../../orchestration/autonomous.md)

> **Voir aussi** : [Guide comparatif ProcessTypes](./process-types.md) · [Schéma YAML](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)

# Orchestration agentique autonome

## Vue d'ensemble

Orkeon fournit un mode d'orchestration autonome (`ProcessType.Autonomous`) : pour chaque tâche, un LLM manager choisit l'agent qui la réclame ; quand cet agent échoue et autorise la délégation, la tâche est confiée à un pair via un canal agent-à-agent. Toute l'exécution est contrainte par un `AgentExecutionBudget` multi-dimensionnel qui garantit la terminaison.

> **API expérimentale.** Les types du budget, `IAgentChannel` et ses records, `AutonomousProcessStrategy` et `SpawnAgentTool` portent `[Experimental("ORKEXP002")]` : le code qui les utilise doit acquitter le diagnostic (voir les [API expérimentales](../reference/experimental-apis.md)). Exécuter une crew YAML en `process: autonomous` ne demande rien.

Contrairement au mode Sequential, où l'ordre déclaré et l'`agent:` de la tâche décident, le mode Autonomous laisse le LLM manager décider qui fait quoi, et donne à une tâche échouée une deuxième chance auprès d'un pair. L'orchestrateur fait respecter le budget et collecte les résultats.

### Positionnement par rapport aux autres stratégies

| Stratégie | Décision de routage | Délégation | Spawn dynamique | Budget multi-dimensionnel | Communication A2A |
|-----------|--------------------|-----------------------|-----------------|------------------------|--------------------|
| Sequential | Agent déclaré, sinon sélecteur | Via `delegate_work_to_coworker` (agents avec `allowDelegation`) | Non | Non | Non |
| Hierarchical | LLM manager | Non (revue du manager à la place) | Non | Non | Non |
| Graph | Agent déclaré, sinon sélecteur | Via `delegate_work_to_coworker` | Non | Non (circuit breaker) | Non |
| **Autonomous** | **LLM manager** | **Sur échec, vers un pair (profondeur bornée)** | **Via `SpawnAgentTool` (enregistré par l'hôte)** | **Oui (5 dimensions)** | **Requête/Réponse** |

## Architecture

### Couche Domain — Budget d'exécution

| Classe | Fichier | Rôle |
|--------|---------|------|
| `AgentExecutionBudget` | `Autonomous/AgentExecutionBudget.cs` | Budget multi-dimensionnel : appels d'outils, profondeur de délégation, durée, tokens, spawns |
| `BudgetSnapshot` | `Autonomous/AgentExecutionBudget.cs` | Instantané immuable pour les logs et la télémétrie |
| `BudgetExhaustedException` | `Autonomous/AgentExecutionBudget.cs` | Exception typée avec `BudgetDimension` (ToolCalls, DelegationDepth, WallTime, Tokens, SpawnedAgents) |
| `BudgetDimension` | `Autonomous/AgentExecutionBudget.cs` | Enum des 5 dimensions du budget |

Les limites sont en `init` uniquement ; les compteurs sont thread-safe (`Interlocked`). Chaque appel `Record*` (`RecordToolCall`, `RecordDelegation`, `RecordSpawn`, `RecordTokens(n)`) vérifie la durée, incrémente son compteur et lève `BudgetExhaustedException` quand le compteur dépasse sa limite. `ThrowIfExhausted()` est la vérification préalable qui ne consomme rien, `AssertWallTime()` ne vérifie que l'horloge, et `IsExhausted` indique si une dimension est épuisée. L'horloge est injectable (`TimeProvider`) pour les tests.

### Couche Domain — ProcessType

`ProcessType.Autonomous` est un membre du value object, et `IProcessStrategy` a un point d'entrée dédié :

```csharp
Task<CrewOutput> ExecuteAutonomousAsync(
    Crew crew,
    AgentExecutionBudget budget,
    IReadOnlyDictionary<string, string>? inputVariables = null,
    CancellationToken cancellationToken = default);
```

### Couche Application — Communication A2A

| Classe | Fichier | Rôle |
|--------|---------|------|
| `IAgentChannel` | `Interfaces/Services/IAgentChannel.cs` | Canal requête/réponse bidirectionnel entre agents |
| `AgentChannelRequest` | `Interfaces/Services/IAgentChannel.cs` | Requête avec ID de corrélation, intention, charge utile, métadonnées optionnelles (`Create(from, to, intent, payload)`) |
| `AgentChannelResponse` | `Interfaces/Services/IAgentChannel.cs` | Réponse corrélée (`Ok(...)` / `Fail(...)`) avec succès/erreur |
| `NullMemoryScope` | `Context/NullMemoryScope.cs` | Singleton no-op pour les contextes sans mémoire |

`IAgentChannel` offre trois opérations :

- **RequestAsync** : requête/réponse avec un timeout (30 s par défaut) ; une cible qui ne répond pas à temps lève `TimeoutException`, une cible sans handler enregistré reçoit une réponse en échec
- **RegisterHandler** : enregistrement d'un handler par agent (renvoie un `IDisposable` qui le désenregistre)
- **BroadcastAsync** : notification aux autres agents d'une crew, sans réponse attendue ; `InMemoryAgentChannel` atteint les agents enregistrés via `RegisterCrewMember(crewId, agentId)`

### Couche Infrastructure — Stratégie autonome

| Classe | Fichier | Rôle |
|--------|---------|------|
| `AutonomousProcessStrategy` | `Crew/Strategies/AutonomousProcessStrategy.cs` | Implémente `IProcessStrategy.ExecuteAutonomousAsync` |
| `InMemoryAgentChannel` | `Communication/InMemoryAgentChannel.cs` | Implémentation in-process du canal A2A (lock-free, `ConcurrentDictionary`), enregistrée comme `IAgentChannel` scoped |
| `SpawnAgentTool` | `Tools/SpawnAgentTool.cs` | Outil qui permet à un agent de créer un sous-agent |
| `DelegateWorkTool` | `Tools/DelegateWorkTool.cs` | Accepte un `AgentExecutionBudget` optionnel et appelle `RecordDelegation()` avant chaque délégation |

### Branchement

| Endroit | Rôle |
|--------|-------------|
| `ProcessStrategyFactory` | `"Autonomous"` → `AutonomousProcessStrategy` |
| `SequentialCrewOrchestrator` | Dirige `"Autonomous"` vers `ExecuteAutonomousAsync` avec `AgentExecutionBudget.Permissive` |
| `AddOrkeonInfrastructure()` | Enregistre `AutonomousProcessStrategy` et `IAgentChannel` → `InMemoryAgentChannel` (scoped) |

## Flux d'exécution

```
                    ┌──────────────────────────────────────────────────┐
                    │         AutonomousProcessStrategy                │
                    │                                                  │
  tâches de la      │  enregistrer chaque agent sur l'IAgentChannel    │
  crew (ordre des   │  pour chaque tâche :                             │
  dépendances)      │    0. budget.AssertWallTime()                    │
                    │    1. manager.AssignTaskAsync (LLM) → agent      │
                    │    2. budget.RecordToolCall()                    │
                    │    3. ExecuteTaskAsync(agent, tâche)             │
                    │    4. échec + AllowDelegation + profondeur + pair│
                    │       ├─ budget.RecordDelegation()               │
                    │       ├─ channel.RequestAsync("delegate",        │
                    │       │    premier autre agent, timeout 2 min)   │
                    │       └─ le pair exécute sous un budget enfant   │
                    │    5. BudgetExhausted dans la tâche → sortie     │
                    │       partielle « [BUDGET EXHAUSTED] … », arrêt  │
                    │  BudgetExhausted entre deux tâches → arrêt       │
                    └──────────────────────────────────────────────────┘
```

Les détails qui comptent pour dimensionner un run :

- **L'assignation** passe par `IManagerAgent` (`LlmBasedManager`), qui appelle le LLM enregistré par l'hôte, comme en mode Hierarchical ; l'`agent:` d'une tâche n'est pas consulté. Une erreur du LLM retombe sur le premier agent.
- **Appels d'outils** : la stratégie enregistre **un** `RecordToolCall()` par tâche distribuée (l'assignation) ; les appels d'outils de l'agent dans sa boucle ne sont pas imputés au budget. `MaxToolCalls` borne donc le nombre de tâches tentées.
- **Profondeur de délégation** : `RecordDelegation()` incrémente un compteur commun à la crew qui n'est jamais décrémenté, si bien que `MaxDelegationDepth` se comporte comme le nombre de délégations permises dans le run. Le délégué est le **premier autre agent** dans l'ordre de la crew ; il reprend la tâche elle-même — son plan aussi, quand la crew planifie (GAP-31) —, dans le contexte de la tentative échouée, dérivé de lui (GAP-21) : l'id et la portée mémoire de la crew, les variables d'entrée du run et les sorties déjà produites, plus `delegation_context` et `autonomous_child_budget_snapshot`. Il rappelle la mémoire de la crew comme toute exécution qui répond à une tâche, sa sortie — quand elle réussit — est le résultat de la tâche, rangée une fois sous le pair avec `memory: true`, et il ne délègue pas plus loin. L'agent qui a échoué fait échouer la tâche (`AgentFailedTaskEvent`), la tâche est assignée au pair, et se termine sous lui ([Événements](../architecture/domain-events.md)).
- **Tokens** : les tokens d'une exécution déléguée sont imputés au budget enfant et au budget parent ; les exécutions directes alimentent la télémétrie de tokens de la crew mais pas `MaxTokensConsumed`.
- **Résultat** : la sortie de la crew concatène les sorties des tâches. Une tâche en échec — directement, sans pair à qui déléguer, ou déléguée à un pair qui échoue aussi — **fait échouer la crew**, et les tâches qui en dépendent sont sautées sans être réclamées. Un budget épuisé fait aussi échouer la crew, son erreur commençant par `Execution budget exhausted: <dimension>` et nommant chaque tâche qu'il n'a pas atteinte ; `ICrewExecutionHook` reçoit `Failed` avec cette raison (`Canceled` reste réservé à une vraie annulation). `orkeon run` sort en 2 dans les deux cas.

## Budget multi-dimensionnel

Le budget contrôle 5 dimensions indépendantes. Chaque dimension a un compteur thread-safe et une limite. L'épuisement de n'importe quelle dimension lève `BudgetExhaustedException`.

| Dimension | Default | Strict | Permissive | Description |
|-----------|--------|--------|------------|-------------|
| MaxToolCalls | 15 | 8 | 50 | Nombre maximal d'appels d'outils |
| MaxDelegationDepth | 2 | 1 | 4 | Profondeur de délégation maximale (A→B→C = 2) |
| MaxWallTime | 5 min | 2 min | 15 min | Durée maximale (temps réel) |
| MaxTokensConsumed | 16 000 | 8 000 | 64 000 | Tokens totaux (prompt + complétion) |
| MaxSpawnedAgents | 3 | 1 | 10 | Nombre maximal de sous-agents créés |

### Presets

```csharp
#pragma warning disable ORKEXP002
// Production : limites conservatrices
var strict = AgentExecutionBudget.Strict;

// Développement : limites larges — ce qu'ICrewOrchestrationService utilise toujours
var permissive = AgentExecutionBudget.Permissive;

// Personnalisé
var custom = new AgentExecutionBudget
{
    MaxToolCalls = 20,
    MaxDelegationDepth = 3,
    MaxWallTime = TimeSpan.FromMinutes(10),
    MaxTokensConsumed = 32_000,
    MaxSpawnedAgents = 5
};

// Un budget autre que Permissive : appeler la stratégie directement
var strategy = serviceProvider.GetRequiredService<AutonomousProcessStrategy>();
var output = await strategy.ExecuteAutonomousAsync(crew, custom, inputVariables: null, ct);
#pragma warning restore ORKEXP002
```

`KickoffAsync` ne prend pas de budget : une crew exécutée via `ICrewOrchestrationService` (le runner, l'hôte, Studio) reçoit toujours `Permissive`. Appeler `ExecuteAutonomousAsync` directement contourne ce que l'orchestrateur ajoute autour d'un run (planification, checkpointing, transitions d'état de la crew).

### Budgets enfants

Quand la stratégie délègue, ou que `SpawnAgentTool` crée un agent, le sous-agent reçoit un budget enfant dérivé :

```csharp
var childBudget = parentBudget.CreateChildBudget();
// MaxToolCalls       = max(1, parent.Max - parent.Current)
// MaxDelegationDepth = max(0, parent.Max - parent.Current - 1)
// MaxWallTime        = parent.MaxWallTime - parent.Elapsed
// MaxTokensConsumed  = max(100, parent.Max - parent.Current)
// MaxSpawnedAgents   = max(0, parent.Max - parent.Current)
```

Un budget enfant est une copie indépendante de l'allocation **restante** du parent, avec ses propres compteurs : deux enfants dérivés l'un après l'autre reçoivent chacun la totalité du reste. Le plafond du parent n'est tenu que là où la consommation lui est aussi imputée — les tokens d'une exécution déléguée, et chaque spawn.

## Communication A2A (IAgentChannel)

Le canal bidirectionnel permet aux agents de communiquer en mode requête/réponse :

```csharp
// L'agent A demande une clarification à l'agent B
var request = AgentChannelRequest.Create(
    from: agentA.Id,
    to: agentB.Id,
    intent: "clarify",
    payload: "Quel format de données pour le rapport ?");

var response = await channel.RequestAsync(request, timeout: TimeSpan.FromSeconds(30));

if (response.Success)
    Console.WriteLine($"Réponse : {response.Payload}");
```

### Intentions

L'intention est une chaîne libre. Dans un run autonome, le handler que chaque agent enregistre comprend :

| Intention | Traitement |
|--------|-------------|
| `delegate` | Exécute la charge utile comme une tâche sous un budget enfant et renvoie sa sortie (ou `Budget exhausted: …`) |
| toute autre (`clarify`, …) | Accusé de réception : `Agent <role> acknowledges: <intent>` |
| `broadcast` | L'intention que `BroadcastAsync` appose sur ses notifications |

L'implémentation `InMemoryAgentChannel` est in-process et lock-free. Pour un déploiement multi-hôtes, implémentez `IAgentChannel` avec Redis Streams ou un broker de messages.

## SpawnAgentTool — Auto-création d'agents

Un outil (`spawn_agent`) qui permet à un agent de créer à la volée un sous-agent spécialisé.
La classe est livrée dans `Orkeon.Infrastructure` mais **ni la stratégie ni aucune racine de
composition livrée ne la fournit** : son constructeur prend l'id de l'agent parent, l'id de
la crew, l'`AgentExecutionBudget` qu'il fait respecter et un `IAgentFactory` (plus, pour un
spawn synchrone, un `IAgentExecutionService`), si bien qu'un hôte qui veut l'auto-création en
construit un par agent et l'ajoute à cet agent — voir l'[inventaire des outils](../tools/inventory.md).
Le budget qu'il fait respecter est celui que l'hôte lui remet, pas le budget du run.

```csharp
// Le LLM de l'agent génère cet appel d'outil :
{
    "tool": "spawn_agent",
    "parameters": {
        "role": "data_analyst",
        "goal": "Analyser les tendances des ventes du T4",
        "task": "Produire un rapport CSV des ventes par région",
        "wait_for_result": true,
        "allow_delegation": false
    }
}
```

`role` et `goal` sont obligatoires ; `backstory` est facultatif. Chaque spawn appelle `RecordSpawn()` sur le budget (un budget épuisé refuse l'appel). L'agent créé l'est avec `MaxIterations = 5` et un budget enfant (transmis dans les métadonnées de la demande de spawn et dans le contexte d'exécution). Avec `wait_for_result: true` (le défaut), l'outil exécute la tâche et renvoie sa sortie ; `false` se contente de créer l'agent.

## Observabilité

### Métadonnées de sortie

Le `CrewOutput` en mode Autonomous inclut des métadonnées de budget, à côté de la télémétrie de tokens :

```json
{
    "process_type": "autonomous",
    "agent_count": 3,
    "budget_tool_calls": "12/50",
    "budget_delegation_depth": "1/4",
    "budget_tokens": "9200/64000",
    "budget_spawned": "0/10",
    "budget_exhausted": false
}
```

### BudgetSnapshot

`budget.ToSnapshot()` renvoie à tout moment un `BudgetSnapshot` immuable (`ToolCalls`/`MaxToolCalls`, `DelegationDepth`/`MaxDelegationDepth`, `TokensConsumed`/`MaxTokensConsumed`, `SpawnedAgents`/`MaxSpawnedAgents`, `Elapsed`/`MaxWallTime`, `IsExhausted`), journalisable et sérialisable.

### Journalisation structurée

Tous les événements clés sont journalisés via `LoggerMessage` :

- `Starting autonomous execution for crew {CrewId} (budget: {MaxToolCalls} tool calls, depth {MaxDepth})`
- `Agent {AgentId} claimed task {TaskId}: {Reason}`
- `Delegation: {From} → {To} for task {TaskId} (depth: {Depth})`
- `Task {TaskId} completed (success: {Success}, budget: {Snapshot})`
- `Budget exhausted for crew {CrewId}: dimension={Dimension}, {Message}`
- `Autonomous execution completed for crew {CrewId} in {Duration} (tool calls: {ToolCalls}, delegation depth: {Depth})`
- `SpawnAgentTool` journalise chaque spawn (parent, id de l'enfant, rôle) et la fin d'un spawn synchrone

Comme tous les modes, la stratégie rend aussi compte via `ICrewExecutionHook` : le démarrage d'une tâche quand un agent la réclame, les fins de tâches et l'issue de la crew quand le run se termine.

## Configuration YAML

```yaml
# Racine plate — pas d'enveloppe crew: ; agents: est un mapping indexé par id d'agent.
name: research-team
process: autonomous        # ← active le mode autonome
goal: "Produire un rapport de recherche complet"

agents:
  researcher:
    role: Researcher
    goal: "Trouver des sources fiables"
    allowDelegation: true   # une tâche échouée de cet agent est confiée à un pair
    tools: [web_search]

  analyst:
    role: Analyst
    goal: "Analyser et synthétiser les données"
    allowDelegation: true
    tools: [json_tool, csv_reader]

  writer:
    role: Writer
    goal: "Rédiger le rapport final"
    allowDelegation: false
```

`spawn_agent` n'est pas listé : le loader YAML résout les noms d'outils dans le registre d'outils, où aucun `SpawnAgentTool` n'est enregistré par défaut (un nom inconnu est ignoré avec un avertissement, ou fait échouer le chargement en mode strict des outils).

> **Note** : il n'existe pas de clé YAML `autonomousBudget` — le loader n'en parse aucune. Dans les crews YAML, le mode Autonomous tourne toujours avec `AgentExecutionBudget.Permissive` (50 appels d'outils, profondeur 4, 15 min, 64 000 tokens, 10 spawns). Dans le DSL de scripting, `crewBuilder().budget({...})` ne borne que la forme procédurale (`await crew.run()`) ; la forme déclarative l'ignore avec un avertissement.

> **Délégation par défaut** : la stratégie autonome délègue via l'`IAgentChannel`, pas via `ITaskDelegator` — le stub qui refuse toute demande (voir [Comportements par défaut](../getting-started/default-behaviors.md)) n'est pas consulté par ce mode.

## Complémentarité avec les autres modes

| Besoin | Mode recommandé |
|--------|----------------|
| Pipeline linéaire, déterminisme maximal | Sequential |
| Manager centralisé, revue qualité | Hierarchical |
| Tâches indépendantes, parallélisme | Parallel |
| Relance de tâches instables, run borné | Graph |
| **Assignation pilotée par LLM, deuxième chance auprès d'un pair, run borné par un budget** | **Autonomous** |

Le mode Autonomous est le moins déterministe. Pour les charges de production sensibles, préférez Sequential ou Hierarchical et réservez Autonomous aux cas où laisser le manager distribuer le travail apporte plus que le coût du non-déterminisme (recherche exploratoire, écriture créative, résolution de problèmes multi-domaines complexes).

## Tests

| Fichier de test | Couverture |
|-----------------|-----------|
| `Orkeon.Infrastructure.Tests/Strategies/CovAutonomous_AutonomousProcessStrategyTests.cs` | Points d'entrée, repli d'assignation, délégation via le canal, épuisement des appels d'outils et de la durée, télémétrie de tokens, handlers du canal |
| `Orkeon.Domain.Tests/Autonomous/AgentExecutionBudgetTimeProviderTests.cs` | Budget de durée avec une horloge contrôlable |
| `Orkeon.Infrastructure.Tests/Communication/CovStubs_InMemoryAgentChannelTests.cs` | Requête/réponse, timeout, handler absent, broadcast |
| `Orkeon.Infrastructure.Tests/Tools/SpawnAgentTool/SpawnAgentToolTests.cs` | Spawn imputé au budget parent, propagation du budget enfant (métadonnées, variables de contexte), décompte des tokens |
| `Orkeon.Infrastructure.Tests/Orchestration/CovAutonomous_SequentialCrewOrchestratorTests.cs` | Aiguillage des six modes (Autonomous avec le budget Permissive) |

---

> **Voir aussi** : [Guide comparatif ProcessTypes](./process-types.md) · [Orchestration Graph](./graph.md) · [Schéma YAML](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)
