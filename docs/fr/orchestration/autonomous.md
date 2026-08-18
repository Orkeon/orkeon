> 🇬🇧 [English version](../../orchestration/autonomous.md)

> **Voir aussi** : [Guide comparatif ProcessTypes](./process-types.md) · [Schéma YAML](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)

# Orchestration agentique autonome

## Vue d'ensemble

Orkeon fournit un mode d'orchestration autonome (`ProcessType.Autonomous`) où les agents s'auto-organisent pour réclamer des tâches, déléguer récursivement à leurs pairs, et spawner des sous-agents spécialisés à la volée. Toute l'exécution est contrainte par un `AgentExecutionBudget` multi-dimensions qui garantit la terminaison.

> **Défaut DI à connaître** : de base, `ITaskDelegator` est un stub qui refuse toute
> demande de délégation (il avertit une fois, avec le correctif) — voir
> [Comportements par défaut](../getting-started/default-behaviors.md) avant de câbler
> une crew qui délègue beaucoup.

Contrairement aux modes Sequential/Hierarchical où l'orchestrateur contrôle le flux, et au mode Graph où le graphe d'état définit la topologie, le mode Autonomous laisse les agents prendre les décisions de délégation et de spawn. L'orchestrateur n'intervient que pour enforcer le budget et collecter les résultats.

### Positionnement par rapport aux autres stratégies

| Stratégie | Décision de routing | Délégation récursive | Spawn dynamique | Budget multi-dimensions | Communication A2A |
|-----------|--------------------|-----------------------|-----------------|------------------------|--------------------|
| Sequential | Fixe (ordre liste) | Non | Non | Non | Non |
| Hierarchical | Manager LLM | 1 niveau | Non | Non | Unidirectionnel |
| Graph | Edges conditionnels | Non | Non | Non (circuit breaker) | Non |
| **Autonomous** | **Agent auto-selection** | **Oui (profondeur contrôlée)** | **Oui (quota)** | **Oui (5 dimensions)** | **Request/Response** |

## Architecture

### Couche Domain — Budget d'execution

| Classe | Fichier | Role |
|--------|---------|------|
| `AgentExecutionBudget` | `Autonomous/AgentExecutionBudget.cs` | Budget multi-dimensions : tool calls, delegation depth, wall time, tokens, spawns |
| `BudgetSnapshot` | `Autonomous/AgentExecutionBudget.cs` | Snapshot immutable pour logging et telemetrie |
| `BudgetExhaustedException` | `Autonomous/AgentExecutionBudget.cs` | Exception typee avec `BudgetDimension` (ToolCalls, DelegationDepth, WallTime, Tokens, SpawnedAgents) |
| `BudgetDimension` | `Autonomous/AgentExecutionBudget.cs` | Enum des 5 dimensions du budget |

Le budget est thread-safe (compteurs `Interlocked`) et immutable après construction (limites en `init`). Chaque action (`RecordToolCall`, `RecordDelegation`, `RecordSpawn`, `RecordTokens`) décrémente le budget et lève `BudgetExhaustedException` si la limite est atteinte.

### Couche Domain — ProcessType

`ProcessType.Autonomous` est ajouté au value object existant. `IProcessStrategy` expose une nouvelle méthode :

```csharp
Task<CrewOutput> ExecuteAutonomousAsync(
    Crew crew,
    AgentExecutionBudget budget,
    IReadOnlyDictionary<string, string>? inputVariables = null);
```

### Couche Application — Communication A2A

| Classe | Fichier | Rôle |
|--------|---------|------|
| `IAgentChannel` | `Interfaces/Services/IAgentChannel.cs` | Canal bidirectionnel request/response entre agents |
| `AgentChannelRequest` | `Interfaces/Services/IAgentChannel.cs` | Request avec correlation ID, intent, payload |
| `AgentChannelResponse` | `Interfaces/Services/IAgentChannel.cs` | Response corrélée avec succès/erreur |
| `NullMemoryScope` | `Context/NullMemoryScope.cs` | Singleton no-op pour les contextes sans mémoire |

`IAgentChannel` supporte trois modes :

- **RequestAsync** : request/response synchrone avec timeout configurable
- **RegisterHandler** : enregistrement d'un handler par agent (retourne `IDisposable`)
- **BroadcastAsync** : notification à tous les agents d'un crew (fire-and-forget)

