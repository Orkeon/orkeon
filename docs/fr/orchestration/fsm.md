> 🇬🇧 [English version](../../orchestration/fsm.md)

> **Voir aussi** : [Guide comparatif ProcessTypes](./process-types.md) · [Schéma YAML](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)

# Orchestration par machine à états finis (FSM)

## Vue d'ensemble

Orkeon fournit un framework de machine à états finis générique (`StateMachine<TState, TEvent>`) dans la couche Domain, avec un circuit breaker intégré pour prévenir les boucles récursives incontrôlées lors de l'orchestration multi-agents.

La FSM est configurable via YAML (champ `circuitBreaker`) et spécialisée en premier lieu pour le cycle de vie d'exécution des tâches (`TaskExecutionStateMachine`). Le framework est générique et extensible à d'autres domaines (Crew, Agent, Flow).

## Architecture

### Couche Domain — Framework générique

Les composants du framework se trouvent dans `Orkeon.Domain.Common.StateMachine` :

| Classe | Role |
|--------|------|
| `StateMachine<TState, TEvent>` | Moteur FSM thread-safe avec circuit breaker intégré |
| `StateMachineBuilder<TState, TEvent>` | API fluent pour déclarer le graphe d'états |
| `CircuitBreakerPolicy` | Configuration des seuils de protection |
| `TransitionResult<TState, TEvent>` | Résultat d'une transition (états, ordinal, timestamp) |
| `IStateMachine<TState, TEvent>` | Interface lecture seule pour observation |
| `IMutableStateMachine<TState, TEvent>` | Interface avec `Fire()`, `TryFire()`, événements |

### Couche Domain — Spécialisation Task

Les composants spécifiques au cycle de vie des tâches se trouvent dans `Orkeon.Domain.Task` :

| Classe | Rôle |
|--------|------|
| `TaskExecutionState` | Enum des états d'exécution (Assigned, Planning, Executing, etc.) |
| `TaskExecutionEvent` | Enum des événements (StartPlanning, RequestToolCall, etc.) |
| `TaskExecutionStateMachine` | Factory pré-configurée avec guards et graphe complet |
| `TaskExecutionGuardContext` | Contexte typé pour les guards (retries, tool calls, validation) |

### Couche Infrastructure — Integration YAML

| Classe | Role |
|--------|------|
| `CircuitBreakerYamlConfig` | Modele YAML pour la section `circuitBreaker` |
| `CircuitBreakerPolicyFactory` | Convertit la config YAML en `CircuitBreakerPolicy` + FSM |

### Couche Domain — Configuration

| Classe | Rôle |
|--------|------|
| `CircuitBreakerConfig` | DTO immutable pour la configuration circuit breaker |

## Graphe d'états de la TaskExecutionStateMachine

```
                     StartPlanning         BeginExecution
   [Assigned] ──────────────────► [Planning] ─────────────► [Executing]
        │                                                      │    ▲
        │ BeginExecution                       ToolCallCompleted│    │ RequestToolCall
        └──────────────────────────────► [Executing] ◄─────────┘    │ (guard: budget)
                                            │    │                  ▼
                              SubmitFor     │    │            [ToolCalling]
                              Validation    │    │ Fail            │
                                            ▼    ▼                 │ ToolCallFailed
                                      [Validating]  [Failed] ◄────┘
                                         │    │       │
                           ValidationPassed│    │       │ Retry (guard: maxRetries)
                                         ▼    │       └──────────► [Executing]
                                   [Completed] │ ValidationFailed
                                               │ (guard: maxValidationRetries)
                                               └──────────► [Executing]

   RequestHumanInput                    HumanInputReceived
   [Executing] ──────────► [WaitingForHumanInput] ──────────► [Executing]

   Cancel (depuis tout état non-terminal) ────────────────► [Cancelled]
   Circuit breaker trip ──────────────────────────────────► [Degraded]
```

États terminaux : `Completed`, `Cancelled`, `Degraded`.

## Circuit breaker

Le circuit breaker est intégré directement dans `StateMachine<TState, TEvent>` et vérifie quatre conditions avant chaque transition :

### Mécanismes de protection

| Mécanisme | Paramètre | Description |
|-----------|-----------|-------------|
| Max transitions | `MaxTransitions` | Nombre total de transitions autorisées |
| Timeout par état | `StateTimeout` | Durée maximale dans un seul état |
| Détection de cycles | `MaxStateVisits` | Nombre max de visites d'un même état |
| Durée totale | `MaxTotalDuration` | Durée de vie maximale de la machine |

