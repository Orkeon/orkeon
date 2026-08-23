> 🇬🇧 [English version](../../architecture/event-hub-and-crew-lifecycle.md)

# EventHub & Crew Lifecycle — Spécification de référence

**Périmètre** : `Orkeon.Application` (ports), `Orkeon.Infrastructure` (adapters)
**Public** : développeurs Orkeon

Ce document consolide les décisions architecturales prises pour la couche de messaging inter-agents et inter-crews d'Orkeon, ainsi que pour la mise en sommeil / réveil des crews.

> **C'est une spécification, et elle décrit plus que ce que le dépôt contient aujourd'hui.** Ce qui est livré : le port `IEventHub` et son adaptateur en mémoire, les sept outils d'agent, le pipeline de middlewares et ses cinq étages (§12), la grammaire `links:` et l'ACL (§10). Ce qui ne l'est pas : la persistance SQLite (§11), la mise en sommeil et le réveil des crews (§7, §8), et la chaîne de parité pilotée par JSON Schema (§13.1). Chaque section décrivant quelque chose de non construit le dit sur place — ces notes font partie du contrat.

---

## 1. Objectifs et hors-périmètre

### 1.1 Objectifs

- Fournir aux agents et au système une primitive unifiée de messaging in-memory.
- Permettre la mise en sommeil d'une crew inactive et son réveil ultérieur sur arrivée d'un message attendu.
- Garantir la survie des messages et des waits en cours à un redémarrage du process (persistance SQLite optionnelle).
- Exposer la même surface fonctionnelle dans les trois paradigmes Orkeon : C#, YAML, TypeScript.
- Supporter le messaging inter-crew sécurisé par une whitelist déclarative.

### 1.2 Hors-périmètre v1

- Brokerage distribué multi-nœuds (Redis Streams, NATS) — reporté à v2 derrière le même port.
- Patterns avancés : Request-Stream, Scatter-Gather, priorités, DLQ — composables au-dessus du port quand le besoin se présentera.
- Sommeil au niveau agent individuel — granularité v1 = crew entière.

---

## 2. Vue d'ensemble

```
                      Agent (LLM hétérogène)
                              │
              ┌───────────────┼───────────────┐
              │               │               │
     publish_event     send_request    wait_for_event ...
              └───────────────┼───────────────┘
                              ▼
              ┌─────── IEventHub (Application port) ───────┐
              │                                            │
              │     Publish · Post · Send · Reply          │
              │     Subscribe · GetLastValue               │
              │                                            │
              └─────────────────┬──────────────────────────┘
                                │
            ┌───────────────────┴──────────────────┐
            ▼                                      ▼
   InMemoryEventHub                       SqliteEventHub
   (dev / tests)                          (prod single-node)
            │                                      │
            └──────────────┬───────────────────────┘
                           │
                ┌──────────▼──────────┐
                │   Middleware chain  │  log · trace · ACL · idempotence
                └──────────┬──────────┘
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
   ICrewLifecycleManager        ICrewActivator
   (idle detect, snapshot,      (lazy boot,
    wake on match)               restore on message)
              │                         │
              └────────────┬────────────┘
                           ▼
                  ICrewStateStore (SQLite)
```

---

## 3. Abstractions du port (Application layer)

### 3.1 `IEventHub`

```csharp
namespace Orkeon.Application.EventHub;

public interface IEventHub
{
    // Broadcast 1→N (anonyme, topic-based)
    Task PublishAsync(
        string topic,
        object payload,
        PublishOptions? options,
        CancellationToken ct);

    // Fire-and-forget 1→1 (vers une boîte adressée)
    Task PostAsync(
        MailboxAddress to,
        object payload,
        CancellationToken ct);

    // Request-response 1→1 (timeout obligatoire — pas de Forever ici)
    Task<TResponse> SendAsync<TRequest, TResponse>(
        MailboxAddress to,
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct);

    // Réponse à un Send pendant (corrélation interne)
    Task ReplyAsync(
        CorrelationId correlation,
        object payload,
        CancellationToken ct);

    // Souscription longue durée (filtrée par TargetCrewId automatiquement)
    IAsyncEnumerable<Message> SubscribeAsync(
        string topic,
        CancellationToken ct);

    // Wait court terme sur topic / mailbox / correlation (Finite ou Forever)
    Task<Message> WaitForAsync(
        WaitDescriptor descriptor,
        WaitTimeout timeout,
        CancellationToken ct);

    // Dernière valeur retenue (scope optionnel par crew)
    Task<Message?> GetLastValueAsync(
        string key,
        CrewId? crewScope,
        CancellationToken ct);
}
```

### 3.2 Types associés au port

```csharp
public sealed record PublishOptions
{
    public CrewId? TargetCrewId { get; init; }                    // null = global
    public ImmutableDictionary<string, string>? Metadata { get; init; }
    public bool RetainAsLastValue { get; init; }                  // alimente LastValueCache
    public string? LastValueKey { get; init; }
    public string? SchemaId { get; init; }                        // engage l'étage de validation du §12.3
}

public abstract record WaitDescriptor;

public sealed record WaitOnTopic(
    string Topic,
    ImmutableDictionary<string, string>? MetadataMatch) : WaitDescriptor;

public sealed record WaitOnMailbox(MailboxAddress Address) : WaitDescriptor;

public sealed record WaitOnReply(CorrelationId Correlation) : WaitDescriptor;

public abstract record WaitTimeout;

public sealed record FiniteWaitTimeout(TimeSpan Duration) : WaitTimeout;
// FiniteWaitTimeout.Of(TimeSpan) valide Duration > 0

public sealed record ForeverWaitTimeout : WaitTimeout
{
    public static readonly ForeverWaitTimeout Instance = new();
}
```

### 3.3 Composants associés (autres ports)

