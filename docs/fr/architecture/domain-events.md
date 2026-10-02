> 🇬🇧 [English version](../../architecture/domain-events.md)

# Événements, CQRS et observabilité

## Événements de domaine

`DomainEvent` (`Orkeon.Domain.SharedKernel.Events`) est le record de base abstrait de tout événement de domaine : `Id` (`DomainEventId`), `OccurredAt` (UTC), `EventName` (le nom du type) et `Version` (virtuelle, 1 par défaut, pour la compatibilité de sérialisation).

Les agrégats (`AggregateRoot<TEntityId>`, qui implémente `IHasDomainEvents`) mettent les événements en file avec la méthode protégée `RaiseDomainEvent()` ; `DomainEvents` expose ceux en attente et `ClearDomainEvents()` vide la liste.

### Dispatch

Le dispatch passe par `IDomainEventDispatcher` (Domain), implémenté par `DomainEventDispatcher` (`Orkeon.Infrastructure.DomainEvents`, scoped, enregistré par `AddOrkeonInfrastructure()`) : pour chaque événement, il résout chaque `IDomainEventHandler<TEvent>` enregistré dans la DI, depuis le scope de l'appelant, et attend son `HandleAsync`, dans l'ordre (`DispatchAsync`, `DispatchManyAsync`) ; un événement sans handler est journalisé au niveau Debug.

Les événements sont dispatchés à trois moments.

**Au fil du run** (GAP-21). Chaque mode d'orchestration fait passer les tâches qu'il exécute et leurs agents par leur cycle de vie. La règle vit à un seul endroit, l'issue que partagent les six modes (`CrewRunOutcome`, qui pilote `TaskLifecycle`) : elle applique une transition de la tâche et des agents concernés, les sauvegarde dans leurs repositories — ceux, scoped, du run —, puis dispatche leurs événements en file, ceux de la tâche d'abord. Un handler entend donc parler d'une tâche pendant le run, avant les événements de la crew elle-même. Pour une tâche qui s'exécute :

1. `TaskAssignedEvent` quand le run la confie à un autre agent que celui qu'elle porte — une tâche qui ne nomme aucun agent, l'agent que choisit un manager hiérarchique ou autonome —, et `AgentAssignedToTaskEvent` la première fois qu'un agent la reçoit ;
2. `TaskStatusChangedEvent` (`Pending` → `InProgress`) et `TaskStartedEvent`, puis `AgentStartedTaskEvent` ;
3. à sa fin, `TaskStatusChangedEvent` et `TaskCompletedEvent` (sa sortie et l'agent qui l'a produite), puis `AgentCompletedTaskEvent` — ou `TaskFailedEvent` et `AgentFailedTaskEvent`, avec la cause.

Les événements de construction d'une tâche (`TaskCreatedEvent`, le `TaskAssignedEvent` d'un `agent:` déclaré, `TaskDependenciesUpdatedEvent`) sont encore en file quand son run la fait passer pour la première fois, et partent alors, comme ceux d'un agent (`AgentCreatedEvent`, `AgentCapabilitiesUpdatedEvent`). Ce que chaque mode ajoute :

