> 🇬🇧 [English version](../../architecture/domain-events.md)

# Événements, CQRS et observabilité

## Événements de domaine

`DomainEvent` (`Orkeon.Domain.SharedKernel.Events`) est le record de base abstrait de tout événement de domaine : `Id` (`DomainEventId`), `OccurredAt` (UTC), `EventName` (le nom du type) et `Version` (virtuelle, 1 par défaut, pour la compatibilité de sérialisation).

Les agrégats (`AggregateRoot<TEntityId>`, qui implémente `IHasDomainEvents`) mettent les événements en file avec la méthode protégée `RaiseDomainEvent()` ; `DomainEvents` expose ceux en attente et `ClearDomainEvents()` vide la liste.

### Dispatch

Le dispatch passe par `IDomainEventDispatcher` (Domain), implémenté par `DomainEventDispatcher` (`Orkeon.Infrastructure.DomainEvents`, scoped, enregistré par `AddOrkeonInfrastructure()`) : pour chaque événement, il résout chaque `IDomainEventHandler<TEvent>` enregistré dans la DI, depuis le scope de l'appelant, et attend son `HandleAsync`, dans l'ordre (`DispatchAsync`, `DispatchManyAsync`) ; un événement sans handler est journalisé au niveau Debug.

Les événements sont dispatchés à deux moments.

**À la fin d'un kickoff de crew.** `SequentialCrewOrchestrator` (l'`ICrewOrchestrationService`) dispatche les événements en file de la crew une fois le run terminé — en cas de succès, d'échec comme d'annulation — puis vide l'agrégat : un second kickoff ne les redispatche jamais. `KickoffForEachAsync`, `KickoffAsyncNoWait` et `KickoffStreamingAsync` passent par le même point. Un kickoff livre donc, dans l'ordre, les événements mis en file pendant la construction de la crew (`CrewCreatedEvent`, `AgentJoinedCrewEvent`, `TaskAddedToCrewEvent`, au premier run), puis `CrewExecutionStartedEvent`, puis `CrewExecutionCompletedEvent` ou `CrewExecutionFailedEvent`. Un handler qui lève une exception est journalisé et ignoré : il ne change ni le `CrewOutput` du run ni l'erreur qu'il rapporte, et les événements suivants sont quand même livrés. L'orchestrateur appelle le dispatcher directement plutôt qu'une unité de travail, dont l'agrégat suivi unique perdrait la crew.

**Quand une unité de travail est sauvegardée** (les commandes CQRS) :

1. Les repositories en mémoire appellent `IUnitOfWork.Track(aggregate)` à chaque ajout/mise à jour. `InMemoryUnitOfWork` garde **un agrégat par scope** (le dernier suivi — une commande, un agrégat).
2. `IUnitOfWork.SaveChangesAsync()` exécute l'étape de persistance (vide pour l'adaptateur en mémoire), dispatche les événements en attente de l'agrégat suivi, puis les efface — même quand un handler lève une exception.
3. `SaveChangesAsync()` est appelé par `UnitOfWorkCommandHandler`, le décorateur placé autour de **chaque handler de commande CQRS** (voir plus bas).

