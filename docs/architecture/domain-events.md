> 🇫🇷 [Version française](../fr/architecture/domain-events.md)

# Events, CQRS and Observability

## Domain Events

`DomainEvent` (`Orkeon.Domain.SharedKernel.Events`) is the abstract base record of every domain event: `Id` (`DomainEventId`), `OccurredAt` (UTC), `EventName` (the type name) and `Version` (virtual, 1 by default, for serialization compatibility).

Aggregates (`AggregateRoot<TEntityId>`, which implements `IHasDomainEvents`) queue events with the protected `RaiseDomainEvent()`; `DomainEvents` exposes the pending ones and `ClearDomainEvents()` empties the list.

### Dispatch

Dispatch goes through `IDomainEventDispatcher` (Domain), implemented by `DomainEventDispatcher` (`Orkeon.Infrastructure.DomainEvents`, scoped, registered by `AddOrkeonInfrastructure()`): for each event it resolves every `IDomainEventHandler<TEvent>` registered in DI, from the caller's scope, and awaits its `HandleAsync`, in order (`DispatchAsync`, `DispatchManyAsync`); an event without a handler is logged at Debug level.

Events are dispatched at two points.

**At the end of a crew kickoff.** `SequentialCrewOrchestrator` (the `ICrewOrchestrationService`) dispatches the crew's queued events once the run ends — on success, failure and cancellation alike — and then empties the aggregate, so a second kickoff never dispatches them again. `KickoffForEachAsync`, `KickoffAsyncNoWait` and `KickoffStreamingAsync` go through the same point. A kickoff therefore delivers, in order, the events queued while the crew was built (`CrewCreatedEvent`, `AgentJoinedCrewEvent`, `TaskAddedToCrewEvent`, on the first run), then `CrewExecutionStartedEvent`, then `CrewExecutionCompletedEvent` or `CrewExecutionFailedEvent`. A handler that throws is logged and skipped: it changes neither the run's `CrewOutput` nor the error it reports, and the events after it are still delivered. The orchestrator calls the dispatcher directly rather than a unit of work, whose single tracked aggregate would lose the crew.

**When a unit of work is saved** (the CQRS commands):

1. The in-memory repositories call `IUnitOfWork.Track(aggregate)` on every add/update. `InMemoryUnitOfWork` keeps **one aggregate per scope** (the last tracked one — one command, one aggregate).
2. `IUnitOfWork.SaveChangesAsync()` runs the persist step (empty for the in-memory adapter), dispatches the tracked aggregate's pending events, then clears them — even when a handler throws.
3. `SaveChangesAsync()` is called by `UnitOfWorkCommandHandler`, the decorator wrapped around **every CQRS command handler** (see below).

During a run only the `Crew` aggregate changes state: no strategy calls the task and agent lifecycle methods (`CrewTask.Start/Complete/Fail`, `Agent.StartTask/CompleteTask/FailTask`), so `TaskStartedEvent`, `AgentCompletedTaskEvent` and their siblings are not raised by a kickoff, and the shipped `AgentCompletedTaskHandler` / `AgentFailedTaskHandler` stay quiet during one. To follow tasks, use `ICrewExecutionHook` or `ICallbackHandler` ([Callbacks and observability](#callbacks-and-observability)).

### Handlers

`AddOrkeonApplication()` scans the `Orkeon.Application` assembly and registers every `IDomainEventHandler<T>` it finds (scoped). Four ship, all of them structured-logging handlers: `AgentCompletedTaskHandler`, `AgentFailedTaskHandler`, `CrewExecutionCompletedHandler`, `CrewExecutionFailedHandler`. A handler in another assembly is registered explicitly: `services.AddScoped<IDomainEventHandler<TaskCompletedEvent>, MyHandler>()`.

### The 33 domain events

| Family | Events | Raised by |
|--------|--------|-----------|
| Agent (9) | `AgentCreatedEvent`, `AgentAssignedToTaskEvent`, `AgentStartedTaskEvent`, `AgentCompletedTaskEvent`, `AgentFailedTaskEvent`, `AgentCapabilitiesUpdatedEvent`, `AgentCollaborationStartedEvent`, `AgentMemoryUpdatedEvent`, `AgentKilledEvent` | `Agent` aggregate |
| Crew (10) | `CrewCreatedEvent`, `AgentJoinedCrewEvent`, `AgentLeftCrewEvent`, `TaskAddedToCrewEvent`, `TaskRemovedFromCrewEvent`, `CrewExecutionStartedEvent`, `CrewExecutionCompletedEvent`, `CrewExecutionFailedEvent`, `CrewProcessTypeChangedEvent`, `CrewGoalUpdatedEvent` | `Crew` aggregate |
| Task (8) | `TaskCreatedEvent`, `TaskAssignedEvent`, `TaskStatusChangedEvent`, `TaskStartedEvent`, `TaskCompletedEvent`, `TaskFailedEvent`, `TaskCancelledEvent`, `TaskDependenciesUpdatedEvent` | `CrewTaskBase<TContext>` aggregate (`CrewTask`) |
| Memory (6) | `MemoryStoreCreatedEvent`, `MemoryAddedEvent`, `MemoryPromotedEvent`, `EntityMemoryUpdatedEvent`, `EpisodicMemoryAddedEvent`, `MemoryClearedEvent` | `AgentMemoryStore` aggregate |

Total: 33 domain events, each raised by a method of its aggregate. The event records live next to their aggregate (`Agent/Events/`, `Crew/Events/`, `Task/Events/`, `Memory/Events/`).

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

Two observation surfaces exist, at different levels:

**`ICrewExecutionHook`** (`Orkeon.Application.Crew`) — the run-level hook every process strategy calls, on every exit (success, failure, cancellation): `OnTaskStartedAsync`, `OnTaskCompletedAsync`, `OnCrewCompletedAsync`, `OnCrewFailedAsync`, with task and crew snapshots (status, duration, tokens, cache split, skipped tasks). It is a single DI service; an exception it throws is logged and swallowed. Shipped implementations: `AutoSummaryWriter` (`AUTO_SUMMARY.md`), the `orkeon run --events` observer (see [the run event bus](./run-event-bus.md)) and the `orkeon-host` progress hook.

**`ICallbackHandler`** (`Orkeon.Application.Callback`) — task- and step-level notifications: `OnTaskStartedAsync` / `OnTaskCompletedAsync` around each agent execution (`AgentExecutionService`), and `OnStepStartedAsync` / `OnStepCompletedAsync` around **each tool call** of the agent loops — the chat-client, native, text and streaming loops alike. A step's `Action` is `tool:<name>`; its completion carries the tool's raw result as `Observation` and `Success = false` when the tool failed, threw or was blocked by the guardian. `CallbackOrchestrator` (`Orkeon.Application.Execution`, the scoped `ICallbackOrchestrator`) fans each notification out to every `ICallbackHandler` registered in DI; a handler that throws is logged and skipped. The step notifications come from `StepNotifyingToolInvocationPipeline`, which wraps the tool-invocation point of the `ExecutionOrchestrator` (its `Callbacks` property, set by `AddOrkeonApplication()`) and of `StreamingAgentExecutionService`. `BaseCallbackHandler` is the base to derive from; `CompositeCallbackHandler` fans out to several; `LoggingCallbackHandler` logs every notification but is not registered by default.

---

> **See also**: [ProcessTypes](../orchestration/process-types.md) · [Security](./security.md) · [Back to index](../INDEX.md)