### Couche Infrastructure — Stratégie autonome

| Classe | Fichier | Rôle |
|--------|---------|------|
| `AutonomousProcessStrategy` | `Crew/Strategies/AutonomousProcessStrategy.cs` | Implémente `IProcessStrategy.ExecuteAutonomousAsync` |
| `InMemoryAgentChannel` | `Communication/InMemoryAgentChannel.cs` | Implémentation in-process du canal A2A (lock-free, `ConcurrentDictionary`) |
| `SpawnAgentTool` | `Tools/SpawnAgentTool.cs` | Outil permettant aux agents de spawner des sous-agents |

### Couche Infrastructure — Modifications existantes

| Classe | Modification |
|--------|-------------|
| `ProcessStrategyFactory` | Ajout du case `"Autonomous"` → `AutonomousProcessStrategy` |
| `SequentialCrewOrchestrator` | Ajout du dispatch `"Autonomous"` avec `AgentExecutionBudget.Default` |
| `DelegateWorkTool` | Ajout du paramètre `AgentExecutionBudget?` optionnel, appel `RecordDelegation()` avant chaque délégation |

## Flux d'execution

```
                    ┌────────────────────────────────────────────┐
                    │         AutonomousProcessStrategy          │
                    │                                            │
  Crew.Tasks ──►    │  pour chaque task :                        │
                    │    1. AssignTaskAsync (LLM-based)           │
                    │    2. budget.RecordToolCall()               │
                    │    3. ExecuteTaskAsync(agent, task)         │
                    │    4. Si échec + AllowDelegation :          │
                    │       ├─ budget.RecordDelegation()          │
                    │       ├─ channel.RequestAsync(peer, task)   │
                    │       └─ peer exécute avec childBudget      │
                    │    5. BudgetExhausted? → partial output     │
                    │                                            │
                    └────────────────────────────────────────────┘

  SpawnAgentTool (optionnel, injecté dans l'agent) :
    1. budget.RecordSpawn()
    2. IAgentFactory.CreateAgentAsync(spawnRequest)
    3. ExecuteTaskAsync(spawnedAgent, task) avec childBudget
```

## Budget multi-dimensions

Le budget contrôle 5 dimensions indépendantes. Chaque dimension a un compteur thread-safe et une limite. L'épuisement de n'importe quelle dimension lève `BudgetExhaustedException`.

| Dimension | Défaut | Strict | Permissive | Description |
|-----------|--------|--------|------------|-------------|
| MaxToolCalls | 15 | 8 | 50 | Nombre max d'appels outils |
| MaxDelegationDepth | 2 | 1 | 4 | Profondeur max de délégation récursive (A→B→C = 2) |
| MaxWallTime | 5 min | 2 min | 15 min | Temps réel maximum |
| MaxTokensConsumed | 16 000 | 8 000 | 64 000 | Tokens totaux (prompt + completion) |
| MaxSpawnedAgents | 3 | 1 | 10 | Nombre max de sous-agents créés |

### Presets

```csharp
// Production : limites conservatrices
var budget = AgentExecutionBudget.Strict;

// Développement : limites larges
var budget = AgentExecutionBudget.Permissive;

// Custom
var budget = new AgentExecutionBudget
{
    MaxToolCalls = 20,
    MaxDelegationDepth = 3,
    MaxWallTime = TimeSpan.FromMinutes(10),
    MaxTokensConsumed = 32_000,
    MaxSpawnedAgents = 5
};
```

### Child budgets

Quand un agent delegue ou spawne, le sous-agent recoit un child budget derive avec les quotas restants :

```csharp
var childBudget = parentBudget.CreateChildBudget();
// MaxToolCalls = parent.Max - parent.Current
// MaxDelegationDepth = parent.Max - parent.Current - 1
// MaxWallTime = parent.Max - parent.Elapsed
// etc.
```

Cela garantit que la somme des consommations enfants ne dépasse jamais le budget parent.

## Communication A2A (IAgentChannel)

Le canal bidirectionnel permet aux agents de communiquer en mode request/response :

```csharp
// Agent A demande a Agent B de clarifier
var request = AgentChannelRequest.Create(
    from: agentA.Id,
    to: agentB.Id,
    intent: "clarify",
    payload: "Quel format de données pour le rapport ?");

var response = await channel.RequestAsync(request, timeout: TimeSpan.FromSeconds(30));

if (response.Success)
    Console.WriteLine($"Réponse: {response.Payload}");
```

