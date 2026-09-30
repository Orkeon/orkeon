> 🇬🇧 [English version](../../architecture/domain-events.md)

# Événements, CQRS et observabilité

## Événements de domaine

`DomainEvent` (`Orkeon.Domain.SharedKernel.Events`) est le record de base abstrait de tout événement de domaine : `Id` (`DomainEventId`), `OccurredAt` (UTC), `EventName` (le nom du type) et `Version` (virtuelle, 1 par défaut, pour la compatibilité de sérialisation).

Les agrégats (`AggregateRoot<TEntityId>`, qui implémente `IHasDomainEvents`) mettent les événements en file avec la méthode protégée `RaiseDomainEvent()` ; `DomainEvents` expose ceux en attente et `ClearDomainEvents()` vide la liste.

### Dispatch

Le dispatch passe par `IDomainEventDispatcher` (Domain), implémenté par `DomainEventDispatcher` (`Orkeon.Infrastructure.DomainEvents`, singleton enregistré par `AddOrkeonInfrastructure()`) : pour chaque événement, il résout chaque `IDomainEventHandler<TEvent>` enregistré dans la DI et attend son `HandleAsync`, dans l'ordre (`DispatchAsync`, `DispatchManyAsync`) ; un événement sans handler est journalisé au niveau Debug.

Les événements sont dispatchés **quand une unité de travail est sauvegardée** :

1. Les repositories en mémoire appellent `IUnitOfWork.Track(aggregate)` à chaque ajout/mise à jour. `InMemoryUnitOfWork` garde **un agrégat par scope** (le dernier suivi — une commande, un agrégat).
2. `IUnitOfWork.SaveChangesAsync()` exécute l'étape de persistance (vide pour l'adaptateur en mémoire), dispatche les événements en attente de l'agrégat suivi, puis les efface — même quand un handler lève une exception.
3. `SaveChangesAsync()` est appelé par `UnitOfWorkCommandHandler`, le décorateur placé autour de **chaque handler de commande CQRS** (voir plus bas).

