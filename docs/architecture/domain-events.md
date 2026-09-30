> 🇫🇷 [Version française](../fr/architecture/domain-events.md)

# Events, CQRS and Observability

## Domain Events

`DomainEvent` (`Orkeon.Domain.SharedKernel.Events`) is the abstract base record of every domain event: `Id` (`DomainEventId`), `OccurredAt` (UTC), `EventName` (the type name) and `Version` (virtual, 1 by default, for serialization compatibility).

Aggregates (`AggregateRoot<TEntityId>`, which implements `IHasDomainEvents`) queue events with the protected `RaiseDomainEvent()`; `DomainEvents` exposes the pending ones and `ClearDomainEvents()` empties the list.

### Dispatch

Dispatch goes through `IDomainEventDispatcher` (Domain), implemented by `DomainEventDispatcher` (`Orkeon.Infrastructure.DomainEvents`, singleton registered by `AddOrkeonInfrastructure()`): for each event it resolves every `IDomainEventHandler<TEvent>` registered in DI and awaits its `HandleAsync`, in order (`DispatchAsync`, `DispatchManyAsync`); an event without a handler is logged at Debug level.

Events are dispatched **when a unit of work is saved**:

1. The in-memory repositories call `IUnitOfWork.Track(aggregate)` on every add/update. `InMemoryUnitOfWork` keeps **one aggregate per scope** (the last tracked one — one command, one aggregate).
2. `IUnitOfWork.SaveChangesAsync()` runs the persist step (empty for the in-memory adapter), dispatches the tracked aggregate's pending events, then clears them — even when a handler throws.
3. `SaveChangesAsync()` is called by `UnitOfWorkCommandHandler`, the decorator wrapped around **every CQRS command handler** (see below).

