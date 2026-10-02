> 🇫🇷 [Version française](../fr/architecture/domain-events.md)

# Events, CQRS and Observability

## Domain Events

`DomainEvent` (`Orkeon.Domain.SharedKernel.Events`) is the abstract base record of every domain event: `Id` (`DomainEventId`), `OccurredAt` (UTC), `EventName` (the type name) and `Version` (virtual, 1 by default, for serialization compatibility).

Aggregates (`AggregateRoot<TEntityId>`, which implements `IHasDomainEvents`) queue events with the protected `RaiseDomainEvent()`; `DomainEvents` exposes the pending ones and `ClearDomainEvents()` empties the list.

### Dispatch

Dispatch goes through `IDomainEventDispatcher` (Domain), implemented by `DomainEventDispatcher` (`Orkeon.Infrastructure.DomainEvents`, scoped, registered by `AddOrkeonInfrastructure()`): for each event it resolves every `IDomainEventHandler<TEvent>` registered in DI, from the caller's scope, and awaits its `HandleAsync`, in order (`DispatchAsync`, `DispatchManyAsync`); an event without a handler is logged at Debug level.

Events are dispatched at three points.

**As a run goes** (GAP-21). Every orchestration mode moves the tasks it runs and their agents through their lifecycle. The rule lives in one place, the outcome the six modes share (`CrewRunOutcome`, which drives `TaskLifecycle`): it applies one transition of the task and of the agents concerned, saves them in their repositories — the run's scoped ones —, then dispatches their queued events, the task's first. A handler therefore hears of a task while the run goes, before the crew's own events. For a task that runs:

1. `TaskAssignedEvent` when the run gives it to another agent than the one it carries — a task that names no agent, the agent a hierarchical or autonomous manager picks —, and `AgentAssignedToTaskEvent` the first time an agent is given it;
2. `TaskStatusChangedEvent` (`Pending` → `InProgress`) and `TaskStartedEvent`, then `AgentStartedTaskEvent`;
3. when it ends, `TaskStatusChangedEvent` and `TaskCompletedEvent` (its output and the agent that produced it), then `AgentCompletedTaskEvent` — or `TaskFailedEvent` and `AgentFailedTaskEvent`, with the cause.

A task's construction events (`TaskCreatedEvent`, the `TaskAssignedEvent` of a declared `agent:`, `TaskDependenciesUpdatedEvent`) are still queued when its run first moves it, and go out then, like an agent's (`AgentCreatedEvent`, `AgentCapabilitiesUpdatedEvent`). What each mode adds:

| Mode | Lifecycle |
|------|-----------|
| Sequential | One start, one end per task, in the run's order. |
| Parallel | Each task of a wave starts as it is launched; the wave's tasks end once it has joined, in declaration order. An agent given two tasks of a wave runs both at once (`Agent.CurrentTasks`). |
| Graph | A task starts once and keeps running through its retries, under the agent it was given; it ends when it succeeds or gives up. |
| Hierarchical | The agent the manager assigns starts the task — re-assigned to it when the crew declared another. Its revisions are part of its work: it ends the task once, completed when the manager accepted an output, failed otherwise. A task the manager gives to an agent the crew does not carry fails without starting. |
| Consensual | The task starts once, under no single agent (`TaskStartedEvent.AgentId` is null). Every agent that answers starts it too and ends it with its own last answer (`AgentCompletedTaskEvent`) or its error (`AgentFailedTaskEvent`); the task is then assigned to the author of the retained answer and completes under it. |
| Autonomous | The agent that claims a task starts it. When it fails and the task goes to a peer, it fails the task (`AgentFailedTaskEvent`, with its error), the task is assigned to the peer (`TaskAssignedEvent`), who starts it and ends it: the task started once, and ends under the peer. |

A task that does not run is cancelled with its reason (`TaskCancelledEvent`): skipped because a task it depends on did not succeed (`skipped: it depends on task …, which did not succeed`) — never started, never ended, its `StartedAt` and `CompletedAt` empty —, or never reached by an Autonomous run whose budget ran out (`not executed: …`). A task an interruption catches running is cancelled when the run was cancelled (`the run was cancelled`) and failed when it stopped on an exception or a graph circuit break (`the run stopped: …`); the agents running it fail it with the same reason.

After the run, the repository holds what happened: a task is `Completed` with its output, `StartedAt` and `CompletedAt`, `Failed` with `StartedAt` and `CompletedAt` (when it ended), or `Cancelled`. A crew kicked off again without being reloaded — the C# `KickoffAsync` loop, the fixed-crew `CrewAgent` — reopens each task the first time the new run moves it (`CrewTask.Reopen`: back to `Pending` with a `TaskStatusChangedEvent`, its output and dates cleared, its assignment kept).

Bookkeeping never changes a run: a transition the aggregate refuses is logged as a warning and the run goes on; a handler that throws is logged and the events after it still go out — the same rule as the crew's. Saving and dispatching ignore the run's token, so a cancelled run still says what became of its tasks. A strategy built by hand without an `IDomainEventDispatcher` (the optional `domainEvents` dependency, which the container always fills) moves and saves its tasks and leaves their events queued.