Pendant un run, seul l'agrégat `Crew` change d'état : aucune stratégie n'appelle les méthodes de cycle de vie de tâche et d'agent (`CrewTask.Start/Complete/Fail`, `Agent.StartTask/CompleteTask/FailTask`), donc `TaskStartedEvent`, `AgentCompletedTaskEvent` et leurs voisins ne sont pas levés par un kickoff, et les handlers livrés `AgentCompletedTaskHandler` / `AgentFailedTaskHandler` restent muets pendant un run. Pour suivre les tâches, utilisez `ICrewExecutionHook` ou `ICallbackHandler` ([Callbacks et observabilité](#callbacks-et-observabilité)).

### Handlers

`AddOrkeonApplication()` parcourt l'assembly `Orkeon.Application` et enregistre chaque `IDomainEventHandler<T>` qu'il y trouve (scoped). Quatre sont livrés, tous des handlers de journalisation structurée : `AgentCompletedTaskHandler`, `AgentFailedTaskHandler`, `CrewExecutionCompletedHandler`, `CrewExecutionFailedHandler`. Un handler situé dans une autre assembly s'enregistre explicitement : `services.AddScoped<IDomainEventHandler<TaskCompletedEvent>, MyHandler>()`.

### Les 33 événements de domaine

| Famille | Événements | Levés par |
|--------|--------|-----------|
| Agent (9) | `AgentCreatedEvent`, `AgentAssignedToTaskEvent`, `AgentStartedTaskEvent`, `AgentCompletedTaskEvent`, `AgentFailedTaskEvent`, `AgentCapabilitiesUpdatedEvent`, `AgentCollaborationStartedEvent`, `AgentMemoryUpdatedEvent`, `AgentKilledEvent` | agrégat `Agent` |
| Crew (10) | `CrewCreatedEvent`, `AgentJoinedCrewEvent`, `AgentLeftCrewEvent`, `TaskAddedToCrewEvent`, `TaskRemovedFromCrewEvent`, `CrewExecutionStartedEvent`, `CrewExecutionCompletedEvent`, `CrewExecutionFailedEvent`, `CrewProcessTypeChangedEvent`, `CrewGoalUpdatedEvent` | agrégat `Crew` |
| Task (8) | `TaskCreatedEvent`, `TaskAssignedEvent`, `TaskStatusChangedEvent`, `TaskStartedEvent`, `TaskCompletedEvent`, `TaskFailedEvent`, `TaskCancelledEvent`, `TaskDependenciesUpdatedEvent` | agrégat `CrewTaskBase<TContext>` (`CrewTask`) |
| Memory (6) | `MemoryStoreCreatedEvent`, `MemoryAddedEvent`, `MemoryPromotedEvent`, `EntityMemoryUpdatedEvent`, `EpisodicMemoryAddedEvent`, `MemoryClearedEvent` | agrégat `AgentMemoryStore` |

Total : 33 événements de domaine, chacun levé par une méthode de son agrégat. Les records d'événements vivent à côté de leur agrégat (`Agent/Events/`, `Crew/Events/`, `Task/Events/`, `Memory/Events/`).

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

Deux surfaces d'observation existent, à des niveaux différents :

**`ICrewExecutionHook`** (`Orkeon.Application.Crew`) — le hook de niveau run que chaque stratégie de process appelle, sur toutes les sorties (succès, échec, annulation) : `OnTaskStartedAsync`, `OnTaskCompletedAsync`, `OnCrewCompletedAsync`, `OnCrewFailedAsync`, avec des instantanés de tâche et de crew (statut, durée, tokens, ventilation du cache, tâches sautées). C'est un service DI unique ; une exception qu'il lève est journalisée et absorbée. Implémentations livrées : `AutoSummaryWriter` (`AUTO_SUMMARY.md`), l'observateur de `orkeon run --events` (voir [le bus d'événements du run](./run-event-bus.md)) et le hook de progression d'`orkeon-host`.

**`ICallbackHandler`** (`Orkeon.Application.Callback`) — notifications au niveau tâche et étape : `OnTaskStartedAsync` / `OnTaskCompletedAsync` autour de chaque exécution d'agent (`AgentExecutionService`), et `OnStepStartedAsync` / `OnStepCompletedAsync` autour de **chaque appel d'outil** des boucles d'agent — boucle chat-client, native, texte et streaming. L'`Action` d'une étape vaut `tool:<nom>` ; sa fin porte le résultat brut de l'outil dans `Observation`, et `Success = false` quand l'outil a échoué, levé une exception ou été bloqué par le guardian. `CallbackOrchestrator` (`Orkeon.Application.Execution`, l'`ICallbackOrchestrator` scoped) diffuse chaque notification à chaque `ICallbackHandler` enregistré dans la DI ; un handler qui lève une exception est journalisé et ignoré. Les notifications d'étape viennent de `StepNotifyingToolInvocationPipeline`, qui enveloppe le point d'invocation d'outils de l'`ExecutionOrchestrator` (sa propriété `Callbacks`, posée par `AddOrkeonApplication()`) et de `StreamingAgentExecutionService`. `BaseCallbackHandler` est la base dont dériver ; `CompositeCallbackHandler` diffuse à plusieurs handlers ; `LoggingCallbackHandler` journalise chaque notification mais n'est pas enregistré par défaut.

---

> **Voir aussi** : [ProcessTypes](../orchestration/process-types.md) · [Sécurité](./security.md) · [Retour à l'index](../INDEX.md)