Quand une condition est violée, deux comportements sont possibles selon `UseDegradedMode` :

- `false` (défaut) : une `CircuitBrokenException` est levée avec un `CircuitBreakerStatus` détaillé
- `true` : la machine transite automatiquement vers l'état dégradé configuré (ex: `TaskExecutionState.Degraded`)

### Presets

Trois presets sont fournis via `CircuitBreakerPolicy` :

| Preset | MaxTransitions | StateTimeout | MaxStateVisits | MaxTotalDuration | DegradedMode |
|--------|---------------|-------------|----------------|------------------|--------------|
| `Strict` | 50 | 2 min | 5 | 10 min | true |
| `Default` | 100 | 5 min | 10 | 30 min | false |
| `Permissive` | 1000 | 30 min | 50 | 2 h | false |

Le preset `Strict` est le défaut pour les workloads LLM en production.

### Observabilité

La machine expose deux événements :

- `OnTransition` : émis après chaque transition réussie, avec `TransitionResult` (from, to, trigger, ordinal, timestamp)
- `OnCircuitBroken` : émis quand le circuit trip, avec `CircuitBreakerStatus` incluant un histogramme des visites par état

`CircuitBreakerStatus` fournit un snapshot complet : `IsBroken`, `BrokenReason`, `TotalTransitions`, `TimeInCurrentState`, `CurrentStateVisitCount`, `TotalElapsed`, `StateVisitHistogram`.

## Guards typés

La `TaskExecutionStateMachine` utilise trois guards typés via `TaskExecutionGuardContext` pour prévenir les boucles dangereuses :

| Guard | Transition protégée | Condition |
|-------|---------------------|-----------|
| Budget tool calls | `Executing → ToolCalling` | `ToolCallCount < MaxToolCallsPerRound && IsToolRegistered` |
| Limite de retries | `Failed → Executing` | `RetryCount < MaxRetries` |
| Limite de validation | `Validating → Executing` | `ValidationAttempts < MaxValidationRetries` |

Le guard `IsToolRegistered` bloque les appels à des outils non enregistrés dans l'agent, ce qui prévient les hallucinations d'outils par le LLM.

## Configuration YAML

### Schéma `circuitBreaker`

Le bloc `circuitBreaker` est utilisable à deux niveaux dans le `config.yaml` :

```yaml
# Niveau crew — défauts pour toutes les tâches
circuitBreaker:
  preset: string              # "strict" | "permissive" | "default"
  maxTransitions: int         # Surcharge le preset
  stateTimeoutSeconds: int    # Timeout par état en secondes
  maxStateVisits: int         # Détection de cycles
  maxTotalDurationSeconds: int # Durée totale en secondes
  useDegradedMode: bool       # true = Degraded, false = exception
  maxRetries: int             # Retries après échec (guard)
  maxToolCallsPerRound: int   # Tool calls max par round (guard)
  maxValidationRetries: int   # Boucles validation max (guard)

tasks:
  <task_id>:
    description: string
    # Niveau task — override pour cette tâche spécifique
    circuitBreaker:
      maxTransitions: int     # Surcharge le défaut crew
      stateTimeoutSeconds: int
      # ... mêmes champs que ci-dessus
```

### Hiérarchie de résolution

```
1. Task-level circuitBreaker     (priorité haute)
2. Crew-level circuitBreaker     (défaut)
3. Preset nommé                  (si spécifié)
4. CircuitBreakerPolicy.Strict   (fallback si rien n'est configuré)
```

Chaque champ individuel surcharge la valeur du preset. Par exemple, un preset "strict" avec `maxTransitions: 200` garde toutes les valeurs du preset sauf les transitions.

### Modèles YAML

| Modèle C# | Classe YAML | Fichier |
|-----------|-------------|---------|
| `CircuitBreakerConfig` | `CircuitBreakerYamlConfig` | `YamlCrewDefinitionLoader.cs` |

Le mapping est effectué par `YamlCrewDefinitionLoader.MapCircuitBreaker()`. La conversion en `CircuitBreakerPolicy` exécutable est effectuée par `CircuitBreakerPolicyFactory.Resolve()`.

## Utilisation en code C#

### Création manuelle (Fluent Builder)