**At the end of a crew kickoff.** `SequentialCrewOrchestrator` (the `ICrewOrchestrationService`) dispatches the crew's queued events once the run ends — on success, failure and cancellation alike — and then empties the aggregate, so a second kickoff never dispatches them again. `KickoffForEachAsync`, `KickoffAsyncNoWait` and `KickoffStreamingAsync` go through the same point. A kickoff therefore delivers, in order, the events queued while the crew was built (`CrewCreatedEvent`, `AgentJoinedCrewEvent`, `TaskAddedToCrewEvent`, on the first run), then `CrewExecutionStartedEvent`, then `CrewExecutionCompletedEvent` or `CrewExecutionFailedEvent`. A handler that throws is logged and skipped: it changes neither the run's `CrewOutput` nor the error it reports, and the events after it are still delivered. The orchestrator calls the dispatcher directly rather than a unit of work, whose single tracked aggregate would lose the crew.

**When a unit of work is saved** (the CQRS commands):

1. The in-memory repositories call `IUnitOfWork.Track(aggregate)` on every add/update. `InMemoryUnitOfWork` keeps **one aggregate per scope** (the last tracked one — one command, one aggregate).
2. `IUnitOfWork.SaveChangesAsync()` runs the persist step (empty for the in-memory adapter), dispatches the tracked aggregate's pending events, then clears them — even when a handler throws.
3. `SaveChangesAsync()` is called by `UnitOfWorkCommandHandler`, the decorator wrapped around **every CQRS command handler** (see below).

### Handlers

`AddOrkeonApplication()` scans the `Orkeon.Application` assembly and registers every `IDomainEventHandler<T>` it finds (scoped). Four ship, all of them structured-logging handlers: `AgentCompletedTaskHandler` and `AgentFailedTaskHandler` (Information and Warning, as a run goes), `CrewExecutionCompletedHandler`, `CrewExecutionFailedHandler`. A handler in another assembly is registered explicitly: `services.AddScoped<IDomainEventHandler<TaskCompletedEvent>, MyHandler>()`.

### The 31 domain events

| Family | Events | Raised by |
|--------|--------|-----------|
| Agent (7) | `AgentCreatedEvent`, `AgentAssignedToTaskEvent`, `AgentStartedTaskEvent`, `AgentCompletedTaskEvent`, `AgentFailedTaskEvent`, `AgentCapabilitiesUpdatedEvent`, `AgentKilledEvent` | `Agent` aggregate |
| Crew (10) | `CrewCreatedEvent`, `AgentJoinedCrewEvent`, `AgentLeftCrewEvent`, `TaskAddedToCrewEvent`, `TaskRemovedFromCrewEvent`, `CrewExecutionStartedEvent`, `CrewExecutionCompletedEvent`, `CrewExecutionFailedEvent`, `CrewProcessTypeChangedEvent`, `CrewGoalUpdatedEvent` | `Crew` aggregate |
| Task (8) | `TaskCreatedEvent`, `TaskAssignedEvent`, `TaskStatusChangedEvent`, `TaskStartedEvent`, `TaskCompletedEvent`, `TaskFailedEvent`, `TaskCancelledEvent`, `TaskDependenciesUpdatedEvent` | `CrewTaskBase<TContext>` aggregate (`CrewTask`) |
| Memory (6) | `MemoryStoreCreatedEvent`, `MemoryAddedEvent`, `MemoryPromotedEvent`, `EntityMemoryUpdatedEvent`, `EpisodicMemoryAddedEvent`, `MemoryClearedEvent` | `AgentMemoryStore` aggregate |

Total: 31 domain events, each raised by a method of its aggregate. The event records live next to their aggregate (`Agent/Events/`, `Crew/Events/`, `Task/Events/`, `Memory/Events/`).

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

**`ICallbackHandler`** (`Orkeon.Application.Callback`) — task- and step-level notifications: `OnTaskStartedAsync` / `OnTaskCompletedAsync` around each agent execution (`AgentExecutionService`) — the completion's `StepsExecuted` is the number of turns the agent loop ran, each a model call with the tool calls it asked for, 0 when the task ended before its first —, and `OnStepStartedAsync` / `OnStepCompletedAsync` around **each tool call** of the agent loops — the chat-client, native, text and streaming loops alike. A step's `Action` is `tool:<name>`; its completion carries the tool's raw result as `Observation` and `Success = false` when the tool failed, threw or was blocked by the guardian. `CallbackOrchestrator` (`Orkeon.Application.Execution`, the scoped `ICallbackOrchestrator`) fans each notification out to every `ICallbackHandler` registered in DI; a handler that throws is logged and skipped. The step notifications come from `StepNotifyingToolInvocationPipeline`, which wraps the tool-invocation point of the `ExecutionOrchestrator` (its `Callbacks` property, set by `AddOrkeonApplication()`) and of `StreamingAgentExecutionService`. `BaseCallbackHandler` is the base to derive from; `CompositeCallbackHandler` fans out to several; `LoggingCallbackHandler` logs every notification but is not registered by default.

---

> **See also**: [ProcessTypes](../orchestration/process-types.md) · [Security](./security.md) · [Back to index](../INDEX.md)