### Intents standards

| Intent | Description |
|--------|-------------|
| `delegate` | Délégation de travail (traitement par le handler du target) |
| `clarify` | Demande d'information ou de précision |
| `broadcast` | Notification à tous les agents du crew |

L'implémentation `InMemoryAgentChannel` est in-process et lock-free. Pour un déploiement multi-host, implémenter `IAgentChannel` avec Redis Streams ou un message broker.

## SpawnAgentTool — Self-spawn d'agents

Outil injecté dans les agents autonomes pour créer des sous-agents spécialisés à la volée :

```csharp
// Le LLM de l'agent génère cet appel d'outil :
{
    "tool": "spawn_agent",
    "parameters": {
        "role": "data_analyst",
        "goal": "Analyser les tendances de ventes Q4",
        "task": "Produire un rapport CSV des ventes par region",
        "wait_for_result": true,
        "allow_delegation": false
    }
}
```

Chaque spawn est contrôlé par le budget (`RecordSpawn`). L'agent spawné reçoit un child budget avec des limites réduites (5 itérations max, quotas restants).

## Observabilite

### Metadata de sortie

Le `CrewOutput` en mode Autonomous inclut des metadata de budget :

```json
{
    "process_type": "autonomous",
    "agent_count": 3,
    "budget_tool_calls": "12/15",
    "budget_delegation_depth": "1/2",
    "budget_tokens": "9200/16000",
    "budget_spawned": "1/3",
    "budget_exhausted": false
}
```

### BudgetSnapshot

`budget.ToSnapshot()` retourne un `BudgetSnapshot` immutable à tout moment, loggable et sérialisable.

### Logging structuré

Tous les événements clés sont loggés via `LoggerMessage` :

- `Starting autonomous execution for crew {CrewId} (budget: {MaxToolCalls} tool calls, depth {MaxDepth})`
- `Agent {AgentId} claimed task {TaskId}: {Reason}`
- `Delegation: {From} -> {To} for task {TaskId} (depth: {Depth})`
- `Budget exhausted for crew {CrewId}: dimension={Dimension}, {Message}`
- `Agent {ParentId} spawned sub-agent {ChildId} (role: {Role})`

## Configuration YAML

```yaml
crew:
  name: research-team
  process: autonomous      # ← active le mode autonome
  goal: "Produire un rapport de recherche complet"
  
  autonomousBudget:        # ← optionnel, défauts si absent
    maxToolCalls: 20
    maxDelegationDepth: 2
    maxWallTime: "00:10:00"
    maxTokensConsumed: 32000
    maxSpawnedAgents: 3
    preset: default        # ou "strict" / "permissive"

  agents:
    - role: researcher
      goal: "Trouver des sources fiables"
      allowDelegation: true
      tools: [web_search, spawn_agent]  # ← spawn_agent pour self-spawn

    - role: analyst
      goal: "Analyser et synthétiser les données"
      allowDelegation: true
      tools: [json_search, csv_search]

    - role: writer
      goal: "Rédiger le rapport final"
      allowDelegation: false
```

> **Note** : Le parsing YAML de `autonomousBudget` n'est pas encore implémenté. Le mode Autonomous utilise `AgentExecutionBudget.Default` pour l'instant. L'injection du budget custom depuis YAML est prévue en v1.1.

## Complémentarité avec les autres modes

| Besoin | Mode recommandé |
|--------|----------------|
| Pipeline linéaire, déterminisme maximal | Sequential |
| Manager centralisé, review de qualité | Hierarchical |
| Tâches indépendantes, parallélisme | Parallel |
| Boucles de raffinement, retry conditionnel | Graph |
| **Agents auto-organisés, délégation récursive, spawn dynamique** | **Autonomous** |

Le mode Autonomous est le plus expressif mais aussi le moins déterministe. Pour les workloads de production sensibles, préférer Sequential ou Hierarchical et réserver Autonomous aux cas où l'autonomie des agents apporte une valeur supérieure au coût de non-déterminisme (recherche exploratoire, creative writing, résolution de problèmes complexes multi-domaines).

---

> **Voir aussi** : [Guide comparatif ProcessTypes](./process-types.md) · [Orchestration Graph](./graph.md) · [Schéma YAML](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)