```csharp
using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Task;

// Créer une FSM avec le preset Strict
var fsm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Strict);

// Observer les transitions
fsm.OnTransition += (_, result) =>
    Console.WriteLine($"{result.FromState} -> {result.ToState} via {result.Trigger}");

fsm.OnCircuitBroken += (_, status) =>
    Console.WriteLine($"CIRCUIT BROKEN: {status.BrokenReason}");

// Contexte de garde
var ctx = new TaskExecutionGuardContext
{
    RetryCount = 0,
    MaxRetries = 3,
    ToolCallCount = 0,
    MaxToolCallsPerRound = 10,
    IsToolRegistered = true,
};

// Exécuter le workflow
fsm.Fire(TaskExecutionEvent.BeginExecution);
fsm.Fire(TaskExecutionEvent.RequestToolCall, ctx);
fsm.Fire(TaskExecutionEvent.ToolCallCompleted);
fsm.Fire(TaskExecutionEvent.SubmitForValidation);
fsm.Fire(TaskExecutionEvent.ValidationPassed);

Console.WriteLine(fsm.CurrentState); // Completed
Console.WriteLine(fsm.IsTerminal);   // true
```

### Création depuis la configuration YAML

```csharp
using Orkeon.Infrastructure.Configuration;

// La CircuitBreakerPolicyFactory résout la hiérarchie crew + task
var fsm = CircuitBreakerPolicyFactory.CreateTaskFsm(
    crewDefault: crewConfig.CircuitBreaker,
    taskOverride: taskConfig.CircuitBreaker
);

var guardCtx = CircuitBreakerPolicyFactory.CreateGuardContext(
    crewDefault: crewConfig.CircuitBreaker,
    taskOverride: taskConfig.CircuitBreaker
);
```

### Construction d'une FSM custom

Le framework générique permet de définir n'importe quel graphe d'états :

```csharp
var fsm = new StateMachineBuilder<MyState, MyEvent>()
    .WithInitialState(MyState.Idle)
    .WithTerminalStates(MyState.Done, MyState.Error)
    .WithDegradedState(MyState.Error)
    .WithCircuitBreaker(new CircuitBreakerPolicy
    {
        MaxTransitions = 50,
        StateTimeout = TimeSpan.FromMinutes(2),
        MaxStateVisits = 5,
        UseDegradedMode = true
    })
    .When(MyState.Idle, MyEvent.Start)
        .TransitionTo(MyState.Processing)
        .WithGuard<MyContext>(ctx => ctx.IsReady, "Must be ready")
        .WithAction((from, to) => Log($"{from} -> {to}"))
        .Done()
    .When(MyState.Processing, MyEvent.Complete)
        .TransitionTo(MyState.Done)
        .Done()
    .Build();
```

## Exemple 103

L'exemple `examples/06-engineering-devops/103-ts-codebase-with-fsm/` démontre l'intégration complète. Il reprend le scénario de l'exemple 102 (analyse de codebase TypeScript) en ajoutant :

- `circuitBreaker` au niveau crew avec preset "strict" et mode dégradé
- `circuitBreaker` au niveau task pour le superviseur (limites relevées car orchestration longue)
- Guards anti-hallucination d'outils (`maxToolCallsPerRound: 30`)
- Limite de retries réduite pour le superviseur (`maxRetries: 2`)

Voir le fichier `config.yaml` de l'exemple pour la syntaxe complète.

## Relation avec l'existant

### StateTransitionManager

Le `StateTransitionManager` existant (`Orkeon.Application.Services.StateManagement`) reste en place. Il valide les transitions d'états persistés (AgentStatus, CrewStatus, TaskStatus). La FSM `TaskExecutionStateMachine` opère à un niveau de granularité différent : elle gère le cycle d'exécution runtime (Assigned → Executing → ToolCalling → Validating → Completed) tandis que le StateTransitionManager gère les états de lifecycle (Pending → InProgress → Completed).

Les deux sont complémentaires : le StateTransitionManager gouverne le statut persisté, la FSM gouverne l'exécution en cours.

### SequentialCrewOrchestrator

L'orchestrateur existant n'est pas remplacé. La FSM s'intègre à l'intérieur des `IProcessStrategy` (Sequential, Hierarchical, Parallel) pour piloter l'exécution de chaque tâche individuelle, là où les boucles LLM sont les plus dangereuses.

## Tests

32+ tests unitaires couvrent le framework et la spécialisation :

| Fichier de test | Couverture |
|-----------------|-----------|
| `StateMachineTests.cs` | Transitions valides/invalides, états terminaux, cycle complet |
| `StateMachineGuardTests.cs` | Guards typés, priorité des guards, actions on-transition |
| `CircuitBreakerTests.cs` | Max transitions, détection de cycles, mode dégradé, reset, histogramme |
| `TaskExecutionStateMachineTests.cs` | Happy path, tool calls, retries, validation, cancel, circuit breaker |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/ --filter "FullyQualifiedName~StateMachine"
```