| Mode | Cycle de vie |
|------|--------------|
| Sequential | Un début, une fin par tâche, dans l'ordre du run. |
| Parallel | Chaque tâche d'une vague démarre au moment où elle est lancée ; les tâches de la vague se terminent une fois la vague rejointe, dans l'ordre de déclaration. Un agent qui reçoit deux tâches d'une vague exécute les deux à la fois (`Agent.CurrentTasks`). |
| Graph | Une tâche démarre une fois et continue de tourner pendant ses reprises, sous l'agent qui l'a reçue ; elle se termine quand elle réussit ou abandonne. |
| Hierarchical | L'agent que le manager désigne démarre la tâche — réassignée à lui quand la crew en déclarait un autre. Ses révisions font partie de son travail : il termine la tâche une fois, réussie quand le manager a accepté une sortie, échouée sinon. Une tâche que le manager confie à un agent absent de la crew échoue sans démarrer. |
| Consensual | La tâche démarre une fois, sous aucun agent en particulier (`TaskStartedEvent.AgentId` est nul). Chaque agent qui répond la démarre aussi et la termine avec sa propre dernière réponse (`AgentCompletedTaskEvent`) ou son erreur (`AgentFailedTaskEvent`) ; la tâche est alors assignée à l'auteur de la réponse retenue et se termine sous lui. |
| Autonomous | L'agent qui réclame une tâche la démarre. Quand il échoue et que la tâche passe à un pair, il fait échouer la tâche (`AgentFailedTaskEvent`, avec son erreur), la tâche est assignée au pair (`TaskAssignedEvent`), qui la démarre et la termine : la tâche a démarré une fois, et se termine sous le pair. |

Une tâche qui ne s'exécute pas est annulée avec sa raison (`TaskCancelledEvent`) : sautée parce qu'une tâche dont elle dépend n'a pas réussi (`skipped: it depends on task …, which did not succeed`) — jamais démarrée, jamais terminée, son `StartedAt` et son `CompletedAt` vides —, ou jamais atteinte par un run autonome dont le budget s'est épuisé (`not executed: …`). Une tâche qu'une interruption surprend en cours est annulée quand le run a été annulé (`the run was cancelled`) et échoue quand il s'est arrêté sur une exception ou une coupure du disjoncteur du graphe (`the run stopped: …`) ; les agents qui l'exécutaient la font échouer avec la même raison.

Après le run, le repository dit ce qui s'est passé : une tâche est `Completed` avec sa sortie, `StartedAt` et `CompletedAt`, `Failed` avec `StartedAt` et `CompletedAt` (le moment de sa fin), ou `Cancelled`. Une crew relancée sans être rechargée — la boucle C# sur `KickoffAsync`, le `CrewAgent` à crew fixe — rouvre chaque tâche la première fois que le nouveau run la fait passer (`CrewTask.Reopen` : retour à `Pending` avec un `TaskStatusChangedEvent`, sa sortie et ses dates effacées, son affectation gardée).

La tenue des comptes ne change jamais un run : une transition que l'agrégat refuse est journalisée en avertissement et le run continue ; un handler qui lève une exception est journalisé et les événements suivants partent quand même — la même règle que pour la crew. La sauvegarde et le dispatch ignorent le jeton du run : un run annulé dit quand même ce que sont devenues ses tâches. Une stratégie construite à la main sans `IDomainEventDispatcher` (la dépendance optionnelle `domainEvents`, que le conteneur remplit toujours) fait passer et sauvegarde ses tâches, et laisse leurs événements en file.

**À la fin d'un kickoff de crew.** `SequentialCrewOrchestrator` (l'`ICrewOrchestrationService`) dispatche les événements en file de la crew une fois le run terminé — en cas de succès, d'échec comme d'annulation — puis vide l'agrégat : un second kickoff ne les redispatche jamais. `KickoffForEachAsync` (un run par entrée, en séquence), `KickoffAsyncNoWait` et `KickoffStreamingAsync` passent par le même point — un kickoff diffusé est le même run. Un kickoff livre donc, dans l'ordre, les événements mis en file pendant la construction de la crew (`CrewCreatedEvent`, `AgentJoinedCrewEvent`, `TaskAddedToCrewEvent`, au premier run), puis `CrewExecutionStartedEvent`, puis `CrewExecutionCompletedEvent` ou `CrewExecutionFailedEvent` — une seule transition terminale par run. Un run dont toutes les tâches ont réussi termine la crew (`CrewExecutionCompletedEvent.CompletedTasks` compte chaque tâche une fois, par son issue finale : une tâche qu'un graphe a reprise avant qu'elle réussisse est une tâche terminée). Un run dont une tâche a échoué ou a été sautée la fait échouer, comme le `CrewOutput` et le code de sortie : `CrewExecutionFailedEvent`, avec l'erreur de la crew pour `Reason` (chaque tâche en échec ou sautée nommée) et sans `Exception`, la crew et son exécution laissées `Failed`. Un run arrêté par une exception échoue avec elle pour `Exception` ; un run annulé est un run en échec dont l'`Exception` est l'`OperationCanceledException` — le flux d'événements du run dit `crew_cancelled`, le domaine n'a pas de transition d'annulation. Un handler qui lève une exception est journalisé et ignoré : il ne change ni le `CrewOutput` du run ni l'erreur qu'il rapporte, et les événements suivants sont quand même livrés. L'orchestrateur appelle le dispatcher directement plutôt qu'une unité de travail, dont l'agrégat suivi unique perdrait la crew.

