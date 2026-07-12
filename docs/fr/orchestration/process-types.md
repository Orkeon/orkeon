> 🇬🇧 [English version](../../orchestration/process-types.md)

# Guide comparatif des ProcessTypes

> **Prérequis** : [Vue d'ensemble](../getting-started/overview.md) et [YAML et Builders](../getting-started/yaml-and-builders.md)

---

## Vue d'ensemble

Orkeon propose **6 stratégies d'orchestration** via le value object `ProcessType` (Domain layer). Chaque stratégie définit comment les agents se coordonnent pour exécuter les tâches d'une Crew. Le choix du ProcessType est le levier architectural le plus impactant sur le comportement d'un système multi-agents.

```csharp
// Orkeon.Domain.SharedKernel.ValueObjects.ProcessType (sealed record)
ProcessType.Sequential    // Pipeline linéaire
ProcessType.Hierarchical  // Manager + workers
ProcessType.Parallel      // Exécution concurrente
ProcessType.Consensual    // Vote et consensus
ProcessType.Graph         // Graphe d'états avec cycles contrôlés
ProcessType.Autonomous    // Auto-organisation avec budget
```

### Architecture d'implémentation

```
ProcessType (Domain — Value Object)
     │
     ▼
IProcessStrategy (Domain — Interface)
     │
     ▼
ProcessStrategyFactory (Infrastructure)
     │
     ├── SequentialProcessStrategy
     ├── HierarchicalProcessStrategy
     ├── ParallelProcessStrategy
     ├── ConsensualProcessStrategy      ← IConsensualProcessStrategy
     ├── GraphProcessStrategy
     └── AutonomousProcessStrategy
```

Le `ProcessStrategyFactory` résout la stratégie appropriée via un switch sur `ProcessType.Value`, injecté par DI.

---

## Matrice de comparaison rapide

| Critère | Sequential | Hierarchical | Parallel | Consensual | Graph | Autonomous |
|---------|-----------|-------------|---------|-----------|-------|-----------|
| **Modèle d'exécution** | Linéaire | Linéaire + revue | Concurrent | Parallèle + vote | Machine à états | Auto-organisé |
| **Coordination** | Round-robin | Manager LLM | Round-robin | Consensus LLM | Round-robin + routing | LLM + canal A2A |
| **Dépendances entre tâches** | Oui (chaînées) | Oui (via manager) | Non | Non | Oui (edges) | Oui (délégation) |
| **Circuit breaker** | — | — | — | — | ✅ 4 mécanismes | — |
| **Budget d'exécution** | — | — | — | — | — | ✅ 5 dimensions |
| **Retry automatique** | — | Révisions (max 3) | — | Rounds de vote | ✅ configurable | Via délégation |
| **Spawn dynamique** | — | — | — | — | — | ✅ SpawnAgentTool |
| **Complexité** | ⭐ | ⭐⭐ | ⭐ | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Coût LLM relatif** | Bas | Moyen | Bas | Élevé | Moyen | Variable |
| **Cas d'usage principal** | Pipelines ETL | QA, revue de code | Tâches indépendantes | Décisions critiques | Workflows complexes | Exploration, R&D |

---

## 1. Sequential — Pipeline linéaire

### Principe

Les tâches s'exécutent **une par une, dans l'ordre** défini par l'`ExecutionPlan`. Chaque tâche reçoit en contexte les résultats des tâches précédentes. L'assignation agent se fait en round-robin (sauf assignation explicite via `task.AssignedAgent`).

### Mécanisme interne

```
Task 1 → Agent A → output₁
                      ↓ (contexte enrichi)
Task 2 → Agent B → output₂
                      ↓
Task 3 → Agent C → output₃ → résultat final
```

**Classes clés** : `SequentialProcessStrategy`, `ExecutionPlan`, `AgentDelegationToolsProvider`

### Configuration YAML

```yaml
crew:
  process: sequential
  tasks:
    - description: "Collecter les données"
      expected_output: "Données brutes"
    - description: "Analyser les données"
      expected_output: "Rapport d'analyse"
    - description: "Générer les recommandations"
      expected_output: "Plan d'action"
```

### Configuration Fluent Builder

```csharp
var crew = new CrewBuilder()
    .Sequential()
    .WithAgent(a => a.Role("Collector").Goal("Gather data"))
    .WithAgent(a => a.Role("Analyst").Goal("Analyze data"))
    .WithTask(t => t.Description("Collect").ExpectedOutput("Raw data"))
    .WithTask(t => t.Description("Analyze").ExpectedOutput("Report"))
    .Build();
```

### Avantages

- **Simplicité maximale** : aucune configuration complexe, comportement prévisible
- **Traçabilité** : chaque étape est clairement identifiable dans les logs
- **Contexte cumulatif** : chaque tâche bénéficie des résultats précédents
- **Déterminisme** : même entrée → même parcours d'exécution

### Inconvénients

- **Pas de parallélisme** : le temps total est la somme de toutes les tâches
- **Point de défaillance unique** : un échec bloque toute la chaîne
- **Pas de retry** : aucune reprise automatique en cas d'erreur
- **Rigide** : l'ordre est fixe, pas de branchement conditionnel

### Quand l'utiliser

- Pipelines ETL (extraction → transformation → chargement)
- Rédaction séquentielle (recherche → rédaction → relecture)
- Workflows où chaque étape dépend strictement de la précédente
- Prototypage rapide et POC

### Quand ne pas l'utiliser

- Tâches indépendantes pouvant s'exécuter en parallèle
- Workflows nécessitant des branches conditionnelles
- Scénarios exigeant de la résilience (retry, fallback)

---

## 2. Hierarchical — Manager + Workers

### Principe

Un **agent manager** (piloté par LLM) coordonne une équipe de workers. Pour chaque tâche, le manager sélectionne l'agent le plus approprié via `AssignTaskAsync()`, puis **revoit la sortie** et peut demander jusqu'à 3 révisions.

### Mécanisme interne

```
                  ┌─── Manager Agent ───┐
                  │   AssignTask()      │
                  │   ReviewOutput()    │
                  └────────┬────────────┘
                           │
            ┌──────────────┼──────────────┐
            ▼              ▼              ▼
        Agent A        Agent B        Agent C
        (choisi)       (en attente)   (en attente)
            │
            ▼
        Output → Review → OK? → oui → tâche suivante
                           → non → révision (max 3)
```

**Classes clés** : `HierarchicalProcessStrategy`, `IManagerAgent`, `LlmBasedManager`, `TaskAssignment`

**Interface `IManagerAgent`** :
- `AssignTaskAsync(task, agents, context)` → `TaskAssignment` (agent sélectionné + justification)
- `ReviewOutputAsync(output, task)` → `bool` (approuvé ou non)

### Configuration YAML

```yaml
crew:
  process: hierarchical
  manager_llm:
    provider: openai
    model: gpt-4o
  agents:
    - role: "Senior Developer"
      goal: "Write production code"
    - role: "QA Engineer"
      goal: "Test and validate"
    - role: "Tech Writer"
      goal: "Document the code"
```

### Avantages

- **Allocation intelligente** : le manager choisit l'agent le plus adapté à chaque tâche
- **Contrôle qualité intégré** : boucle de révision automatique
- **Flexibilité** : le manager peut adapter la stratégie en cours d'exécution
- **Traçabilité** : chaque assignation est justifiée

### Inconvénients

- **Coût LLM supplémentaire** : le manager consomme des tokens pour chaque décision + revue
- **Goulot d'étranglement** : tout passe par le manager (pas de parallélisme)
- **Révisions limitées** : max 3 révisions (hardcodé), pas de retry structurel
- **Dépendance à la qualité du manager** : un mauvais prompt manager dégrade tout le workflow

### Quand l'utiliser

- Revue de code (le manager assigne le reviewer le plus compétent)
- Projets avec des spécialistes hétérogènes (dev, QA, design, rédaction)
- Workflows nécessitant une validation humaine simulée
- Situations où la qualité prime sur la vitesse

### Quand ne pas l'utiliser

- Tâches homogènes (round-robin suffit)
- Contraintes de coût LLM strictes
- Workflows à haute fréquence (le manager est un goulot)

---

## 3. Parallel — Exécution concurrente

### Principe

Toutes les tâches s'exécutent **simultanément** via `Task.WhenAll()`. Chaque tâche dispose d'un contexte d'exécution indépendant (pas de résultats partagés entre tâches). Les résultats sont agrégés à la fin.

### Mécanisme interne

```
        ┌── Task 1 → Agent A → output₁ ──┐
        │                                  │
Start ──┼── Task 2 → Agent B → output₂ ──┼── Agrégation → Résultat
        │                                  │
        └── Task 3 → Agent C → output₃ ──┘
```

**Classes clés** : `ParallelProcessStrategy`

### Configuration YAML

```yaml
crew:
  process: parallel
  tasks:
    - description: "Analyser le marché français"
      expected_output: "Rapport France"
    - description: "Analyser le marché allemand"
      expected_output: "Rapport Allemagne"
    - description: "Analyser le marché espagnol"
      expected_output: "Rapport Espagne"
```

### Avantages

- **Vitesse maximale** : temps total = durée de la tâche la plus longue
- **Simplicité** : pas de coordination complexe
- **Scalabilité** : ajout de tâches sans impact sur le temps total
- **Isolation** : un échec d'une tâche n'impacte pas les autres

### Inconvénients

- **Pas de dépendances** : impossible de chaîner les résultats entre tâches
- **Consommation API en pic** : toutes les requêtes LLM partent en même temps (rate limiting)
- **Agrégation basique** : les résultats sont simplement concaténés
- **Pas de retry** : aucune reprise automatique

### Quand l'utiliser

- Analyses multi-marchés ou multi-sources indépendantes
- Génération de contenu en batch (un article par marché, par langue)
- Tâches de classification parallèles
- Tout scénario où les tâches n'ont aucune dépendance mutuelle

### Quand ne pas l'utiliser

- Tâches avec dépendances (utiliser Sequential ou Graph)
- APIs avec rate limiting strict (les appels simultanés peuvent être throttled)
- Scénarios nécessitant une synthèse progressive

---

## 4. Consensual — Vote et consensus

### Principe

Pour chaque tâche, **tous les agents l'exécutent indépendamment**, puis un vote détermine le meilleur résultat. Si aucun consensus n'est atteint, des **rounds de discussion** permettent aux agents de reconsidérer leur position en voyant les résultats des autres. Un mécanisme de fallback tranche en dernier recours.

### Mécanisme interne

```
Task N ──┬── Agent A → résultat A ──┐
         ├── Agent B → résultat B ──┼── Vote ── Consensus? ── oui → Accepté
         └── Agent C → résultat C ──┘              │
                                                   non
                                                    ↓
                                          Discussion (round 2)
                                          Agents voient les autres résultats
                                                    ↓
                                                  Re-vote
                                                    ↓
                                          Échec → Fallback
```

**Classes clés** : `ConsensualProcessStrategy`, `IVotingStrategy`, `IVotingStrategyFactory`, `Vote`, `VoteResult`, `VotingOptions`, `ConsensualProcessOptions`

### Types de consensus disponibles

Le mécanisme de vote est **sélectionnable par configuration** : la valeur de
`Orkeon:Consensus:VotingOptions:ConsensusType` (section .NET `appsettings.json`)
choisit la stratégie dédiée via `IVotingStrategyFactory`. Sans configuration,
le défaut reste `Majority` (rétro-compatible).

| Type | Stratégie résolue | Description | Seuil par défaut |
|------|-------------------|-------------|------------------|
| `Majority` | `MajorityVotingStrategy` | Plus de 50% des votes | 50% |
| `SuperMajority` | `SuperMajorityVotingStrategy` | Seuil configurable (défaut 2/3) | 66.7% |
| `Unanimity` | `UnanimityVotingStrategy` | Tous les agents doivent s'accorder | 100% |
| `WeightedConsensus` | `WeightedConsensusStrategy` | Votes pondérés par rôle (`RoleWeights`) | Configurable |
| `BordaCount` | `BordaCountStrategy` | Classement par score Borda | N/A |

### Configuration (.NET, section `Orkeon:Consensus`)

```json
{
  "Orkeon": {
    "Consensus": {
      "VotingOptions": {
        "ConsensusType": "SuperMajority",
        "ConsensusThreshold": 75,
        "QuorumPercent": 60,
        "MaxVotingRounds": 3,
        "UseWeightedVotes": true,
        "AllowAbstention": false
      },
      "EnableDiscussion": true,
      "FallbackStrategy": "AcceptBestScore",
      "RoleWeights": {
        "Senior Analyst": 2.0,
        "Junior Analyst": 1.0
      }
    }
  }
}
```

> **Note** : `ConsensusThreshold` et `QuorumPercent` s'expriment en **pourcentage
> (0–100)**, pas en fraction. Un seuil de super-majorité de 75 % s'écrit `75`.

### Avantages

- **Robustesse** : réduit les hallucinations et biais individuels
- **Qualité** : la "sagesse collective" produit souvent de meilleurs résultats
- **Flexibilité du vote** : 5 stratégies de consensus, pondération par rôle
- **Discussion** : les agents peuvent s'améliorer mutuellement entre les rounds
- **Fallback** : 3 stratégies de repli (meilleur score, échec, décision manager)

### Inconvénients

- **Coût LLM élevé** : chaque tâche est exécutée N fois (N = nombre d'agents) × rounds
- **Lenteur** : multiplication des appels LLM, surtout avec discussion activée
- **Complexité de configuration** : nombreux paramètres (seuil, quorum, pondération, rounds)
- **Résultat incertain** : le fallback peut produire un résultat insatisfaisant

### Quand l'utiliser

- Décisions critiques (diagnostic médical, évaluation de risque, audit)
- Situations où la fiabilité prime sur le coût
- Évaluations subjectives nécessitant plusieurs perspectives
- Scénarios de "red team" (plusieurs agents tentent de trouver des failles)

### Quand ne pas l'utiliser

- Tâches factuelles avec une seule réponse correcte
- Contraintes de budget LLM (multiplication par N agents × R rounds)
- Workflows à haute fréquence (trop lent)

---

## 5. Graph — Graphe d'états avec cycles contrôlés

### Principe

Les tâches sont organisées dans un **graphe d'états typé** inspiré de LangGraph. Le graphe supporte des **edges conditionnels** (routing dynamique) et des **cycles contrôlés** (retry automatique). Un **circuit breaker** à 4 mécanismes empêche les boucles infinies.

### Mécanisme interne

```
START ──→ execute_task ──→ route ──┬── succès ──→ execute_task (suivante)
                                   │                      ↓
                                   │               ... (loop) ...
                                   │                      ↓
                                   ├── échec ──→ execute_task (retry)
                                   │
                                   └── terminé ──→ END
```

**Classes clés** :
- `StateGraph<TState>` — Définition du graphe (nodes + edges)
- `GraphRunner<TState>` — Moteur d'exécution
- `GraphProcessStrategy` — Implémentation `IProcessStrategy`
- `CircuitBreakerPolicy` — Protection anti-boucle
- `CrewGraphState` — État typé circulant dans le graphe

### `CrewGraphState` — Propriétés de l'état

| Propriété | Type | Description |
|-----------|------|-------------|
| `PendingTaskIds` | `Queue<string>` | Tâches restantes à exécuter |
| `FailedTaskIds` | `Queue<string>` | Tâches éligibles au retry |
| `RetryCounts` | `Dict<string, int>` | Compteur de retries par tâche |
| `MaxRetryCycles` | `int` | Nombre max de retries (défaut: 2) |
| `ApplicationOutputs` | `Dict` | Sorties accumulées |
| `TotalTokensUsed` | `int` | Compteur de tokens consommés (propagé aux métadonnées du `CrewOutput` sous `totalTokens`) |
| `PromptTokensUsed` | `int` | Compteur de tokens côté prompt (0 si le provider ne fournit pas le split) |
| `CompletionTokensUsed` | `int` | Compteur de tokens côté completion (0 si le provider ne fournit pas le split) |

### Circuit Breaker — 4 mécanismes de protection

| Mécanisme | Strict | Default | Permissive |
|-----------|--------|---------|------------|
| `MaxTransitions` | 50 | 100 | 1000 |
| `StateTimeout` | 2 min | 5 min | 30 min |
| `MaxStateVisits` | 5 | 10 | 50 |
| `MaxTotalDuration` | 10 min | 30 min | 2 h |

### Configuration YAML

```yaml
crew:
  process: graph
  graph_config:
    circuit_breaker: Strict    # ou Default, Permissive
    max_retry_cycles: 3
    # Surcharges individuelles possibles :
    max_transitions: 75
    state_timeout: "00:03:00"
```

### Avantages

- **Branchement conditionnel** : routing dynamique selon le résultat de chaque nœud
- **Retry intégré** : les tâches échouées sont automatiquement re-tentées
- **Sécurité** : circuit breaker à 4 niveaux empêche les exécutions infinies
- **Observabilité** : événements `OnNodeCompleted`, `OnCircuitBroken`
- **Cycles utiles** : boucle feedback → correction → validation
- **Presets** : Strict (production) vs Permissive (développement)

### Inconvénients

- **Complexité de conception** : définir un graphe correct demande de la réflexion
- **Debugging** : tracer un parcours dans un graphe cyclique est plus difficile
- **Overhead** : le moteur de graphe ajoute une couche de complexité
- **Circuit breaker** : peut couper l'exécution prématurément si mal configuré

### Quand l'utiliser

- Workflows avec branchements conditionnels (validation → OK/KO → chemins différents)
- Pipelines nécessitant du retry avec backoff
- Scénarios de correction itérative (rédaction → revue → correction → re-revue)
- Workflows réglementaires avec des chemins d'exception

### Quand ne pas l'utiliser

- Pipelines linéaires simples (Sequential suffit)
- Tâches indépendantes (Parallel suffit)
- Équipes qui ne sont pas à l'aise avec la modélisation par graphe

> **Voir aussi** : [Orchestration FSM](./fsm.md) pour le détail complet.

---

## 6. Autonomous — Auto-organisation avec budget

### Principe

Les agents **s'auto-organisent** : ils revendiquent des tâches, délèguent récursivement à leurs pairs, et peuvent **spawner dynamiquement** de nouveaux agents. Un **budget multi-dimensionnel** (5 axes) garantit la terminaison. La communication inter-agents se fait via un canal bidirectionnel (`IAgentChannel`).

### Mécanisme interne

```
                    ┌─── Budget (5 dimensions) ───┐
                    │  tool calls · depth · time   │
                    │  tokens · spawned agents      │
                    └──────────────┬────────────────┘
                                   │
Manager (LLM) ── assigne ──→ Agent A
                                   │
                    ┌──────────────┼──────────────┐
                    ▼              ▼              ▼
              DelegateWork   SpawnAgent      Exécution
              (child budget)  (via factory)   directe
                    │              │
                    ▼              ▼
                Agent B       Agent D (nouveau)
                    │              │
                    ▼              ▼
              Résultat ←── canal A2A ──→ Résultat
```

**Classes clés** :
- `AutonomousProcessStrategy` — Stratégie d'orchestration
- `AgentExecutionBudget` — Budget multi-dimensionnel (Domain)
- `IAgentChannel` / `InMemoryAgentChannel` — Communication A2A (lock-free)
- `SpawnAgentTool` — Création dynamique d'agents
- `DelegateWorkTool` — Délégation avec budget hérité
- `BudgetExhaustedException` — Exception levée quand un axe est épuisé

### Budget multi-dimensionnel — 5 axes

| Dimension | Strict | Default | Permissive | Description |
|-----------|--------|---------|------------|-------------|
| `MaxToolCalls` | 8 | 15 | 50 | Nombre max d'appels d'outils |
| `MaxDelegationDepth` | 1 | 2 | 4 | Profondeur de délégation (A→B→C = 2) |
| `MaxTokensConsumed` | 8 000 | 16 000 | 64 000 | Tokens LLM consommés |
| `MaxSpawnedAgents` | 1 | 3 | 10 | Agents créés dynamiquement |
| `MaxWallTime` | 2 min | 5 min | 15 min | Durée maximale d'exécution |

**Child budgets** : quand un agent délègue, il crée un budget enfant dérivé de ses propres ressources restantes (via `CreateChildBudget()`). Le budget enfant est toujours inférieur ou égal au budget parent restant.

### Communication A2A

```csharp
// Request/Response
var request = AgentChannelRequest.Create(
    from: analyst.Id,
    to: researcher.Id,
    intent: "find_data",
    payload: "Statistiques marché 2025");

var response = await channel.RequestAsync(request, timeout);

// Broadcast (fire-and-forget)
await channel.BroadcastAsync(analyst.Id, crew.Id, "Résultats disponibles", ct);
```

### Configuration YAML

```yaml
crew:
  process: autonomous
  autonomous_budget:
    preset: Default           # Strict, Default, ou Permissive
    # Surcharges possibles :
    max_tool_calls: 20
    max_delegation_depth: 3
    max_wall_time: "00:10:00"
    max_tokens_consumed: 32000
    max_spawned_agents: 5
```

### Avantages

- **Adaptabilité maximale** : les agents réagissent au contexte en temps réel
- **Spawn dynamique** : capacité à créer des spécialistes à la demande
- **Terminaison garantie** : budget multi-dimensionnel empêche les exécutions infinies
- **Communication riche** : A2A request/response avec corrélation
- **Presets** : configuration rapide (Strict/Default/Permissive)
- **Scalabilité** : les agents se répartissent le travail organiquement

### Inconvénients

- **Complexité élevée** : mode le plus difficile à configurer et débugger
- **Coût imprévisible** : la consommation de tokens dépend des décisions des agents
- **Non-déterministe** : deux exécutions identiques peuvent suivre des chemins différents
- **Budget trop strict** : peut couper l'exécution avant d'obtenir un résultat complet
- **Observabilité** : nécessite un bon logging (BudgetSnapshot) pour comprendre le comportement

### Quand l'utiliser

- Exploration (recherche, R&D, analyse exploratoire)
- Problèmes mal définis où la stratégie optimale n'est pas connue à l'avance
- Systèmes nécessitant de l'auto-réparation (un agent détecte un problème → délègue la correction)
- Scénarios multi-étapes où chaque étape peut révéler de nouvelles sous-tâches

### Quand ne pas l'utiliser

- Workflows déterministes et bien définis (Sequential ou Graph)
- Environnements à budget LLM contraint sans marge
- Scénarios réglementaires nécessitant une traçabilité complète du parcours

> **Voir aussi** : [Orchestration Autonome](./autonomous.md) pour le détail complet.

---

## Arbre de décision

```
Tes tâches ont-elles des dépendances entre elles ?
│
├── NON
│   └── As-tu besoin de fiabilité maximale (multi-perspectives) ?
│       ├── OUI → Consensual
│       └── NON → Parallel
│
└── OUI
    └── Le workflow a-t-il des branches conditionnelles ou des boucles ?
        │
        ├── NON
        │   └── As-tu besoin de contrôle qualité (revue manager) ?
        │       ├── OUI → Hierarchical
        │       └── NON → Sequential
        │
        └── OUI
            └── La stratégie optimale est-elle connue à l'avance ?
                ├── OUI → Graph (workflow modélisable)
                └── NON → Autonomous (exploration)
```

---

## Combinaisons et complémentarité

Les ProcessTypes ne sont pas mutuellement exclusifs à l'échelle d'un système. Il est courant de combiner plusieurs stratégies :

**Graph + FSM** : Le Graph orchestre la Crew (niveau inter-tâches), tandis que la FSM gère l'exécution interne de chaque tâche (niveau intra-tâche). Les deux utilisent `CircuitBreakerPolicy` avec les mêmes presets.

**Sequential + Hierarchical** : Une Crew séquentielle peut contenir des tâches dont les agents utilisent `DelegateWorkTool` pour simuler un comportement hiérarchique local.

**Autonomous + Graph** : Un agent autonome peut décider de créer un sous-workflow Graph pour structurer une sous-tâche complexe qu'il a découverte dynamiquement.

---

## Résumé des coûts et performances

| ProcessType | Appels LLM (N tâches, M agents) | Latence | Prévisibilité |
|-------------|----------------------------------|---------|---------------|
| Sequential | N | Σ(durées) | ⭐⭐⭐⭐⭐ |
| Hierarchical | N × (1 assign + 1-3 reviews) | Σ(durées) × 1.5-3 | ⭐⭐⭐⭐ |
| Parallel | N | max(durées) | ⭐⭐⭐⭐ |
| Consensual | N × M × rounds | Σ(max(durées) × rounds) | ⭐⭐⭐ |
| Graph | N × (1 + retries) | Variable | ⭐⭐⭐ |
| Autonomous | Imprévisible (budget-bounded) | Variable (wall-time bounded) | ⭐⭐ |

---

## Références croisées

| Sujet | Document |
|-------|----------|
| Architecture et concepts de base | [Vue d'ensemble](../getting-started/overview.md) |
| Fonctionnalités détaillées | [YAML et Builders](../getting-started/yaml-and-builders.md) |
| Orchestration FSM (intra-tâche) | [./fsm.md](./fsm.md) |
| Orchestration Graph (inter-tâches) | [./graph.md](./graph.md) |
| Orchestration Autonomous | [./autonomous.md](./autonomous.md) |
| Blueprint pour nouveau ProcessType | [../../guides/blueprint.md](../guides/blueprint.md) |
