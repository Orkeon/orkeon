> 🇬🇧 [English version](../../orchestration/fsm.md)

> **Voir aussi** : [Guide comparatif ProcessTypes](./process-types.md) · [Schéma YAML](../architecture/yaml-schema.md) · [Retour à l'index](../INDEX.md)

# Moteur de machine à états finis (FSM)

## Vue d'ensemble

Orkeon fournit un framework de machine à états finis générique (`StateMachine<TState, TEvent>`) dans la couche Domain, avec un circuit breaker intégré (`CircuitBreakerPolicy`) qui arrête une boucle emballée. C'est une brique pour votre propre code et pour trois consommateurs livrés :

- **`orkeon forge`** déroule le cycle de sa session (brief → blueprint → rendu → validation → test → diagnostic → verdict) sur une `StateMachine<ForgeState, ForgeTrigger>` (`ForgeStateMachineFactory`).
- **Le [mode Graph](./graph.md)** borne les runs de son `StateGraph` avec une `CircuitBreakerPolicy` construite depuis le `graphConfig` de la crew.
- **Le graphe RAG correctif** (`CorrectiveRagPipeline`) borne sa boucle récupérer → évaluer → régénérer avec sa propre `CircuitBreakerPolicy`.

> **Pas de machine à états de tâche.** Les tâches d'une crew ne passent par aucune FSM. Ce qui borne une tâche, c'est sa boucle d'agent : le `maxIter` de l'agent, un arrêt après 3 erreurs d'outil identiques consécutives, et les reprises de validation de sortie. L'ancienne spécialisation tâche (`TaskExecutionStateMachine`) et les blocs YAML `circuitBreaker:` qui la configuraient n'ont jamais été exécutés et ont été supprimés ; une crew qui écrit encore `circuitBreaker:` est refusée au chargement. En YAML, le seul réglage de circuit breaker est le `graphConfig` d'une crew Graph (voir [Schéma YAML](../architecture/yaml-schema.md)).

## Architecture

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

`CircuitBreakerPolicyFactory` (`Orkeon.Infrastructure.Configuration`) transforme le `graphConfig` d'une crew Graph en policy (`ResolveGraph`).

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
- `true` : la machine passe dans l'état dégradé déclaré avec `WithDegradedState(...)` ; sans état dégradé déclaré, elle lève l'exception comme ci-dessus

`TryFire()` renvoie `false` au lieu de lever, pour un disjoncteur déclenché comme pour une transition absente. Un guard qui rejette toutes les transitions candidates se traduit par une `InvalidTransitionException`. `ResetCircuitBreaker()` efface le déclenchement et les compteurs.

### Presets

Trois presets sont fournis via `CircuitBreakerPolicy` :

| Preset | MaxTransitions | StateTimeout | MaxStateVisits | MaxTotalDuration | DegradedMode |
|--------|---------------|-------------|----------------|------------------|--------------|
| `Strict` | 50 | 2 min | 5 | 10 min | true |
| `Default` | 100 | 5 min | 10 | 30 min | false |
| `Permissive` | 1000 | 30 min | 50 | 2 h | false |

`new CircuitBreakerPolicy()` équivaut à `Default`. `Strict` est le repli de `StateGraph` et de `CircuitBreakerPolicyFactory.ResolveGraph` quand `graphConfig` ne nomme aucun preset (ou un preset inconnu).

### Observabilité

La machine expose deux événements :

- `OnTransition` : levé après chaque transition réussie, avec un `TransitionResult` (`FromState`, `ToState`, `Trigger`, `TransitionOrdinal`, `Timestamp`, `StateChanged`)
- `OnCircuitBroken` : levé quand le circuit se déclenche, avec un `CircuitBreakerStatus` incluant un histogramme des visites par état

`CircuitBreakerStatus` fournit un instantané complet : `IsBroken`, `BrokenReason`, `TotalTransitions`, `TimeInCurrentState`, `CurrentStateVisitCount`, `TotalElapsed`, `StateVisitHistogram`. L'interface en lecture seule offre aussi `CurrentState`, `IsTerminal`, `TransitionCount`, `GetPermittedEvents()` et `CanFire(event)`.

## Guards typés

Une transition peut porter un guard sur un contexte typé — `WithGuard<TContext>(prédicat, description)` — et une action. `Fire(event, context)` évalue les guards des transitions candidates dans l'ordre de déclaration et retient la première qui passe ; quand tous les guards refusent, l'appel lève une `InvalidTransitionException` (`TryFire` renvoie `false`).

## Utilisation en code C#

### Construire une machine à états

Le framework permet de définir n'importe quel graphe d'états :

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

## Relation avec l'existant

### StateTransitionManager

Le `StateTransitionManager` (`Orkeon.Application.Services.StateManagement`) valide les transitions d'états persistés (AgentStatus, CrewStatus, TaskStatus). Il gouverne les états de cycle de vie (Pending → InProgress → Completed) et n'utilise pas ce moteur.

### Stratégies de process

Les implémentations de `IProcessStrategy` exécutent une tâche via `IAgentExecutionService` et sa boucle d'agent ; aucune ne construit de machine à états. Le mode Graph est la seule stratégie qui applique une `CircuitBreakerPolicy`, au runner de son `StateGraph`.

## Tests

| Fichier de test | Couverture |
|-----------------|-----------|
| `Orkeon.Domain.Tests/Common/StateMachine/StateMachineTests.cs` | Transitions valides/invalides, états terminaux, cycle complet |
| `Orkeon.Domain.Tests/Common/StateMachine/StateMachineGuardTests.cs` | Guards typés, priorité des guards, actions sur transition |
| `Orkeon.Domain.Tests/Common/StateMachine/CircuitBreakerTests.cs` | Transitions max, détection de cycles, mode dégradé, reset, histogramme |
| `Orkeon.Infrastructure.Tests/Configuration/CircuitBreakerPolicyFactoryGraphTests.cs` | `graphConfig` → policy : presets, surcharges, repli |
| `Orkeon.Scripting.Cli.Tests/Forge/ForgeStateMachineTests.cs` | Les coups légaux du cycle forge |

```bash
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj --filter "FullyQualifiedName~StateMachine"
```