Une crew exécutée via `ICrewOrchestrationService` ne sauvegarde pas d'unité de travail : les événements que les agrégats lèvent pendant un kickoff (`CrewExecutionStartedEvent`, `CrewExecutionCompletedEvent`, …) restent en file sur l'agrégat et ne sont pas dispatchés. Pour observer un run, utilisez `ICrewExecutionHook` ([Callbacks et observabilité](#callbacks-et-observabilité)).

### Handlers

`AddOrkeonApplication()` parcourt l'assembly `Orkeon.Application` et enregistre chaque `IDomainEventHandler<T>` qu'il y trouve (scoped). Quatre sont livrés, tous des handlers de journalisation structurée : `AgentCompletedTaskHandler`, `AgentFailedTaskHandler`, `CrewExecutionCompletedHandler`, `CrewExecutionFailedHandler`. Un handler situé dans une autre assembly s'enregistre explicitement : `services.AddScoped<IDomainEventHandler<TaskCompletedEvent>, MyHandler>()`.

### Les 44 événements de domaine

| Famille | Événements | Levés par |
|--------|--------|-----------|
| Agent (10) | `AgentCreatedEvent`, `AgentAssignedToTaskEvent`, `AgentStartedTaskEvent`, `AgentCompletedTaskEvent`, `AgentFailedTaskEvent`, `AgentCapabilitiesUpdatedEvent`, `AgentCollaborationStartedEvent`, `AgentMemoryUpdatedEvent`, `AgentKilledEvent` | agrégat `Agent` |
| | `AgentSpawnedEvent` | déclaré, pas encore levé |
| Crew (11) | `CrewCreatedEvent`, `AgentJoinedCrewEvent`, `AgentLeftCrewEvent`, `TaskAddedToCrewEvent`, `TaskRemovedFromCrewEvent`, `CrewExecutionStartedEvent`, `CrewExecutionCompletedEvent`, `CrewExecutionFailedEvent`, `CrewProcessTypeChangedEvent`, `CrewGoalUpdatedEvent` | agrégat `Crew` |
| | `CrewCompletedEvent` | déclaré (avec une factory `FromOutput`), pas encore levé |
| Task (11) | `TaskCreatedEvent`, `TaskAssignedEvent`, `TaskStatusChangedEvent`, `TaskStartedEvent`, `TaskCompletedEvent`, `TaskFailedEvent`, `TaskCancelledEvent`, `TaskDependenciesUpdatedEvent` | agrégat `CrewTaskBase<TContext>` (`CrewTask`) |
| | `TaskContextUpdatedEvent` | collecté par `TypedTaskContext<T>` dans sa propre liste (`GetEvents()` / `ClearEvents()`), hors du chemin de dispatch |
| | `TaskBlockedEvent`, `TaskUnblockedEvent` | déclarés, pas encore levés |
| Memory (6) | `MemoryStoreCreatedEvent`, `MemoryAddedEvent`, `MemoryPromotedEvent`, `EntityMemoryUpdatedEvent`, `EpisodicMemoryAddedEvent`, `MemoryClearedEvent` | agrégat `AgentMemoryStore` |
| Delegation (5) | `TaskDelegatedEvent`, `DelegationCompletedEvent`, `DelegationQueuedEvent`, `AgentRegisteredForDelegationEvent`, `AgentUnregisteredFromDelegationEvent` | déclarés, pas encore levés |
| Human input (1) | `HumanInputRequestedEvent` | déclaré, pas encore levé |

Total : 44 événements de domaine — 34 levés par le modèle de domaine, 10 déclarés pour le cycle de vie qu'ils décrivent mais levés par aucun chemin de code pour l'instant. Les records d'événements vivent à côté de leur agrégat (`Agent/Events/`, `Crew/Events/`, `Task/Events/`, `Memory/Events/`, `Delegation/Events/`, `HumanInput/Events/`).

## CQRS et pipeline

La couche Application implémente CQRS avec `ICommand<TResponse>` / `ICommandHandler<TCommand, TResponse>` et `IQuery<TResponse>` / `IQueryHandler<TQuery, TResponse>` (`Orkeon.Application.Common.CQRS`), plus `ICommandValidator<TCommand>` (`Orkeon.Application.Validation`). `AddOrkeonApplication()` parcourt l'assembly Application et les enregistre ; il n'y a pas de médiateur — résolvez l'interface du handler depuis la DI et appelez `HandleAsync`.

Chaque handler de commande est enregistré derrière deux décorateurs :

```
ValidatingCommandHandler   (seulement si un validateur existe — lève CommandValidationException)
  └── UnitOfWorkCommandHandler   (IUnitOfWork.SaveChangesAsync → événements de domaine dispatchés)
        └── le handler concret
```

Les handlers de requête sont enregistrés tels quels (pas d'unité de travail).

| Nature | Types | Validateur |
|------|-------|-----------|
| Commandes | `CreateAgentCommand`, `CreateCrewCommand`, `CreateTaskCommand` | ✅ (`CreateAgentCommandValidator`, `CreateCrewCommandValidator`, `CreateTaskCommandValidator`) |
| | `AddMemoryCommand`, `CreateMemoryStoreCommand` | — |
| Requêtes | `GetAgentQuery`, `GetCrewQuery`, `GetTaskQuery`, `SearchMemoryQuery` | — |

## Callbacks et observabilité

Trois surfaces d'observation existent, à des niveaux différents :

**`ICrewExecutionHook`** (`Orkeon.Application.Crew`) — le hook de niveau run que chaque stratégie de process appelle, sur toutes les sorties (succès, échec, annulation) : `OnTaskStartedAsync`, `OnTaskCompletedAsync`, `OnCrewCompletedAsync`, `OnCrewFailedAsync`, avec des instantanés de tâche et de crew (statut, durée, tokens, ventilation du cache, tâches sautées). C'est un service DI unique ; une exception qu'il lève est journalisée et absorbée. Implémentations livrées : `AutoSummaryWriter` (`AUTO_SUMMARY.md`), l'observateur de `orkeon run --events` (voir [le bus d'événements du run](./run-event-bus.md)) et le hook de progression d'`orkeon-host`.

**`ICallbackHandler`** (`Orkeon.Application.Callback`) — notifications au niveau tâche et étape : `OnStepStartedAsync`, `OnStepCompletedAsync`, `OnTaskStartedAsync`, `OnTaskProgressAsync`, `OnTaskCompletedAsync`, `OnFlowStepStartedAsync`, `OnFlowStepCompletedAsync`. `CallbackOrchestrator` (`Orkeon.Application.Execution`, l'`ICallbackOrchestrator` scoped) diffuse chaque notification à chaque `ICallbackHandler` enregistré dans la DI ainsi qu'aux `CallbackHandlers` passés à l'appel ; `AgentExecutionService` appelle `NotifyTaskStartedAsync` et `NotifyTaskCompletedAsync` autour de chaque tâche. L'interface a aussi `NotifyStepProgressAsync`, et la classe `NotifyToolUsedAsync` / `NotifyDelegationAsync`. `BaseCallbackHandler` est la base dont dériver ; `LoggingCallbackHandler` journalise chaque notification mais n'est pas enregistré par défaut.

**`IStepCallback`** (`Orkeon.Domain.Agent` : `OnStepStartAsync`, `OnStepCompletedAsync`, `OnStepFailedAsync`) et **`ITaskCallback`** (`Orkeon.Domain.Task` : `OnTaskStartAsync`, `OnTaskCompletedAsync`, `OnTaskFailedAsync`) — callbacks de niveau domaine portés par les agrégats (`AgentBuilder.WithStepCallback`, `Crew.StepCallback` / `Crew.TaskCallback` ; défauts DI `NullStepCallback` / `NullTaskCallback`). Le moteur d'exécution ne les invoque pas aujourd'hui : préférez `ICrewExecutionHook` ou `ICallbackHandler`.

---

> **Voir aussi** : [ProcessTypes](../orchestration/process-types.md) · [Sécurité](./security.md) · [Retour à l'index](../INDEX.md)