```csharp
// Façade orchestrant les transitions de cycle de vie.
// Appelée par l'IIdleDetector (sleep) et l'IEventHub (wake on message / timeout).
// Délègue l'activation effective à ICrewActivator.
public interface ICrewLifecycleManager
{
    Task SleepAsync(CrewId id, SleepReason reason, CancellationToken ct);
    Task<Crew> WakeAsync(CrewId id, Message trigger, CancellationToken ct);
}

// Persistance brute du snapshot. Pas de logique métier.
public interface ICrewStateStore
{
    Task SaveAsync(CrewId id, CrewSnapshot snapshot, CancellationToken ct);
    Task<CrewSnapshot?> LoadAsync(CrewId id, CancellationToken ct);
    Task DeleteAsync(CrewId id, CancellationToken ct);
}

// Implémentation concrète de l'activation : charge depuis ICrewStateStore,
// reconstruit les Agents et Mailboxes, restaure le budget et l'OrchestrationState,
// puis livre le message déclencheur. Pas appelé directement par l'EventHub —
// passe toujours par ICrewLifecycleManager.WakeAsync.
public interface ICrewActivator
{
    Task<Crew> ActivateAsync(CrewId id, Message? trigger, CancellationToken ct);
}

public interface IIdleDetector
{
    void Touch(CrewId id);
    event Func<CrewId, Task> OnIdleAsync;
}

public interface IWaitScheduler
{
    Task ScheduleAsync(CrewId crewId, string waitId, DateTimeOffset expiresAt, CancellationToken ct);
    Task CancelAsync(string waitId, CancellationToken ct);
}

public interface IEventSchemaRegistry
{
    // Récupère le schéma JSON enregistré pour un SchemaId donné
    Task<JsonNode?> GetAsync(string schemaId, CancellationToken ct);

    // Enregistre un schéma (versionné). Rejet si SchemaId déjà enregistré avec un schéma différent.
    Task RegisterAsync(string schemaId, JsonNode schema, CancellationToken ct);
}
```

`IEventHubMiddleware` est défini en §12 (Pipeline de middleware).

---

## 4. Patterns de messaging

| Pattern | Méthode | Sémantique | Peut réveiller ? |
|---|---|---|---|
| **Publish** | `PublishAsync` | Broadcast 1→N, anonyme, topic-based, scope optionnel par crew | Oui (subscribers dormants) |
| **Post** | `PostAsync` | Fire-and-forget 1→1, adressé par `MailboxAddress` | Oui (boîte dormante) |
| **Send** | `SendAsync` | Request-response 1→1, timeout obligatoire | Oui (côté receveur) |
| **Reply** | `ReplyAsync` | Réponse corrélée à un `Send` pendant | Oui (demandeur dormant) |
| **Subscribe** | `SubscribeAsync` | Flux long-lived sur topic | Oui |
| **LastValueCache** | `GetLastValueAsync` + `PublishOptions.RetainAsLastValue` | Slot 1-clé/1-valeur retenu, scopable par crew | Non (lecture seule) |

---

## 5. Surface tools pour les agents

Sept tools, enregistrés par l'appel explicite `AddOrkeonEventHubTools()` aux côtés du hub (`AddOrkeonInMemoryEventHub()` — le runner host apparie les deux appels ; rien n'est automatique). Tous suivent `ToolBase<TRequest, TResponse>` avec DTO scellés et JSON snake_case.

