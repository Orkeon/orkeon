> 🇬🇧 [English version](../../orchestration/fsm.md)

> **Voir aussi** : [Guide comparatif ProcessTypes](./process-types.md) · [Schéma YAML](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)

# Orchestration par machine à états finis (FSM)

## Vue d'ensemble

Orkeon fournit un framework de machine à états finis générique (`StateMachine<TState, TEvent>`) dans la couche Domain, avec un circuit breaker intégré pour prévenir les boucles récursives incontrôlées lors de l'orchestration multi-agents. Il est livré avec une spécialisation prête à l'emploi pour le cycle de vie d'exécution des tâches (`TaskExecutionStateMachine`) et s'étend à d'autres domaines.

> **Ce qui s'exécute aujourd'hui.** La FSM est une brique Domain : **aucune stratégie de process ne fait encore passer ses tâches par `TaskExecutionStateMachine`.** Le bloc YAML `circuitBreaker` est parsé aux deux niveaux, mais à l'exécution seul le bloc **de niveau crew** est lu, par le [mode Graph](./graph.md) (quand aucun `graphConfig` n'est déclaré) ; le bloc de niveau tâche et les trois limites de guards sont résolus par `CircuitBreakerPolicyFactory` mais aucun chemin d'exécution n'appelle cette résolution pour les tâches (voir [Schéma YAML](../architecture/yaml-schema.md#configuration-circuit-breaker)). Ce qui borne une tâche à l'exécution, c'est la boucle de l'agent : `maxIter` itérations, et un arrêt après 3 erreurs d'outil identiques consécutives.

## Architecture

### Couche Domain — Framework générique

Les composants du framework se trouvent dans `Orkeon.Domain.Common.StateMachine` :

| Classe | Rôle |
|--------|------|
| `StateMachine<TState, TEvent>` | Moteur FSM thread-safe (`lock`) avec circuit breaker intégré |
| `StateMachineBuilder<TState, TEvent>` | API fluent pour déclarer le graphe d'états |
| `CircuitBreakerPolicy` | Configuration des seuils de protection, presets `Strict` / `Default` / `Permissive` |
| `CircuitBreakerStatus` | Instantané du disjoncteur (même fichier que la policy) |
| `TransitionResult<TState, TEvent>` | Résultat d'une transition (états, déclencheur, ordinal, horodatage) |
| `IStateMachine<TState, TEvent>` | Interface lecture seule pour l'observation |
| `IMutableStateMachine<TState, TEvent>` | Interface avec `Fire()`, `TryFire()`, `ResetCircuitBreaker()`, événements |
| `CircuitBrokenException`, `InvalidTransitionException<TState, TEvent>` | Disjoncteur déclenché ; aucune transition pour cet état + événement |

### Couche Domain — Spécialisation Task

Les composants spécifiques au cycle de vie des tâches se trouvent dans `Orkeon.Domain.Task` :

| Classe | Rôle |
|--------|------|
| `TaskExecutionState` | Enum des états d'exécution (Assigned, Planning, Executing, etc.) — `TaskExecutionState.cs` |
| `TaskExecutionEvent` | Enum des événements (StartPlanning, RequestToolCall, etc.) — même fichier |
| `TaskExecutionStateMachine` | Factory statique (`Create`, `CreateBuilder`) avec guards et graphe complet |
| `TaskExecutionGuardContext` | Contexte typé pour les guards (retries, appels d'outils, validation) — même fichier que la factory |

### Couche Infrastructure — Intégration YAML

| Classe | Rôle |
|--------|------|
| `CircuitBreakerYamlConfig` | Modèle YAML de la section `circuitBreaker` |
| `CircuitBreakerPolicyFactory` | Convertit la config en `CircuitBreakerPolicy`, en FSM de tâche ou en contexte de guards ; `ResolveGraph` sert le mode Graph |

### Couche Domain — Configuration

| Classe | Rôle |
|--------|------|
| `CircuitBreakerConfig` | DTO immuable de la configuration du circuit breaker (`CrewConfiguration.CircuitBreaker`, `TaskConfiguration.CircuitBreaker`, `Crew.CircuitBreaker`) |

## Graphe d'états de la TaskExecutionStateMachine

```
[Assigned] ─► [Planning] ─► [Executing] ⇄ [ToolCalling]
                                │  ▲
                                ▼  │ ValidationFailed / Retry / HumanInputReceived
                          [Validating] ─► [Completed]
                          [Failed] · [WaitingForHumanInput]

Cancel (tout état non terminal) ─► [Cancelled]      déclenchement du disjoncteur (mode dégradé) ─► [Degraded]
```

| Depuis | Événement | Vers | Guard |
|--------|-----------|------|-------|
| `Assigned` | `StartPlanning` | `Planning` | — |
| `Assigned`, `Planning` | `BeginExecution` | `Executing` | — |
| `Executing` | `RequestToolCall` | `ToolCalling` | `CanCallTool` |
| `ToolCalling` | `ToolCallCompleted`, `ToolCallFailed` | `Executing` | — |
| `Executing` | `SubmitForValidation` | `Validating` | — |
| `Validating` | `ValidationPassed` | `Completed` | — |
| `Validating` | `ValidationFailed` | `Executing` | `CanRetryValidation` |
| `Executing` | `RequestHumanInput` | `WaitingForHumanInput` | — |
| `WaitingForHumanInput` | `HumanInputReceived` | `Executing` | — |
| `Executing`, `Validating` | `Fail` | `Failed` | — |
| `Failed` | `Retry` | `Executing` | `CanRetry` |
| tout état non terminal | `Cancel` | `Cancelled` | — |

`ToolCallFailed` revient à `Executing` (l'agent décide quoi faire de l'erreur). États terminaux : `Completed`, `Cancelled`, `Degraded`.

## Circuit breaker

Le circuit breaker est intégré directement dans `StateMachine<TState, TEvent>` et vérifie quatre conditions avant chaque transition :

### Mécanismes de protection

| Mécanisme | Paramètre | Description |
|-----------|-----------|-------------|
| Transitions max | `MaxTransitions` | Nombre total de transitions autorisées |
| Timeout par état | `StateTimeout` | Temps maximal depuis la dernière transition (`TimeSpan.Zero` le désactive) |
| Détection de cycles | `MaxStateVisits` | Nombre maximal de visites d'un même état (0 le désactive) |
| Durée totale | `MaxTotalDuration` | Durée de vie maximale depuis la première transition (`TimeSpan.Zero` la désactive) |

Quand une condition est violée, deux comportements sont possibles selon `UseDegradedMode` :

- `false` : une `CircuitBrokenException` est levée avec un `CircuitBreakerStatus` détaillé
- `true` : la machine passe dans l'état dégradé déclaré avec `WithDegradedState(...)` (par ex. `TaskExecutionState.Degraded`) ; sans état dégradé déclaré, elle lève l'exception comme ci-dessus

`TryFire()` renvoie `false` au lieu de lever, pour un disjoncteur déclenché comme pour une transition absente. Un guard qui rejette toutes les transitions candidates se traduit par une `InvalidTransitionException`. `ResetCircuitBreaker()` efface le déclenchement et les compteurs.

### Presets

Trois presets sont fournis via `CircuitBreakerPolicy` :

| Preset | MaxTransitions | StateTimeout | MaxStateVisits | MaxTotalDuration | DegradedMode |
|--------|---------------|-------------|----------------|------------------|--------------|
| `Strict` | 50 | 2 min | 5 | 10 min | true |
| `Default` | 100 | 5 min | 10 | 30 min | false |
| `Permissive` | 1000 | 30 min | 50 | 2 h | false |

`new CircuitBreakerPolicy()` équivaut à `Default`. `Strict` est le repli de `TaskExecutionStateMachine.Create()` et de `CircuitBreakerPolicyFactory` quand rien (ou un preset inconnu) n'est configuré.

### Observabilité

La machine expose deux événements :

- `OnTransition` : levé après chaque transition réussie, avec un `TransitionResult` (`FromState`, `ToState`, `Trigger`, `TransitionOrdinal`, `Timestamp`, `StateChanged`)
- `OnCircuitBroken` : levé quand le circuit se déclenche, avec un `CircuitBreakerStatus` incluant un histogramme des visites par état

`CircuitBreakerStatus` fournit un instantané complet : `IsBroken`, `BrokenReason`, `TotalTransitions`, `TimeInCurrentState`, `CurrentStateVisitCount`, `TotalElapsed`, `StateVisitHistogram`. L'interface en lecture seule offre aussi `CurrentState`, `IsTerminal`, `TransitionCount`, `GetPermittedEvents()` et `CanFire(event)`.

## Guards typés

La `TaskExecutionStateMachine` utilise trois guards typés via `TaskExecutionGuardContext` pour empêcher les boucles dangereuses :

| Guard | Transition protégée | Condition |
|-------|---------------------|-----------|
| Budget d'appels d'outils | `Executing → ToolCalling` | `ToolCallCount < MaxToolCallsPerRound && IsToolRegistered` (`CanCallTool`) |
| Limite de retries | `Failed → Executing` | `RetryCount < MaxRetries` (`CanRetry`) |
| Limite de validation | `Validating → Executing` | `ValidationAttempts < MaxValidationRetries` (`CanRetryValidation`) |

Défauts : `MaxRetries = 3`, `MaxToolCallsPerRound = 10`, `MaxValidationRetries = 3`, `IsToolRegistered = true`. Le guard `IsToolRegistered` bloque les appels à des outils non enregistrés sur l'agent, ce qui empêche les hallucinations d'outils par le LLM.

## Configuration YAML

### Schéma `circuitBreaker`

Le bloc `circuitBreaker` peut être déclaré à deux niveaux d'un fichier de crew (voir l'état à l'exécution dans la [Vue d'ensemble](#vue-densemble)) :

```yaml
# Niveau crew — défauts pour toutes les tâches (lu par le mode Graph)
circuitBreaker:
  preset: string              # "strict" (aussi le repli) | "permissive" | "default"
  maxTransitions: int         # Surcharge le preset
  stateTimeoutSeconds: int    # Timeout par état en secondes
  maxStateVisits: int         # Détection de cycles
  maxTotalDurationSeconds: int # Durée totale en secondes
  useDegradedMode: bool       # true = Degraded, false = exception
  maxRetries: int             # Retries après échec (guard, défaut 3)
  maxToolCallsPerRound: int   # Appels d'outils max par round (guard, défaut 10)
  maxValidationRetries: int   # Boucles de validation max (guard, défaut 3)

tasks:
  <task_id>:
    description: string
    # Niveau tâche — surcharge pour cette tâche (parsée, pas encore appliquée)
    circuitBreaker:
      maxTransitions: int     # Surcharge le défaut de la crew
      stateTimeoutSeconds: int
      # ... mêmes champs que ci-dessus
```

Les clés s'écrivent en camelCase ou en snake_case (`max_transitions`), comme le reste du schéma.

### Hiérarchie de résolution (`CircuitBreakerPolicyFactory.Resolve`)

```
Limites de la policy (champ par champ) :
1. circuitBreaker de niveau tâche   (priorité la plus haute)
2. circuitBreaker de niveau crew
3. Preset nommé                     (celui de la tâche, sinon celui de la crew)
4. CircuitBreakerPolicy.Strict      (pas de preset, preset inconnu ou rien de configuré)

Limites des guards (CreateGuardContext — bloc entier) :
   le bloc de la tâche s'il existe, sinon celui de la crew, sinon 3 / 10 / 3
```

Chaque champ individuel de la policy surcharge la valeur du preset : un preset « strict » avec `maxTransitions: 200` garde toutes les valeurs du preset sauf les transitions. Les limites des guards ne fusionnent pas champ par champ — un bloc de tâche sans `maxRetries` retombe sur 3, pas sur la valeur de la crew.

### Modèles YAML

| Modèle C# | Classe YAML | Fichier |
|-----------|-------------|---------|
| `CircuitBreakerConfig` | `CircuitBreakerYamlConfig` (sur `CrewYamlConfig`, `CrewSettingsYamlConfig` et `TaskYamlConfig`) | `Configuration/Yaml/YamlConfigModels.cs` |

Le mapping est effectué par `YamlCrewMapper.MapCircuitBreaker()` (privée, `Configuration/Yaml/`) ; `CrewFactory` reporte le bloc de niveau crew sur l'agrégat `Crew` (`Crew.CircuitBreaker`, également réglable avec `CrewBuilder.WithCircuitBreaker(...)`). La conversion en `CircuitBreakerPolicy` exécutable est faite par `CircuitBreakerPolicyFactory.Resolve()`.

## Utilisation en code C#

### Création manuelle

```csharp
using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Task;

// Créer une FSM avec le preset Strict (aussi le défaut de Create())
var fsm = TaskExecutionStateMachine.Create(CircuitBreakerPolicy.Strict);

// Observer les transitions
fsm.OnTransition += (_, result) =>
    Console.WriteLine($"{result.FromState} -> {result.ToState} via {result.Trigger}");

fsm.OnCircuitBroken += (_, status) =>
    Console.WriteLine($"CIRCUIT BROKEN: {status.BrokenReason}");

// Contexte des guards
var ctx = new TaskExecutionGuardContext
{
    RetryCount = 0,
    MaxRetries = 3,
    ToolCallCount = 0,
    MaxToolCallsPerRound = 10,
    IsToolRegistered = true,
};

// Dérouler le workflow
fsm.Fire(TaskExecutionEvent.BeginExecution);
fsm.Fire(TaskExecutionEvent.RequestToolCall, ctx);
fsm.Fire(TaskExecutionEvent.ToolCallCompleted);
fsm.Fire(TaskExecutionEvent.SubmitForValidation);
fsm.Fire(TaskExecutionEvent.ValidationPassed);

Console.WriteLine(fsm.CurrentState); // Completed
Console.WriteLine(fsm.IsTerminal);   // true
```

### Création depuis la configuration YAML

Aucun chemin d'exécution ne fait cet appel : pour faire passer une tâche par une FSM configurée par les blocs YAML, votre propre code la construit avec `CircuitBreakerPolicyFactory` et déclenche ses événements.

```csharp
using Orkeon.Infrastructure.Configuration;

// crewConfig / taskConfig : la CrewConfiguration / TaskConfiguration produite par le loader
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

`AddTransition(from, trigger, to)` est le raccourci pour une transition sans guard ni action ; `WithStateKey` / `WithEventKey` fournissent les fonctions de clé quand `TState` / `TEvent` ne sont pas des enums.

> Le DSL de scripting a son propre littéral `stateMachine()` (`fsm.d.ts`), une implémentation JavaScript distincte sans circuit breaker — voir [Scripting](../architecture/scripting.md).

## Exemple 103

L'exemple [103-ts-codebase-with-fsm](https://github.com/orkeon/orkeon/blob/main/examples/06-engineering-devops/103-ts-codebase-with-fsm/) montre la syntaxe complète de `circuitBreaker` sur le scénario de l'exemple 102 (analyse d'une codebase TypeScript) :

- `circuitBreaker` au niveau crew avec le preset « strict » et le mode dégradé
- `circuitBreaker` au niveau tâche pour le superviseur (limites relevées à cause de la longue orchestration)
- Guards anti-hallucination d'outils (`maxToolCallsPerRound: 30`)
- Limite de retries réduite pour le superviseur (`maxRetries: 2`)

La crew est en `process: sequential` : aujourd'hui ces blocs sont parsés et validés mais pas appliqués (voir la [Vue d'ensemble](#vue-densemble)).

## Relation avec l'existant

### StateTransitionManager

Le `StateTransitionManager` (`Orkeon.Application.Services.StateManagement`) valide les transitions d'états persistés (AgentStatus, CrewStatus, TaskStatus). La `TaskExecutionStateMachine` modélise un autre niveau de granularité : le cycle d'exécution runtime (Assigned → Executing → ToolCalling → Validating → Completed), là où le StateTransitionManager gouverne les états de cycle de vie (Pending → InProgress → Completed).

### Stratégies de process

Les implémentations de `IProcessStrategy` exécutent une tâche via `IAgentExecutionService` et sa boucle d'agent ; aucune n'instancie la FSM de tâche. Le seul consommateur du circuit breaker à l'exécution est le [mode Graph](./graph.md), qui applique `CircuitBreakerPolicy` au runner de son `StateGraph`.

## Tests

32 méthodes de test couvrent le framework et la spécialisation :

| Fichier de test | Couverture |
|-----------------|-----------|
| `Common/StateMachine/StateMachineTests.cs` | Transitions valides/invalides, états terminaux, cycle complet |
| `Common/StateMachine/StateMachineGuardTests.cs` | Guards typés, priorité des guards, actions sur transition |
| `Common/StateMachine/CircuitBreakerTests.cs` | Transitions max, détection de cycles, mode dégradé, reset, histogramme |
| `Task/TaskExecutionStateMachineTests.cs` | Chemin nominal, appels d'outils, retries, validation, annulation, circuit breaker |

`CircuitBreakerPolicyFactory` est couverte dans `Orkeon.Infrastructure.Tests` (`Configuration/CovSecurity_CircuitBreakerPolicyFactoryTests.cs`, `Configuration/CircuitBreakerPolicyFactoryGraphTests.cs`).

```bash
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --filter "FullyQualifiedName~StateMachine"
```