A crew run through `ICrewOrchestrationService` does not save a unit of work: the events the aggregates raise during a kickoff (`CrewExecutionStartedEvent`, `CrewExecutionCompletedEvent`, …) stay queued on the aggregate and are not dispatched. To observe a run, use `ICrewExecutionHook` ([Callbacks and observability](#callbacks-and-observability)).

### Handlers

`AddOrkeonApplication()` scans the `Orkeon.Application` assembly and registers every `IDomainEventHandler<T>` it finds (scoped). Four ship, all of them structured-logging handlers: `AgentCompletedTaskHandler`, `AgentFailedTaskHandler`, `CrewExecutionCompletedHandler`, `CrewExecutionFailedHandler`. A handler in another assembly is registered explicitly: `services.AddScoped<IDomainEventHandler<TaskCompletedEvent>, MyHandler>()`.

### The 44 domain events

| Family | Events | Raised by |
|--------|--------|-----------|
| Agent (10) | `AgentCreatedEvent`, `AgentAssignedToTaskEvent`, `AgentStartedTaskEvent`, `AgentCompletedTaskEvent`, `AgentFailedTaskEvent`, `AgentCapabilitiesUpdatedEvent`, `AgentCollaborationStartedEvent`, `AgentMemoryUpdatedEvent`, `AgentKilledEvent` | `Agent` aggregate |
| | `AgentSpawnedEvent` | declared, not raised yet |
| Crew (11) | `CrewCreatedEvent`, `AgentJoinedCrewEvent`, `AgentLeftCrewEvent`, `TaskAddedToCrewEvent`, `TaskRemovedFromCrewEvent`, `CrewExecutionStartedEvent`, `CrewExecutionCompletedEvent`, `CrewExecutionFailedEvent`, `CrewProcessTypeChangedEvent`, `CrewGoalUpdatedEvent` | `Crew` aggregate |
| | `CrewCompletedEvent` | declared (with a `FromOutput` factory), not raised yet |
| Task (11) | `TaskCreatedEvent`, `TaskAssignedEvent`, `TaskStatusChangedEvent`, `TaskStartedEvent`, `TaskCompletedEvent`, `TaskFailedEvent`, `TaskCancelledEvent`, `TaskDependenciesUpdatedEvent` | `CrewTaskBase<TContext>` aggregate (`CrewTask`) |
| | `TaskContextUpdatedEvent` | collected by `TypedTaskContext<T>` in its own list (`GetEvents()` / `ClearEvents()`), outside the dispatch path |
| | `TaskBlockedEvent`, `TaskUnblockedEvent` | declared, not raised yet |
| Memory (6) | `MemoryStoreCreatedEvent`, `MemoryAddedEvent`, `MemoryPromotedEvent`, `EntityMemoryUpdatedEvent`, `EpisodicMemoryAddedEvent`, `MemoryClearedEvent` | `AgentMemoryStore` aggregate |
| Delegation (5) | `TaskDelegatedEvent`, `DelegationCompletedEvent`, `DelegationQueuedEvent`, `AgentRegisteredForDelegationEvent`, `AgentUnregisteredFromDelegationEvent` | declared, not raised yet |
| Human input (1) | `HumanInputRequestedEvent` | declared, not raised yet |

Total: 44 domain events — 34 raised by the domain model, 10 declared for the lifecycle they describe but raised by no code path yet. The event records live next to their aggregate (`Agent/Events/`, `Crew/Events/`, `Task/Events/`, `Memory/Events/`, `Delegation/Events/`, `HumanInput/Events/`).

## CQRS and pipeline

The Application layer implements CQRS with `ICommand<TResponse>` / `ICommandHandler<TCommand, TResponse>` and `IQuery<TResponse>` / `IQueryHandler<TQuery, TResponse>` (`Orkeon.Application.Common.CQRS`), plus `ICommandValidator<TCommand>` (`Orkeon.Application.Validation`). `AddOrkeonApplication()` scans the Application assembly and registers them; there is no mediator — resolve the handler interface from DI and call `HandleAsync`.

Each command handler is registered behind two decorators:

```
ValidatingCommandHandler   (only when a validator exists — throws CommandValidationException)
  └── UnitOfWorkCommandHandler   (IUnitOfWork.SaveChangesAsync → domain events dispatched)
        └── the concrete handler
```

Query handlers are registered as is (no unit of work).

| Kind | Types | Validator |
|------|-------|-----------|
| Commands | `CreateAgentCommand`, `CreateCrewCommand`, `CreateTaskCommand` | ✅ (`CreateAgentCommandValidator`, `CreateCrewCommandValidator`, `CreateTaskCommandValidator`) |
| | `AddMemoryCommand`, `CreateMemoryStoreCommand` | — |
| Queries | `GetAgentQuery`, `GetCrewQuery`, `GetTaskQuery`, `SearchMemoryQuery` | — |

## Callbacks and observability

Three observation surfaces exist, at different levels:

**`ICrewExecutionHook`** (`Orkeon.Application.Crew`) — the run-level hook every process strategy calls, on every exit (success, failure, cancellation): `OnTaskStartedAsync`, `OnTaskCompletedAsync`, `OnCrewCompletedAsync`, `OnCrewFailedAsync`, with task and crew snapshots (status, duration, tokens, cache split, skipped tasks). It is a single DI service; an exception it throws is logged and swallowed. Shipped implementations: `AutoSummaryWriter` (`AUTO_SUMMARY.md`), the `orkeon run --events` observer (see [the run event bus](./run-event-bus.md)) and the `orkeon-host` progress hook.

**`ICallbackHandler`** (`Orkeon.Application.Callback`) — task- and step-level notifications: `OnStepStartedAsync`, `OnStepCompletedAsync`, `OnTaskStartedAsync`, `OnTaskProgressAsync`, `OnTaskCompletedAsync`, `OnFlowStepStartedAsync`, `OnFlowStepCompletedAsync`. `CallbackOrchestrator` (`Orkeon.Application.Execution`, the scoped `ICallbackOrchestrator`) fans each notification out to every `ICallbackHandler` registered in DI plus the per-call `CallbackHandlers`; `AgentExecutionService` calls `NotifyTaskStartedAsync` and `NotifyTaskCompletedAsync` around every task. The interface also has `NotifyStepProgressAsync`, and the class `NotifyToolUsedAsync` / `NotifyDelegationAsync`. `BaseCallbackHandler` is the base to derive from; `LoggingCallbackHandler` logs every notification but is not registered by default.

**`IStepCallback`** (`Orkeon.Domain.Agent`: `OnStepStartAsync`, `OnStepCompletedAsync`, `OnStepFailedAsync`) and **`ITaskCallback`** (`Orkeon.Domain.Task`: `OnTaskStartAsync`, `OnTaskCompletedAsync`, `OnTaskFailedAsync`) — domain-level callbacks carried by the aggregates (`AgentBuilder.WithStepCallback`, `Crew.StepCallback` / `Crew.TaskCallback`; DI defaults `NullStepCallback` / `NullTaskCallback`). The execution engine does not invoke them today: prefer `ICrewExecutionHook` or `ICallbackHandler`.

---

> **See also**: [ProcessTypes](../orchestration/process-types.md) · [Security](./security.md) · [Back to index](../INDEX.md)