**Quand une unité de travail est sauvegardée** (les commandes CQRS) :

1. Les repositories en mémoire appellent `IUnitOfWork.Track(aggregate)` à chaque ajout/mise à jour. `InMemoryUnitOfWork` garde **un agrégat par scope** (le dernier suivi — une commande, un agrégat).
2. `IUnitOfWork.SaveChangesAsync()` exécute l'étape de persistance (vide pour l'adaptateur en mémoire), dispatche les événements en attente de l'agrégat suivi, puis les efface — même quand un handler lève une exception.
3. `SaveChangesAsync()` est appelé par `UnitOfWorkCommandHandler`, le décorateur placé autour de **chaque handler de commande CQRS** (voir plus bas).

### Handlers

`AddOrkeonApplication()` parcourt l'assembly `Orkeon.Application` et enregistre chaque `IDomainEventHandler<T>` qu'il y trouve (scoped). Quatre sont livrés, tous des handlers de journalisation structurée : `AgentCompletedTaskHandler` et `AgentFailedTaskHandler` (Information et Warning, au fil du run), `CrewExecutionCompletedHandler`, `CrewExecutionFailedHandler`. Un handler situé dans une autre assembly s'enregistre explicitement : `services.AddScoped<IDomainEventHandler<TaskCompletedEvent>, MyHandler>()`.

### Les 31 événements de domaine

| Famille | Événements | Levés par |
|--------|--------|-----------|
| Agent (7) | `AgentCreatedEvent`, `AgentAssignedToTaskEvent`, `AgentStartedTaskEvent`, `AgentCompletedTaskEvent`, `AgentFailedTaskEvent`, `AgentCapabilitiesUpdatedEvent`, `AgentKilledEvent` | agrégat `Agent` |
| Crew (10) | `CrewCreatedEvent`, `AgentJoinedCrewEvent`, `AgentLeftCrewEvent`, `TaskAddedToCrewEvent`, `TaskRemovedFromCrewEvent`, `CrewExecutionStartedEvent`, `CrewExecutionCompletedEvent`, `CrewExecutionFailedEvent`, `CrewProcessTypeChangedEvent`, `CrewGoalUpdatedEvent` | agrégat `Crew` |
| Task (8) | `TaskCreatedEvent`, `TaskAssignedEvent`, `TaskStatusChangedEvent`, `TaskStartedEvent`, `TaskCompletedEvent`, `TaskFailedEvent`, `TaskCancelledEvent`, `TaskDependenciesUpdatedEvent` | agrégat `CrewTaskBase<TContext>` (`CrewTask`) |
| Memory (6) | `MemoryStoreCreatedEvent`, `MemoryAddedEvent`, `MemoryPromotedEvent`, `EntityMemoryUpdatedEvent`, `EpisodicMemoryAddedEvent`, `MemoryClearedEvent` | agrégat `AgentMemoryStore` |

