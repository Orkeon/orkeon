> 🇫🇷 [Version française](../fr/architecture/domain-events.md)

# Events, CQRS and Observability

## Domain Events

The framework implements a complete domain event system. `DomainEvent` (`Orkeon.Domain.Shared.Events`) is the abstract base class (record) with `Id`, `OccurredAt` and `Version`.

Aggregates emit events via `RaiseDomainEvent()` (inherited from `AggregateRoot<T>`). Dispatch is handled by `DomainEventDispatcher` (`Orkeon.Infrastructure.DomainEvents`), which resolves the `IDomainEventHandler<TEvent>` instances registered through DI.

Agent events (10 types): `AgentCreatedEvent`, `AgentAssignedToTaskEvent`, `AgentStartedTaskEvent`, `AgentCompletedTaskEvent`, `AgentFailedTaskEvent`, `AgentCapabilitiesUpdatedEvent`, `AgentCollaborationStartedEvent`, `AgentMemoryUpdatedEvent`, `AgentKilledEvent`, `AgentSpawnedEvent`.

Crew events (10 types): `CrewCreatedEvent`, `AgentJoinedCrewEvent`, `AgentLeftCrewEvent`, `TaskAddedToCrewEvent`, `TaskRemovedFromCrewEvent`, `CrewExecutionStartedEvent`, `CrewExecutionCompletedEvent`, `CrewExecutionFailedEvent`, `CrewProcessTypeChangedEvent`, `CrewGoalUpdatedEvent`.

Task events (10 types): `TaskCreatedEvent`, `TaskAssignedEvent`, `TaskStatusChangedEvent`, `TaskStartedEvent`, `TaskCompletedEvent`, `TaskFailedEvent`, `TaskCancelledEvent`, `TaskDependenciesUpdatedEvent`, `TaskBlockedEvent`, `TaskUnblockedEvent`.

Memory events (6 types): `MemoryStoreCreatedEvent`, `MemoryAddedEvent`, `MemoryPromotedEvent`, `EntityMemoryUpdatedEvent`, `EpisodicMemoryAddedEvent`, `MemoryClearedEvent`.

Delegation events (5 types): `TaskDelegatedEvent`, `DelegationCompletedEvent`, `DelegationQueuedEvent`, `AgentRegisteredForDelegationEvent`, `AgentUnregisteredFromDelegationEvent`.

Total: 41 domain events covering the entire lifecycle of agents, crews, tasks, memory and delegation.

## CQRS and pipeline

The Application layer implements the CQRS pattern with the `ICommand`/`ICommandHandler<TCommand, TResult>` and `IQuery<TResult>`/`IQueryHandler<TQuery, TResult>` interfaces (`Orkeon.Application.Common`). Handlers are auto-scanned and registered at startup via `AddCqrsHandlers()`.

Existing commands: `CreateCrewCommand`, `CreateTaskCommand`. Queries: `GetCrewQuery`, `GetTaskQuery`, `SearchMemoryQuery`.

## Callbacks and observability

Two levels of callbacks are available:

- `IStepCallback` (`Orkeon.Domain.Agent`): `OnStepStartAsync`, `OnStepCompletedAsync`, `OnStepFailedAsync` — agent-iteration granularity
- `ITaskCallback` (`Orkeon.Domain.Task`): `OnTaskStartAsync`, `OnTaskCompletedAsync`, `OnTaskFailedAsync` — task granularity

`CallbackOrchestrator` (`Orkeon.Application.Execution`) centralizes notification dispatch with dedicated methods: `NotifyTaskStartedAsync`, `NotifyTaskCompletedAsync`, `NotifyStepProgressAsync`, `NotifyToolUsedAsync`, `NotifyDelegationAsync`.

`LoggingCallbackHandler` (`Orkeon.Application.Callback`) provides a default implementation that logs all events.

---

> **See also**: [Security](./security.md) · [Back to index](../INDEX.md)
