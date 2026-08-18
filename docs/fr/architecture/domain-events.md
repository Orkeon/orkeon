> 🇬🇧 [English version](../../architecture/domain-events.md)

# Événements, CQRS et observabilité

## Domain Events

Le framework implémente un système d'événements domaine complet. `DomainEvent` (`Orkeon.Domain.SharedKernel.Events`) est la classe de base abstraite (record) avec `Id`, `OccurredAt` et `Version`.

Les agrégats émettent des événements via `RaiseDomainEvent()` (hérité de `AggregateRoot<T>`). Le dispatch est assuré par `DomainEventDispatcher` (`Orkeon.Infrastructure.DomainEvents`) qui résout les `IDomainEventHandler<TEvent>` enregistrés via DI.

Événements Agent (10 types) : `AgentCreatedEvent`, `AgentAssignedToTaskEvent`, `AgentStartedTaskEvent`, `AgentCompletedTaskEvent`, `AgentFailedTaskEvent`, `AgentCapabilitiesUpdatedEvent`, `AgentCollaborationStartedEvent`, `AgentMemoryUpdatedEvent`, `AgentKilledEvent`, `AgentSpawnedEvent`.

Événements Crew (11 types) : `CrewCreatedEvent`, `AgentJoinedCrewEvent`, `AgentLeftCrewEvent`, `TaskAddedToCrewEvent`, `TaskRemovedFromCrewEvent`, `CrewExecutionStartedEvent`, `CrewExecutionCompletedEvent`, `CrewExecutionFailedEvent`, `CrewCompletedEvent`, `CrewProcessTypeChangedEvent`, `CrewGoalUpdatedEvent`.

Événements Task (11 types) : `TaskCreatedEvent`, `TaskAssignedEvent`, `TaskStatusChangedEvent`, `TaskStartedEvent`, `TaskCompletedEvent`, `TaskFailedEvent`, `TaskCancelledEvent`, `TaskDependenciesUpdatedEvent`, `TaskContextUpdatedEvent`, `TaskBlockedEvent`, `TaskUnblockedEvent`.

Événements Memory (6 types) : `MemoryStoreCreatedEvent`, `MemoryAddedEvent`, `MemoryPromotedEvent`, `EntityMemoryUpdatedEvent`, `EpisodicMemoryAddedEvent`, `MemoryClearedEvent`.

Événements Delegation (5 types) : `TaskDelegatedEvent`, `DelegationCompletedEvent`, `DelegationQueuedEvent`, `AgentRegisteredForDelegationEvent`, `AgentUnregisteredFromDelegationEvent`.

Événements de saisie humaine (1 type) : `HumanInputRequestedEvent`.

Total : 44 événements domaine couvrant l'intégralité du cycle de vie agents, crews, tasks, mémoire, délégation et saisie humaine.

## CQRS et pipeline

La couche Application implémente le pattern CQRS avec les interfaces `ICommand`/`ICommandHandler<TCommand, TResult>` et `IQuery<TResult>`/`IQueryHandler<TQuery, TResult>` (`Orkeon.Application.Common`). Les handlers sont auto-scannés et enregistrés au démarrage via `AddCqrsHandlers()`.

Commandes existantes : `CreateAgentCommand`, `CreateCrewCommand`, `CreateTaskCommand`, `AddMemoryCommand`, `CreateMemoryStoreCommand`. Queries : `GetAgentQuery`, `GetCrewQuery`, `GetTaskQuery`, `SearchMemoryQuery`.

## Callbacks et observabilité

Deux niveaux de callbacks sont disponibles :

- `IStepCallback` (`Orkeon.Domain.Agent`) : `OnStepStartAsync`, `OnStepCompletedAsync`, `OnStepFailedAsync` — granularité itération agent
- `ITaskCallback` (`Orkeon.Domain.Task`) : `OnTaskStartAsync`, `OnTaskCompletedAsync`, `OnTaskFailedAsync` — granularité tâche

`CallbackOrchestrator` (`Orkeon.Application.Execution`) centralise le dispatch des notifications avec des méthodes dédiées : `NotifyTaskStartedAsync`, `NotifyTaskCompletedAsync`, `NotifyStepProgressAsync`, `NotifyToolUsedAsync`, `NotifyDelegationAsync`.

`LoggingCallbackHandler` (`Orkeon.Application.Callback`) fournit une implémentation par défaut qui journalise tous les événements.

---

> **Voir aussi** : [Sécurité](../architecture/security.md) · [Retour à l'index](../INDEX.md)