Total : 31 événements de domaine, chacun levé par une méthode de son agrégat. Les records d'événements vivent à côté de leur agrégat (`Agent/Events/`, `Crew/Events/`, `Task/Events/`, `Memory/Events/`).

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

**`ICrewExecutionHook`** (`Orkeon.Application.Crew`) — le hook de niveau run que chaque stratégie de process appelle, sur toutes les sorties (succès, échec, annulation) : `OnTaskStartedAsync`, `OnTaskCompletedAsync`, `OnCrewCompletedAsync`, `OnCrewFailedAsync`, avec des instantanés de tâche et de crew (statut, durée, tokens, ventilation du cache, tâches sautées). C'est un service DI unique ; une exception qu'il lève est journalisée et absorbée. Implémentations livrées : `AutoSummaryWriter` (`AUTO_SUMMARY.md`), l'observateur de `orkeon run --events` (voir [le bus d'événements du run](./run-event-bus.md)) et le hook de progression d'`orkeon-host`. Tout run en échec l'atteint une fois, `OnCrewFailedAsync` : la stratégie rapporte un échec qu'elle a vu, et l'orchestrateur celui qui survient avant toute stratégie — une mémoire inutilisable, un plan dont le fournisseur a échoué, une crew introuvable ou refusée par `ValidateCanKickoff`.

**`ICallbackHandler`** (`Orkeon.Application.Callback`) — notifications au niveau tâche et étape : `OnTaskStartedAsync` / `OnTaskCompletedAsync` autour de chaque exécution d'agent (`AgentExecutionService`) — le `StepsExecuted` de la fin est le nombre de tours de la boucle d'agent, chacun un appel au modèle avec les appels d'outils qu'il a demandés, 0 quand la tâche s'est terminée avant le premier —, et `OnStepStartedAsync` / `OnStepCompletedAsync` autour de **chaque appel d'outil** des boucles d'agent — boucle chat-client, native et texte (un run diffusé passe par les mêmes boucles ; il n'a pas de boucle de streaming à part). L'`Action` d'une étape vaut `tool:<nom>` ; sa fin porte le résultat brut de l'outil dans `Observation`, et `Success = false` quand l'outil a échoué, levé une exception ou été bloqué par le guardian. `CallbackOrchestrator` (`Orkeon.Application.Execution`, l'`ICallbackOrchestrator` scoped) diffuse chaque notification à chaque `ICallbackHandler` enregistré dans la DI ; un handler qui lève une exception est journalisé et ignoré. Les notifications d'étape viennent de `StepNotifyingToolInvocationPipeline`, qui enveloppe le point d'invocation d'outils de l'`ExecutionOrchestrator` (sa propriété `Callbacks`, posée par `AddOrkeonApplication()`). `BaseCallbackHandler` est la base dont dériver ; `CompositeCallbackHandler` diffuse à plusieurs handlers ; `LoggingCallbackHandler` journalise chaque notification mais n'est pas enregistré par défaut.

**Le kickoff diffusé** (`ICrewOrchestrationService.KickoffStreamingAsync`) — ce qu'observe un appel, quand les deux surfaces ci-dessus sont des enregistrements qui servent chaque run d'un conteneur. Il rend les événements du run au fil de l'eau, dans le vocabulaire du bus d'événements du run (`RunEventKinds`) : le début et la fin de chaque tâche (depuis le même point de dispatch que le hook), chaque appel d'outil (depuis le même pipeline que les notifications d'étape), le texte du modèle tel qu'il arrive (depuis la boucle chat-client), puis `run.finished` avec le `CrewOutput` — après un `error` quand le run a échoué. Une crew lancée depuis une tâche — un outil qui lance une autre crew — n'écrit jamais dans le flux de son appelant. Voir [Démarrage](../getting-started/bootstrap.md#modes-dexécution-alternatifs).

---

> **Voir aussi** : [ProcessTypes](../orchestration/process-types.md) · [Sécurité](./security.md) · [Retour à l'index](../INDEX.md)