| Tool | Pattern | Input | Output |
|---|---|---|---|
| `publish_event` | Publish | `topic`, `payload`, `target_crew_id?`, `metadata?`, `retain_as_last_value?`, `last_value_key?` | `event_id`, `published_at` |
| `post_message` | Post | `target_mailbox`, `payload`, `metadata?` | `message_id`, `posted_at` |
| `send_request` | Send | `target_mailbox`, `payload`, `timeout_ms` (requis), `metadata?` | `correlation_id`, `response_payload` ou `TimeoutError` |
| `reply_to` | Reply | `correlation_id`, `payload` | `replied_at` |
| `receive_message` | (pull mailbox) | `timeout_ms` ou `wait_forever:true`, `mailbox?` (défaut : agent courant) | `message?` |
| `wait_for_event` | Wait | `topic`, `timeout_ms` ou `wait_forever:true`, `metadata_match?` | `message` ou `timed_out:true` |
| `get_last_value` | LastValueCache | `key`, `crew_scope?` (défaut : crew courante) | `found`, `value`, `set_at` (`found` distingue un null caché d'une clé absente) |

### 5.1 Règles de validation

- `wait_for_event` et `receive_message` exigent **exactement un** des deux champs `timeout_ms` / `wait_forever`. `{}` et `{timeout_ms, wait_forever:true}` sont rejetés.
- `send_request` impose `timeout_ms` (pas de `Forever` sur Send — voir §9.3).
- `Forever` reste un opt-in explicite, jamais un défaut implicite.
- `reply_to` exige un `correlation_id` actif : l'agent l'obtient depuis un `Message` reçu via `receive_message` ou `wait_for_event` (champ `correlation_id` de l'enveloppe). Un `reply_to` avec `correlation_id` inconnu ou déjà répondu est rejeté.
- Les agents ne peuvent pas publier sur un topic commençant par `_system.` (réservé aux messages produits par le hub, ex. `_system.wait_timed_out`).

### 5.2 Subscribe long-lived côté agent

L'interface `IEventHub.SubscribeAsync` retourne un `IAsyncEnumerable<Message>` — adapté au code C# / TypeScript natif. Pour les agents LLM (qui ne peuvent pas consommer un flux), le pattern « subscribe » se réalise via une **boucle de `wait_for_event`** :

```text
loop:
  msg = wait_for_event(topic="orders.*", timeout_ms=600000)
  if msg.timed_out: handle idle / continue
  else: process(msg); continue
```

Chaque appel `wait_for_event` produit un `PendingWait` ; si la crew dort entre deux itérations, elle est réveillée par le message suivant. Sémantique identique à une souscription long-lived, exposée comme une primitive familière au LLM.

---

## 6. Enveloppe `Message` et adressage

### 6.1 Enveloppe canonique

```csharp
public sealed record Message
{
    public required MessageId Id { get; init; }
    public required string Topic { get; init; }
    public required CrewId SourceCrewId { get; init; }
    public AgentId? SourceAgentId { get; init; }                  // null = système
    public CrewId? TargetCrewId { get; init; }                    // null = global
    public MailboxAddress? TargetMailbox { get; init; }
    public CorrelationId? CorrelationId { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public required ImmutableDictionary<string, string> Metadata { get; init; }
    public required ReadOnlyMemory<byte> Payload { get; init; }
    public required string SchemaId { get; init; }
}
```

- **`SourceCrewId`** : toujours renseigné, posé par le hub à partir du contexte de l'appelant. Pas modifiable par l'agent (anti-usurpation).
- **`TargetCrewId`** : optionnel. Null = broadcast global. Pour `Post`/`Send`/`Reply`, dérivé du `TargetMailbox` ; pour `Publish`, fixé par l'agent dans `PublishOptions`.

### 6.2 Format `MailboxAddress` (URI)

```
agent://{crewId}/{agentId}      boîte d'un agent précis
crew://{crewId}                  boîte de la crew (router interne)
topic://{topicName}              alias d'un topic broadcast
client://{name}                  un processus client observateur (voir §10.2.1 et le bus d'événements de run)
```

Validé par regex dans `MailboxAddress.Parse`, l'unique point d'entrée du port (il n'existe pas de voie crew-builder pour les adresses, et le SDK TS du §13.4 est un design non construit). `TargetCrewId` est extrait automatiquement du `TargetMailbox` ; toute incohérence entre les deux est rejetée par le middleware.

---

## 7. Cycle de vie crew

```
                 ┌─────────┐
                 │ Active  │ ◄── démarrage / restauration
                 └────┬────┘
                      │ idle_timeout dépassé
                      │ ET la crew est en WaitingForMessage
                      ▼
                 ┌─────────┐
                 │Snapshot.│  écriture atomique CrewSnapshot + pending_waits
                 └────┬────┘
                      ▼
                 ┌─────────┐
                 │ Asleep  │  ── survit aux redémarrages process ──
                 └────┬────┘
                      │ message matchant un pending_wait
                      │   OU expiration finite atteinte (scheduler)
                      ▼
                 ┌─────────┐
                 │Activat. │  ICrewActivator charge snapshot, recompose Crew
                 └────┬────┘
                      ▼
                  Active (livre le message déclencheur à l'agent)
```

### 7.1 Détection d'idle

`IIdleDetector.Touch(crewId)` est appelé à chaque message reçu ET à chaque transition d'état d'orchestration. Un timer par crew expire après `idle_timeout` configuré. Avant snapshot, vérification d'une condition stricte : la crew **doit** être en état `WaitingForMessage` (pas en traitement actif). Sans ça, on évite d'endormir une crew qui peut encore progresser sans message externe.

### 7.2 Snapshot atomique

Snapshot et écriture dans `pending_waits` sont **dans la même transaction SQLite**. Garantit qu'une crew dormante a toujours un index de routage cohérent. Si la transaction échoue, la crew reste `Active` et l'idle timer redémarre.

### 7.3 Réveil

Deux déclencheurs uniquement :

1. **Message matchant** : l'EventHub consulte ses maps `topic → crewIds` et `mailbox → crewId` ; pour chaque crew dormante touchée, appelle `ICrewActivator.ActivateAsync(id, msg)`.
2. **Expiration finite** : `IWaitScheduler` détecte un `expires_at <= now` ; appelle `ActivateAsync(id, WaitTimedOutMessage)`.

Pas de réveil au démarrage du process. Politique **lazy boot** : au boot, seul l'index `pending_waits` est rechargé en RAM, les crews restent dormantes jusqu'à message matchant. Cohérent avec la sémantique « le message est le seul déclencheur ».

---

## 8. `CrewSnapshot` par mode d'orchestration

### 8.1 Enveloppe

```csharp
public sealed record CrewSnapshot
{
    public required CrewId Id { get; init; }
    public required ProcessType Mode { get; init; }
    public required OrchestrationState Orchestration { get; init; }
    public required ImmutableList<AgentState> Agents { get; init; }
    public required BudgetSnapshot Budget { get; init; }
    public required ImmutableList<MailboxContent> Mailboxes { get; init; }
    public required ImmutableList<PendingWait> PendingWaits { get; init; }
    public required DateTimeOffset SnapshotAt { get; init; }
    public required int SchemaVersion { get; init; }
}
```

### 8.2 `OrchestrationState` discriminé par mode

```csharp
public abstract record OrchestrationState
{
    public sealed record Sequential(
        int CurrentTaskIndex,
        ImmutableList<TaskResult> Completed) : OrchestrationState;

    public sealed record Hierarchical(
        ManagerState Manager,
        ImmutableDictionary<AgentId, DelegationState> Workers) : OrchestrationState;

    public sealed record Parallel(
        ImmutableDictionary<BranchId, BranchState> Branches,
        JoinState? Join) : OrchestrationState;

    public sealed record Consensual(
        int Round,
        ImmutableList<Vote> CastVotes,
        ProposalState Proposal) : OrchestrationState;

    public sealed record Graph(
        NodeId CurrentNode,
        ImmutableDictionary<string, object> StateVars,
        int CycleCount,
        ImmutableList<EdgeTransition> History) : OrchestrationState;

    public sealed record Autonomous(
        AgentTree Spawned,
        ImmutableDictionary<AgentId, BudgetSnapshot> Budgets,
        int CurrentDepth,
        ImmutableList<PendingDelegation> Pending) : OrchestrationState;
}
```

Contenu attendu par mode :

| Mode | Champs |
|---|---|
| **Sequential** | tâche courante + résultats des tâches déjà exécutées (entrées + sorties, pour réinjection contextuelle) |
| **Hierarchical** | état du manager (plan, historique LLM tronqué) + par worker : sous-tâche en cours et résultats partiels |
| **Parallel** | statut par branche (`NotStarted`/`Running`/`WaitingForMessage`/`Done`) + état du join (politique d'agrégation, résultats partiels) |
| **Consensual** | tour de scrutin, votes déjà émis, proposition en cours |
| **Graph** | nœud courant, variables d'état du graphe, compteur de cycles (circuit breaker), historique des transitions |
| **Autonomous** | arbre des sous-agents spawned, un `BudgetSnapshot` par agent, profondeur courante, délégations dont la réponse n'est pas arrivée |

### 8.3 `PendingWait`

```csharp
public sealed record PendingWait(
    string WaitId,
    DateTimeOffset StartedAt,
    DateTimeOffset? ExpiresAt,                    // null ⇔ Forever
    PendingWaitKind Kind);

public abstract record PendingWaitKind
{
    public sealed record OnTopic(
        string Topic,
        CrewId? TargetCrewScope,
        ImmutableDictionary<string, string>? MetadataMatch) : PendingWaitKind;

    public sealed record OnMailbox(MailboxAddress Address) : PendingWaitKind;

    public sealed record OnReply(CorrelationId Correlation) : PendingWaitKind;
}
```

### 8.4 Versioning du schéma

`SchemaVersion` incrémenté à chaque changement de forme. Loader rejette les versions inconnues. Un `ISnapshotMigrator` peut faire `vN → vN+1` lors du chargement.

---

## 9. Sémantique des timeouts

### 9.1 `Finite` vs `Forever`

| Type | Comportement |
|---|---|
| **`WaitTimeout.Finite(d)`** | Si la crew dort, `IWaitScheduler` planifie un réveil à `started_at + d`. À l'expiration : livraison d'un `WaitTimedOutMessage` synthétique. La crew traite ça comme un retour de `wait_for_event` avec `timed_out: true`. |
| **`WaitTimeout.Forever`** | Pas d'entrée scheduler. La crew dort indéfiniment. Réveil exclusivement sur message matchant. |

### 9.2 `WaitTimedOutMessage`

Type de message de premier ordre, livré par le système (pas par un agent) lorsqu'un `WaitTimeout.Finite` expire pendant le sommeil d'une crew. Caractéristiques :

- `SourceCrewId = CrewId.System` (constante réservée, distincte de tout `CrewId` utilisateur)
- `SourceAgentId = null`
- `Topic = "_system.wait_timed_out"` (préfixe `_system.` réservé aux messages produits par le hub)
- `Metadata` contient `original_topic`, `original_wait_id`, `original_started_at`
- `Payload` vide (`ReadOnlyMemory<byte>.Empty`)

Apparait dans `processed_messages` pour l'idempotence (sinon redémarrage = re-livraison du même timeout).

Le préfixe `_system.` est réservé : un agent ne peut pas publier sur un topic commençant par `_system.` — c'est **l'outil `publish_event`** qui lève `ReservedTopicException` avant même d'atteindre le hub (le hub lui-même, donc les appelants in-process, n'est pas gardé ; le middleware de validation ne vérifie que `SchemaId`).

### 9.3 Send ne supporte pas Forever

`send_request` impose `timeout_ms`. `Forever` est interdit. Justification : un demandeur ne peut pas être suspendu indéfiniment en attente d'une réponse — la chaîne d'appel serait bloquée et le budget de la crew demandeuse consommé à vide. `Forever` est réservé aux primitives qui mettent la crew en sommeil pur (`wait_for_event`, `receive_message`, `subscribe`).

### 9.4 Liaison au budget

Le `CancellationToken` passé aux waits est dérivé du `AgentExecutionBudget.LinkedToken` de la crew. Quand le budget expire, le wait s'arrête avec `BudgetExhaustedException` — distinct d'un `TimeoutException`. Permet au superviseur de distinguer « pas de message en temps voulu » de « budget épuisé ».

---

## 10. Messaging inter-crew et ACL

### 10.1 Filtrage à la livraison

Un agent de crew Y reçoit, parmi les messages d'un topic auquel il est abonné, ceux où `TargetCrewId IN (null, Y)`. Filtrage en amont du dispatch, jamais côté agent.

### 10.2 `CrewLink` — autorisation déclarative

```yaml
name: billing-crew
links:
  - to: fraud-crew
    direction: bidirectional
    allowed_topics: [fraud.check, fraud.result]
  - to: audit-crew
    direction: outbound
    allowed_topics: [audit.event]
  - to: "client:studio"        # un pair externe, pas une crew — voir plus bas
    direction: bidirectional
```

`links:` se place à côté de `name:` et `agents:` — à la racine d'un YAML de crew en fichier unique, ou dans le `crew.yaml` de la disposition multi-fichiers. Le déclarer depuis le builder fluide C# n'est **pas** possible : les liens atteignent l'ACL par le YAML d'une crew et `CrewFactory`.

`to:` nomme la crew cible par son **`name:`**, comparé mot pour mot — la seule identité qu'un auteur YAML possède en écrivant le bloc, les ids de crew étant frappés à la création. Chaque crew enregistre son nom auprès de l'ACL à sa création, même sans déclarer de liens : les liens des *autres* doivent pouvoir la nommer.

L'étage ACL refuse les `Post` / `Send` / `Publish` scopé qu'aucune `CrewLink` n'autorise. Quatre règles, chacune avec sa raison :

| Cas | Décision | Pourquoi |
|---|---|---|
| Message **sans cible** (`Publish` global, ou boîte `topic://`) | passe | une diffusion est une offre, pas une remise ; les abonnés filtrent de leur côté |
| Crew n'ayant **jamais déclaré** de bloc `links:` | passe par défaut | le hub a été livré sans aucune ACL : refuser le trafic non déclaré le jour où l'étage s'allume casserait toutes les crews existantes. Un déploiement qui veut une porte fermée enregistre `RestrictiveCrewLinkPolicy` — et sous elle, un émetteur non déclaré passe encore quand la *cible* lui a accordé un lien `inbound` (§10.3) |
| Crew ayant **déclaré** des liens | tenue à ses liens — bloc déclaré-mais-vide compris, qui refuse tout | déclarer est le geste qui ferme la porte ; un bloc dont toutes les entrées ont échoué au parsing doit la fermer, jamais l'ouvrir |
| Crew dont un lien déclaré correspond | passe | pour un publish scopé, le lien doit nommer la cible **et** autoriser le topic ; pour `Post`/`Send`, le lien suffit — voir ci-dessous |

Un **`allowed_topics` vide** autorise tous les topics : un lien qui n'autoriserait rien serait sans objet, donc le cas vide est une confiance, pas un accident. La liste ne contraint que les **topics** : le courrier point à point (`Post`/`Send`) porte un topic synthétique du hub qu'aucun auteur ne pourrait nommer, il est donc autorisé par le lien lui-même — direction et destinataire. Une entrée sans `to:`, ou avec une `direction:` illisible, est **écartée avec un avertissement** plutôt que devinée — une direction devinée est une autorisation que l'auteur n'a jamais écrite — et la présence du bloc ferme quand même la porte : une autorisation malformée ne doit jamais devenir permissive.

Une forme YAML échappe à cette règle, et elle est dite plutôt que cachée : une clé **`links:` nue, sans valeur**, se désérialise en null — indistinguable d'un bloc absent — et se lit donc comme *jamais déclaré*. Pour fermer la porte explicitement, écrivez `links: []` : la liste vide est une vraie déclaration, et elle refuse tout.

### 10.2.1 Nommer un pair externe

`CrewLink` autorise une crew à parler à une crew. Un processus hors du hub — Studio qui regarde un run, une passerelle — n'en est pas une, et il atteint le hub par le schéma de boîte aux lettres `client://{nom}`. Un lien le nomme avec le préfixe réservé `client:`, comme dans l'exemple ci-dessus.

Sans cela, un pair externe échapperait à l'ACL simplement en n'étant pas modélisé. C'est le seul endroit où l'implémentation étend cette spécification au lieu de l'appliquer.

### 10.3 Direction de la `CrewLink`

| Direction | Sens autorisé |
|---|---|
| `outbound` | A peut envoyer vers B, mais pas l'inverse |
| `inbound` | B peut envoyer vers A, mais pas l'inverse |
| `bidirectional` | Les deux sens |

Une `CrewLink` est vérifiée côté **émetteur**, au moment du `PublishAsync` / `PostAsync` / `SendAsync` — trafic de boîte aux lettres compris, ce qui compte puisque `client://` ne s'atteint que par là. La déclaration propre de l'émetteur gouverne son trafic sortant : un lien `outbound` ou `bidirectional` l'accorde, un lien `inbound` non.

`inbound` est un **grant** : « le pair nommé peut m'écrire ». Il est consulté sur les déclarations de la *cible* quand l'émetteur n'a lui-même jamais déclaré de bloc `links:` et que le déploiement refuse le trafic non déclaré — exactement le moment où le mot-clé compte. Il ne rouvre jamais une porte que l'émetteur a fermée lui-même : une crew qui a déclaré des liens ne nommant pas la cible reste refusée, quoi que la cible accorde.

L'autorisation a lieu **une fois**, à l'envoi : le chemin de réception ne revérifie délibérément pas, car revérifier refuserait un message déjà passé. Une hypothèse voyage avec cette décision : **le lecteur d'une boîte en est le propriétaire.** Le hub auto-enregistre les attentes et la lecture est destructive, donc `receive_message` n'accepte que les boîtes de la crew appelante — sans cette restriction, n'importe quel agent pourrait siphonner `client://studio`, le trafic même que l'ACL garde à l'écriture.

### 10.4 Garanties d'ordering

| Niveau | Garantie | Non-garanti |
|---|---|---|
| Mailbox individuelle | FIFO strict : les messages livrés à `agent://X/Y` sont reçus dans l'ordre de leur `PublishedAt` | — |
| Topic | FIFO par topic et par subscriber : un même subscriber voit les messages d'un topic dans l'ordre | Ordre entre subscribers différents |
| Global | — | Aucun ordre garanti entre topics, ni entre un topic et une mailbox |

Conséquence pour les modèles hétérogènes : si une logique métier dépend d'un ordre entre deux topics distincts, il faut un topic « parent » qui multiplexe, ou utiliser des `CorrelationId` pour reconstituer la séquence applicativement.

---

## 11. Persistance SQLite

### 11.1 Schéma

```sql
CREATE TABLE crew_snapshots (
    crew_id        TEXT PRIMARY KEY,
    snapshot       BLOB NOT NULL,                -- JSON sérialisé
    mode           TEXT NOT NULL,                -- ProcessType
    schema_version INTEGER NOT NULL,
    snapshot_at    TEXT NOT NULL,                -- ISO8601
    state          TEXT NOT NULL                 -- Asleep | Failed | Completed
);

CREATE TABLE pending_waits (
    wait_id          TEXT PRIMARY KEY,
    crew_id          TEXT NOT NULL,
    kind             TEXT NOT NULL,              -- OnTopic | OnMailbox | OnReply
    topic            TEXT,
    target_crew_id   TEXT,
    target_mailbox   TEXT,
    correlation_id   TEXT,
    metadata_match   TEXT,                       -- JSON
    started_at       TEXT NOT NULL,
    expires_at       TEXT,                       -- NULL = Forever
    FOREIGN KEY (crew_id) REFERENCES crew_snapshots(crew_id) ON DELETE CASCADE
);

CREATE INDEX idx_pending_waits_topic_crew
    ON pending_waits(topic, target_crew_id);
CREATE INDEX idx_pending_waits_mailbox
    ON pending_waits(target_mailbox);
CREATE INDEX idx_pending_waits_correlation
    ON pending_waits(correlation_id);
CREATE INDEX idx_pending_waits_expires
    ON pending_waits(expires_at) WHERE expires_at IS NOT NULL;

CREATE TABLE mailboxes (
    mailbox_address TEXT NOT NULL,
    message_id      TEXT NOT NULL,
    payload         BLOB NOT NULL,
    metadata        TEXT NOT NULL,               -- JSON
    received_at     TEXT NOT NULL,
    PRIMARY KEY (mailbox_address, message_id)
);

CREATE TABLE outbox (
    message_id     TEXT PRIMARY KEY,
    source_crew_id TEXT NOT NULL,
    target_mailbox TEXT,
    target_crew_id TEXT,
    topic          TEXT NOT NULL,
    payload        BLOB NOT NULL,
    metadata       TEXT NOT NULL,                -- JSON
    created_at     TEXT NOT NULL,
    dispatched_at  TEXT,
    state          TEXT NOT NULL                 -- Pending | Dispatched | Failed
);

CREATE INDEX idx_outbox_pending
    ON outbox(state, created_at) WHERE state = 'Pending';

CREATE TABLE processed_messages (
    message_id   TEXT NOT NULL,
    crew_id      TEXT NOT NULL,
    processed_at TEXT NOT NULL,
    PRIMARY KEY (message_id, crew_id)
);
```

### 11.2 Idempotence de livraison

Avant traitement d'un message, l'activator consulte `processed_messages`. Si présent, message ignoré (déjà traité). À la fin du traitement réussi, insertion dans `processed_messages` dans la même transaction que l'écriture du nouveau snapshot. Indispensable pour gérer les redémarrages mid-handle.

### 11.3 Outbox transactionnel

Quand une crew traite un message et publie une ou plusieurs réponses, la transaction unique est :

1. `INSERT INTO processed_messages(message_id, crew_id)`.
2. `UPDATE/INSERT INTO crew_snapshots(crew_id, ...)`.
3. `INSERT INTO outbox(message_id, ...)` pour chaque message sortant.

Un dispatcher de fond (`IOutboxDispatcher`) lit l'outbox et pousse dans l'EventHub réel. Garantit qu'aucun message sortant n'est perdu ni dupliqué après crash.

### 11.4 Purge

| État crew | Politique |
|---|---|
| `Completed` | Suppression immédiate du snapshot + waits associés (cascade FK) |
| `Failed` | Conservation N jours (configurable, défaut 7) pour audit, puis suppression |
| `Asleep` | Conservée tant que des `pending_waits` existent ; sinon GC orphelin après TTL configurable |

`processed_messages` : conservation cohérente avec la rétention `crew_snapshots`.

---

## 12. Pipeline de middleware

```csharp
public interface IEventHubMiddleware
{
    Task<Message> OnPublishAsync(Message msg, Func<Message, Task<Message>> next, CancellationToken ct);
    Task<Message> OnReceiveAsync(Message msg, Func<Message, Task<Message>> next, CancellationToken ct);
}
```

L'exécuteur est `EventHubMiddlewarePipeline`. L'ordre est celui de l'enregistrement sur le chemin de publication, et l'inverse sur le chemin de réception : un étage enveloppe donc un message symétriquement à l'aller et au retour. Un étage court-circuite en **levant** ; le pipeline n'attrape pas, car un refus doit atteindre l'appelant.

L'ordre ci-dessous n'est pas cosmétique : sur le **chemin de publication**, la journalisation vient en premier pour voir tout ce qu'un étage ultérieur rejette, et la validation en dernier parce qu'elle est le seul étage qui consulte un magasin. Sur le chemin de réception, la chaîne tourne en sens inverse par construction : l'observabilité y est donc la plus intérieure — un message qu'un étage de réception refuse (un doublon d'idempotence) est écarté avant que la journalisation ne parle ; la ligne Debug du hub couvre ce cas.

| # | Étage | Enregistrement | Rôle |
|---|---|---|---|
| 1 | `LoggingEventHubMiddleware` | `AddOrkeonEventHubObservability()` | log structuré : path, topic, id de message, millisecondes écoulées |
| 2 | `TelemetryEventHubMiddleware` | idem | spans OTel sur la source `Orkeon.EventHub`, attributs `crew.source`, `crew.target`, `event.topic`, `event.pattern` |
| 3 | `AclEventHubMiddleware` | `AddOrkeonEventHubAcl(policy?)` | vérifie `CrewLink` à la publication ; refuse par `EventAclException` (§10) |
| 4 | `IdempotencyEventHubMiddleware` | `AddOrkeonEventHubIdempotency(capacity?)` | refuse un message qu'une boîte aux lettres a déjà consommé, en réception |
| 5 | `ValidationEventHubMiddleware` | `AddOrkeonEventHubValidation()` | vérifie qu'un `SchemaId` déclaré nomme un contrat enregistré, à la publication |

Chaque étage est opt-in — un hub que personne ne regarde ne paie rien — et les étages custom sont injectables par `AddEventHubMiddleware<T>()`.

### 12.1 Où tournent les étages

Les étages de publication tournent sur `Publish`, **sur `Post` et `Send`**, et **sur `Reply`** : le trafic de boîte aux lettres doit les traverser, puisque l'ACL garde qui peut atteindre une boîte et que `client://` — la seule adresse qui sort du processus — ne s'atteint que par là. Une réponse est du trafic de hub comme un autre — `reply_to` et le pont client l'atteignent depuis l'extérieur du processus, et une réponse ni journalisée ni spannée serait le seul message que personne n'observe. Une exemption délibérée : l'ACL n'*autorise* pas une réponse. Une réponse est adressée par son id de corrélation — un ULID frappé par le demandeur et remis au seul destinataire de la requête — donc posséder l'id **est** l'autorisation, à la manière d'une capability ; un contrôle par lien exigerait une grammaire « qui peut me répondre » que personne n'a demandée.

Les étages de réception tournent là où un destinataire *consomme* un message : consommation d'un abonnement, attente sur un topic, attente sur une boîte aux lettres. L'attente d'une réponse à un `Send` n'y passe pas — c'est la queue d'un échange dont la réponse a déjà traversé les étages de publication, et elle dénoue un `TaskCompletionSource` au lieu de vider un canal.

### 12.2 L'idempotence ne garde que le point à point

Un message adressé à une boîte aux lettres a exactement un lecteur légitime : « déjà traité ? » y est une question bien posée. Un message de topic atteint légitimement **chaque** abonné, et le dédupliquer par identifiant affamerait tous sauf le premier — un bug qui ressemblerait à une fonctionnalité. Le trafic de topic passe donc intact.

Le contrat de middleware ne sait que laisser passer ou lever ; « écarte ce doublon » se dit donc par `DuplicateMessageException`, que le hub attrape au point de livraison. Un consommateur ne la voit jamais.

> **La mémoire ne survit pas au processus.** C'est un ensemble borné d'identifiants dans le processus du hub — ce qui est cohérent, le hub étant lui aussi en mémoire. Un redémarrage la vide et un message rejoué serait retraité. C'est une limite assumée : l'idempotence durable appartient au hub durable du §11, et le port absorbe le changement le jour où il existe.

### 12.3 Ce que la validation valide

Que le contrat déclaré **existe** dans `IEventSchemaRegistry` — pas que la charge utile s'y conforme. Aucun moteur JSON Schema n'est embarqué ici, et la moitié d'un moteur ressemblerait à une garantie sans en être une. Ce que l'étage attrape est réel : une faute de frappe dans un `schema_id`, ou un type d'événement que le déploiement n'a jamais déclaré.

Un message portant `Message.NoDeclaredSchemaId` (`"_none"` — délibérément pas un id plausible : un déploiement pourrait légitimement enregistrer `application/json`, et une sentinelle en collision passerait sans contrôle, en silence) ne déclare aucun contrat — c'est le cas de tout `Post`, `Send` et `Reply` — et passe. Déclarer un schéma est le geste qui engage le contrôle, comme déclarer un lien ferme la porte de l'ACL. Le refus est levé à la **publication**, car un abonné ne peut rien à un schéma déclaré par quelqu'un d'autre.

---

## 13. Parité C# / YAML / TypeScript

### 13.1 Schéma JSON canonique

Source unique : `schemas/orkeon-crew-schema.json`, versionné dans le repo. Utilisé pour :

- Valider le YAML au chargement (`JsonSchema.Net`).
- Générer les types TypeScript (`json-schema-to-typescript`).
- Documenter le contrat (Swagger / Redoc).

Sans cette source unique, les trois paradigmes dérivent en quelques semaines.

### 13.2 C# — fluent builder + DI

```csharp
// Non construit : .IdleTimeout et .Links n'existent pas sur CrewBuilder, et les
// liaisons enregistrées par .OnEvent ne sont distribuées par rien. Déclarez les
// liens en YAML (§10.2) ; consommez le hub directement, comme ci-dessous.
var crew = new CrewBuilder()
    .Name("order-processor")
    .Build();

// Consommation directe pour code utilisateur
public sealed class CustomFlow(IEventHub hub)
{
    public Task NotifyAsync() =>
        hub.PublishAsync(
            "audit.event",
            payload,
            new PublishOptions { TargetCrewId = null /* global */ },
            ct);
}
```

### 13.3 YAML — déclaratif (forme de design, pas la grammaire livrée)

La grammaire **livrée** est le bloc `links:` à racine plate (voir [le schéma YAML](./yaml-schema.md)) ; la forme à racine `crew:` ci-dessous, avec `idle_timeout` et les blocs `event:`, appartient au design non construit de cette section :

```yaml
crew:
  name: order-processor
  idle_timeout: 5m
  links:
    - to: fraud-crew
      direction: bidirectional
      allowed_topics: [fraud.check, fraud.result]
  event:
    publish:
      - topic: order.processed
        target_crew_id: billing-crew
      - topic: audit.event             # broadcast global
    subscribe:
      - topic: order.received
        deliver_to: agent://order-processor/router
```

### 13.4 TypeScript SDK

```typescript
import { defineCrew } from '@orkeon/sdk';

export const orderProcessor = defineCrew({
  name: 'order-processor',
  idleTimeout: '5m',
  links: [
    {
      to: 'fraud-crew',
      direction: 'bidirectional',
      allowedTopics: ['fraud.check', 'fraud.result']
    }
  ],
  event: {                              // singulier, lié à l'EventHub
    publish: [
      { topic: 'order.processed', targetCrewId: 'billing-crew' },
      { topic: 'audit.event' }
    ],
    subscribe: [
      { topic: 'order.received', deliverTo: 'agent://order-processor/router' }
    ],
  },
});

// Côté agent custom
agent.event.publish('order.processed', payload, { targetCrewId: 'billing-crew' });

const resp = await agent.event.send(
    'agent://fraud-crew/scorer',
    req,
    { timeoutMs: 5000 });

await agent.event.waitFor('order.received', { waitForever: true });
```

### 13.5 Convention de nommage : `event` singulier

La propriété SDK est `event` au **singulier**, pas `events`, pour trois raisons :

- éviter la collision avec le module Node natif `node:events`,
- éviter la confusion avec une « collection de listeners » (`events: Listener[]`),
- aligner sémantiquement : `agent.event.publish(...)` se lit « émettre un événement », pas « accéder à la liste des events ».

Le YAML utilise aussi `event:` (singulier) pour la cohérence cross-paradigme.

---

## 14. Observabilité

### 14.1 Spans OTel — attributs standards

Ce que `TelemetryEventHubMiddleware` pose **aujourd'hui** (quatre attributs) :

- `event.topic`
- `event.pattern` (`publish` | `receive` — les deux seules valeurs émises)
- `crew.source`
- `crew.target` (seulement quand non-null)

Conçus mais **non construits** : `event.correlation_id`, `event.message_id`,
`event.outcome`, et le vocabulaire de patterns plus fin (`post`/`send`/`reply`/
`subscribe`/`wait`).

### 14.2 Métriques — **non construites**

Aucun instrument EventHub n'existe encore (`OrkeonMetrics` ne couvre que
`orkeon.llm.*`, `orkeon.tool.*`, `orkeon.task.*`, `orkeon.crew.*`). Le jeu
conçu, gardé ici comme cible :

- `orkeon_event_published_total{topic, source_crew, target_crew}` — counter
- `orkeon_event_latency_seconds{pattern}` — histogram
- `orkeon_crew_asleep_total{crew}` — gauge
- `orkeon_wait_timed_out_total{topic}` — counter
- `orkeon_pending_waits_total{kind}` — gauge

---

## 15. Roadmap d'implémentation

**Construit.** Le port `IEventHub`, `PublishOptions`/`WaitDescriptor`, `IEventSchemaRegistry`, `InMemoryEventHub`, les sept outils d'agent, la grammaire `links:` et l'ACL (§10), et tout le pipeline de middlewares avec ses cinq étages (§12) — journalisation, télémétrie, ACL, idempotence, validation. (Les interfaces de cycle de vie du §3.3 — `ICrewLifecycleManager`, `ICrewStateStore`, `ICrewActivator`, `IIdleDetector`, `IWaitScheduler` — sont du design, listées comme manquantes plus bas.)

**Non construit**, chacun derrière le même port pour que le construire ne change aucun appelant :

| Manquant | Ce que cela apporterait | Où c'est décrit |
|---|---|---|
| Hub durable et magasin d'état | messages et attentes en vol survivant à un redémarrage ; idempotence qui dépasse le processus | §11 |
| Sommeil et réveil des crews | `ICrewLifecycleManager`, `IIdleDetector`, `IWaitScheduler`, snapshots | §7, §8 |
| Chaîne JSON Schema canonique | YAML validé et types TypeScript générés depuis une source unique | §13.1 |
| Courtage distribué | multi-nœuds, publish d'un côté et wait de l'autre, ACL préservée | §1.2 |

Deux inerties plus petites, à nommer plutôt qu'à laisser découvrir : `CrewBuilder.OnEvent` enregistre des liaisons que rien ne distribue, et le builder fluide ne sait pas déclarer une `CrewLink`.

---

## 16. Tests requis

### 16.1 Unitaires (port + InMemory)

- `Publish` global → tous abonnés voient.
- `Publish` scopé crew X → seuls abonnés de X voient.
- `Post` vers boîte inexistante → erreur typée.
- `Send` répondu dans le timeout → réponse retournée.
- `Send` non répondu avant timeout → `TimeoutException` typée.
- `Reply` sans `Send` pendant → erreur.
- `WaitFor` Finite expiré → `WaitTimedOutMessage` livré.
- `WaitFor` Forever sans message → bloque indéfiniment (test annulé via CT).
- `WaitFor` budget épuisé → `BudgetExhaustedException` (distinct du timeout).

### 16.2 Intégration (SQLite)

- Sleep → wake on message matchant.
- Sleep → wake on timeout finite.
- Sleep Forever → wake on message, jamais via scheduler.
- Crash mid-handle → redelivery via outbox au reboot.
- Idempotence : même message livré 2× → traité 1×.
- Lazy boot : index reconstruit, crews dormantes, pas de réveil spontané.
- Boot avec `expires_at` déjà passé → réveil immédiat avec `WaitTimedOutMessage`.
- Subscribe long-lived dormant → arrivée d'un message → réveil + livraison via la prochaine itération de `wait_for_event`.
- FIFO mailbox : 100 messages postés rapidement → réception dans l'ordre exact.
- Topic réservé `_system.*` : tentative de publish par un agent → rejet typé `ReservedTopicException`.

### 16.3 Inter-crew

- `Send` crew A → crew B autorisé par `CrewLink` → livré.
- `Send` crew A → crew B sans `CrewLink` → `EventAclException`.
- `Publish` scopé `target_crew_id: B` → un abonné dans C ne reçoit jamais.
- Fuite cross-crew : test de non-régression standard.

### 16.4 Parité paradigmes

- Config crew identique en C#/YAML/TS produit le même `Crew` runtime.
- Validation JSON Schema rejette YAML invalide.
- Types TS générés couvrent 100 % du schéma canonique.

---

## 17. Hors-périmètre v1 (différé)

- **Brokerage distribué** : v2 avec `RedisStreamsEventHub` ou `NatsJetStreamEventHub`, même port.
- **Request-Stream** : `Send` retournant `IAsyncEnumerable<Response>` pour streaming token-par-token. Composable plus tard sans casser le port.
- **Scatter-Gather** : composable au-dessus de `Send` + `Task.WhenAll`/`WhenAny` côté C# applicatif. Pas de pattern hub natif.
- **DLQ** : à brancher via middleware quand un volume d'erreurs le justifie.
- **Sommeil par agent** : granularité v1 = crew. Sommeil agent-level reporté.
- **Priorités** : pas de queue prioritaire en v1. FIFO par mailbox.

---

## 18. Décisions architecturales clés (récap)

| Décision | Choix | Justification |
|---|---|---|
| Réveil de crew | Implicite via `PendingWaits` du snapshot | Pas de double déclaration `wait` + `wakeup` ; évite la désynchro |
| Boot après crash | Lazy (index uniquement, crews dormantes) | Cohérent avec « le message est le seul déclencheur » |
| Granularité sommeil | Crew entière | Simplifie le contrat, évite `IAgentLifecycleManager` |
| Timeout Forever | Autorisé sur `wait_for_event` / `receive_message` / `subscribe` uniquement | Send avec Forever bloquerait la chaîne d'appel |
| Persistance v1 | SQLite single-node | Cohérent avec `SqliteStateStore` / `SqliteMemoryProvider` existants, pas de dépendance externe |
| Distribué | Reporté en v2 derrière le même port | Stabilité > expressivité ; permet d'évoluer sans casser l'API |
| Multi-tenant | `CrewLink` whitelist + middleware ACL | Souveraineté inter-crew explicite, défense en profondeur |
| Paradigmes | Schéma JSON canonique source unique | Évite la dérive C# / YAML / TS |
| Nommage TS | `event` singulier | Anti-collision `node:events`, sémantique « émettre un événement » |
| Idempotence | Table `processed_messages` + outbox transactionnel | Survie aux crashes mid-handle, atomicité publish+ack |

---

## 19. Glossaire

- **Crew dormante** : crew avec `CrewSnapshot` persisté, instances runtime libérées.
- **Lazy boot** : au redémarrage, l'index `pending_waits` est rechargé sans réhydrater les crews.
- **PendingWait** : attente en cours d'une crew, capturée dans son snapshot, utilisée comme trigger de réveil implicite.
- **CrewLink** : déclaration ACL d'une autorisation de messaging entre deux crews.
- **LastValueCache** : cache 1-clé/1-valeur scopable par crew, alimenté via `PublishOptions.RetainAsLastValue`.
- **Outbox** : table SQLite tampon des messages sortants pour garantir l'atomicité publish+ack.
- **Forever** : timeout infini, autorisé sur `wait_for_event`, `receive_message` et `subscribe`. Réveil exclusif sur message matchant.
- **MailboxAddress** : URI structuré identifiant une boîte (`agent://`, `crew://`, `topic://`, `client://`).
- **`WaitTimedOutMessage`** : message système livré à une crew lorsque son `WaitTimeout.Finite` expire pendant son sommeil. Topic réservé : `_system.wait_timed_out`.
- **`CrewId.System`** : constante réservée représentant le hub lui-même comme émetteur. Utilisée comme `SourceCrewId` pour tous les messages système (timeouts, notifications de cycle de vie). Aucune crew utilisateur ne peut prendre cet ID.
- **Topic réservé `_system.*`** : préfixe interdit aux agents. Seul le hub peut publier sur ces topics. Toute tentative est rejetée par le middleware de validation.

---

## 20. Références

- `docs/architecture/raggable-tree.md` — pattern Clean Architecture port/adapter de référence dans Orkeon.
- `src/core/Orkeon.Domain/Autonomous/AgentExecutionBudget.cs` — budget multi-dimensionnel utilisé pour les `CancellationToken` budget-aware.
- `src/core/Orkeon.Infrastructure/Communication/InMemoryAgentChannel.cs` — pattern de communication lock-free existant, à réutiliser pour `InMemoryEventHub`.
- `src/core/Orkeon.Infrastructure/Memory/EncryptedMemoryProviderDecorator.cs` — référence pour `SqliteCrewStateStore`.
