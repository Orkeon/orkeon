> 🇫🇷 [Version française](../fr/architecture/event-hub-and-crew-lifecycle.md)

# EventHub & Crew Lifecycle — Reference Specification

**Status**: design frozen, ready for v1 implementation
**Date**: 2026-05-23
**Scope**: `Orkeon.Application` (ports), `Orkeon.Infrastructure` (InMemory + SQLite adapters)
**Audience**: Orkéon developers

This document consolidates the architectural decisions made for Orkéon's inter-agent and inter-crew messaging layer, as well as for putting crews to sleep and waking them up. It serves as the single reference for the v1 implementation.

---

## 1. Goals and out of scope

### 1.1 Goals

- Provide agents and the system with a unified in-memory messaging primitive.
- Allow an inactive crew to be put to sleep and woken up later upon arrival of an expected message.
- Guarantee that messages and in-flight waits survive a process restart (optional SQLite persistence).
- Expose the same functional surface across Orkéon's three paradigms: C#, YAML, TypeScript.
- Support inter-crew messaging secured by a declarative whitelist.

### 1.2 Out of scope for v1

- Distributed multi-node brokerage (Redis Streams, NATS) — deferred to v2 behind the same port.
- Advanced patterns: Request-Stream, Scatter-Gather, priorities, DLQ — composable on top of the port when the need arises.
- Sleep at the individual agent level — v1 granularity = whole crew.

---

## 2. Overview

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

## 3. Port abstractions (Application layer)

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

### 3.2 Types associated with the port

```csharp
public sealed record PublishOptions
{
    public CrewId? TargetCrewId { get; init; }                    // null = global
    public ImmutableDictionary<string, string>? Metadata { get; init; }
    public bool RetainAsLastValue { get; init; }                  // alimente LastValueCache
    public string? LastValueKey { get; init; }
}

public abstract record WaitDescriptor
{
    public sealed record OnTopic(
        string Topic,
        ImmutableDictionary<string, string>? MetadataMatch) : WaitDescriptor;

    public sealed record OnMailbox(MailboxAddress Address) : WaitDescriptor;

    public sealed record OnReply(CorrelationId Correlation) : WaitDescriptor;
}

public abstract record WaitTimeout
{
    public sealed record Finite(TimeSpan Duration) : WaitTimeout;

    public sealed record Forever : WaitTimeout
    {
        public static readonly Forever Instance = new();
    }
}
```

### 3.3 Associated components (other ports)

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
    Task<JsonSchema?> GetAsync(string schemaId, CancellationToken ct);

    // Enregistre un schéma (versionné). Rejet si SchemaId déjà enregistré avec un schéma différent.
    Task RegisterAsync(string schemaId, JsonSchema schema, CancellationToken ct);
}
```

`IEventHubMiddleware` is defined in §12 (Middleware pipeline).

---

## 4. Messaging patterns

| Pattern | Method | Semantics | Can wake? |
|---|---|---|---|
| **Publish** | `PublishAsync` | 1→N broadcast, anonymous, topic-based, optional per-crew scope | Yes (sleeping subscribers) |
| **Post** | `PostAsync` | 1→1 fire-and-forget, addressed by `MailboxAddress` | Yes (sleeping mailbox) |
| **Send** | `SendAsync` | 1→1 request-response, mandatory timeout | Yes (receiver side) |
| **Reply** | `ReplyAsync` | Correlated response to a pending `Send` | Yes (sleeping requester) |
| **Subscribe** | `SubscribeAsync` | Long-lived stream on a topic | Yes |
| **LastValueCache** | `GetLastValueAsync` + `PublishOptions.RetainAsLastValue` | Retained 1-key/1-value slot, scopable per crew | No (read-only) |

---

## 5. Tool surface for agents

Seven tools, auto-registered as soon as `IEventHub` is in DI. All follow `ToolBase<TRequest, TResponse>` with sealed DTOs and snake_case JSON.

| Tool | Pattern | Input | Output |
|---|---|---|---|
| `publish_event` | Publish | `topic`, `payload`, `target_crew_id?`, `metadata?`, `retain_as_last_value?`, `last_value_key?` | `event_id`, `published_at` |
| `post_message` | Post | `target_mailbox`, `payload`, `metadata?` | `message_id`, `posted_at` |
| `send_request` | Send | `target_mailbox`, `payload`, `timeout_ms` (required), `metadata?` | `correlation_id`, `response_payload` or `TimeoutError` |
| `reply_to` | Reply | `correlation_id`, `payload` | `replied_at` |
| `receive_message` | (pull mailbox) | `timeout_ms` or `wait_forever:true`, `mailbox?` (default: current agent) | `message?` |
| `wait_for_event` | Wait | `topic`, `timeout_ms` or `wait_forever:true`, `metadata_match?` | `message` or `timed_out:true` |
| `get_last_value` | LastValueCache | `key`, `crew_scope?` (default: current crew) | `value`, `set_at` or null |

### 5.1 Validation rules

- `wait_for_event` and `receive_message` require **exactly one** of the two fields `timeout_ms` / `wait_forever`. `{}` and `{timeout_ms, wait_forever:true}` are rejected.
- `send_request` enforces `timeout_ms` (no `Forever` on Send — see §9.3).
- `Forever` remains an explicit opt-in, never an implicit default.
- `reply_to` requires an active `correlation_id`: the agent obtains it from a `Message` received via `receive_message` or `wait_for_event` (the envelope's `correlation_id` field). A `reply_to` with an unknown or already-answered `correlation_id` is rejected.
- Agents cannot publish on a topic starting with `_system.` (reserved for hub-produced messages, e.g. `_system.wait_timed_out`).

### 5.2 Long-lived subscribe on the agent side

The `IEventHub.SubscribeAsync` interface returns an `IAsyncEnumerable<Message>` — suited to native C# / TypeScript code. For LLM agents (which cannot consume a stream), the "subscribe" pattern is realized via a **`wait_for_event` loop**:

```text
loop:
  msg = wait_for_event(topic="orders.*", timeout_ms=600000)
  if msg.timed_out: handle idle / continue
  else: process(msg); continue
```

Each `wait_for_event` call produces a `PendingWait`; if the crew sleeps between two iterations, it is woken up by the next message. Semantics identical to a long-lived subscription, exposed as a primitive familiar to the LLM.

---

## 6. `Message` envelope and addressing

### 6.1 Canonical envelope

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

- **`SourceCrewId`**: always set, stamped by the hub from the caller's context. Not modifiable by the agent (anti-spoofing).
- **`TargetCrewId`**: optional. Null = global broadcast. For `Post`/`Send`/`Reply`, derived from the `TargetMailbox`; for `Publish`, set by the agent in `PublishOptions`.

### 6.2 `MailboxAddress` format (URI)

```
agent://{crewId}/{agentId}      boîte d'un agent précis
crew://{crewId}                  boîte de la crew (router interne)
topic://{topicName}              alias d'un topic broadcast
```

Validated by regex at the entry of the port, the C# builder, the YAML loader and the TS SDK. `TargetCrewId` is extracted automatically from the `TargetMailbox`; any inconsistency between the two is rejected by the middleware.

---

## 7. Crew lifecycle

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

### 7.1 Idle detection

`IIdleDetector.Touch(crewId)` is called on every received message AND on every orchestration state transition. A per-crew timer expires after the configured `idle_timeout`. Before snapshotting, a strict condition is checked: the crew **must** be in the `WaitingForMessage` state (not actively processing). This avoids putting to sleep a crew that can still make progress without an external message.

### 7.2 Atomic snapshot

The snapshot and the write into `pending_waits` happen **within the same SQLite transaction**. This guarantees that a sleeping crew always has a consistent routing index. If the transaction fails, the crew stays `Active` and the idle timer restarts.

### 7.3 Wake-up

Only two triggers:

1. **Matching message**: the EventHub consults its `topic → crewIds` and `mailbox → crewId` maps; for each sleeping crew that is hit, it calls `ICrewActivator.ActivateAsync(id, msg)`.
2. **Finite expiration**: `IWaitScheduler` detects an `expires_at <= now`; it calls `ActivateAsync(id, WaitTimedOutMessage)`.

No wake-up at process startup. **Lazy boot** policy: at boot, only the `pending_waits` index is reloaded into RAM; crews stay asleep until a matching message arrives. Consistent with the "the message is the only trigger" semantics.

---

## 8. `CrewSnapshot` per orchestration mode

### 8.1 Envelope

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

### 8.2 `OrchestrationState` discriminated by mode

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

Expected content per mode:

| Mode | Fields |
|---|---|
| **Sequential** | current task + results of the tasks already executed (inputs + outputs, for contextual re-injection) |
| **Hierarchical** | manager state (plan, truncated LLM history) + per worker: in-progress subtask and partial results |
| **Parallel** | per-branch status (`NotStarted`/`Running`/`WaitingForMessage`/`Done`) + join state (aggregation policy, partial results) |
| **Consensual** | voting round, votes already cast, current proposal |
| **Graph** | current node, graph state variables, cycle counter (circuit breaker), transition history |
| **Autonomous** | tree of spawned sub-agents, one `BudgetSnapshot` per agent, current depth, delegations whose response has not arrived |

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

### 8.4 Schema versioning

`SchemaVersion` is incremented on every shape change. The loader rejects unknown versions. An `ISnapshotMigrator` can perform `vN → vN+1` at load time.

---

## 9. Timeout semantics

### 9.1 `Finite` vs `Forever`

| Type | Behavior |
|---|---|
| **`WaitTimeout.Finite(d)`** | If the crew is asleep, `IWaitScheduler` schedules a wake-up at `started_at + d`. On expiration: delivery of a synthetic `WaitTimedOutMessage`. The crew handles it as a `wait_for_event` return with `timed_out: true`. |
| **`WaitTimeout.Forever`** | No scheduler entry. The crew sleeps indefinitely. Wake-up exclusively on a matching message. |

### 9.2 `WaitTimedOutMessage`

A first-class message type, delivered by the system (not by an agent) when a `WaitTimeout.Finite` expires while a crew is asleep. Characteristics:

- `SourceCrewId = CrewId.System` (reserved constant, distinct from any user `CrewId`)
- `SourceAgentId = null`
- `Topic = "_system.wait_timed_out"` (the `_system.` prefix is reserved for hub-produced messages)
- `Metadata` contains `original_topic`, `original_wait_id`, `original_started_at`
- Empty `Payload` (`ReadOnlyMemory<byte>.Empty`)

It appears in `processed_messages` for idempotence (otherwise a restart = re-delivery of the same timeout).

The `_system.` prefix is reserved: an agent cannot publish on a topic starting with `_system.` — rejected by the validation middleware.

### 9.3 Send does not support Forever

`send_request` enforces `timeout_ms`. `Forever` is forbidden. Rationale: a requester cannot be suspended indefinitely waiting for a response — the call chain would be blocked and the requesting crew's budget consumed for nothing. `Forever` is reserved for the primitives that put the crew into pure sleep (`wait_for_event`, `receive_message`, `subscribe`).

### 9.4 Budget binding

The `CancellationToken` passed to waits is derived from the crew's `AgentExecutionBudget.LinkedToken`. When the budget expires, the wait stops with `BudgetExhaustedException` — distinct from a `TimeoutException`. This lets the supervisor distinguish "no message in time" from "budget exhausted".

---

## 10. Inter-crew messaging and ACL

### 10.1 Filtering at delivery

An agent of crew Y receives, among the messages of a topic it subscribes to, those where `TargetCrewId IN (null, Y)`. Filtering happens upstream of dispatch, never on the agent side.

### 10.2 `CrewLink` — declarative authorization

```yaml
crew:
  name: billing-crew
  links:
    - to: fraud-crew
      direction: bidirectional
      allowed_topics: [fraud.check, fraud.result]
    - to: audit-crew
      direction: outbound
      allowed_topics: [audit.event]
```

The ACL middleware rejects `Post` / `Send` / scoped `Publish` calls that do not match a `CrewLink` allowing the direction and the topic. Global `Publish` calls (without `target_crew_id`) remain free; subscribers filter on the `Subscribe` side.

### 10.3 `CrewLink` direction

| Direction | Allowed direction |
|---|---|
| `outbound` | A can send to B, but not the other way around |
| `inbound` | B can send to A, but not the other way around |
| `bidirectional` | Both directions |

A `CrewLink` is checked on the **sender** side by the ACL middleware at `PublishAsync`/`PostAsync`/`SendAsync` time.

### 10.4 Ordering guarantees

| Level | Guarantee | Not guaranteed |
|---|---|---|
| Individual mailbox | Strict FIFO: messages delivered to `agent://X/Y` are received in the order of their `PublishedAt` | — |
| Topic | FIFO per topic and per subscriber: a given subscriber sees a topic's messages in order | Order across different subscribers |
| Global | — | No order guaranteed across topics, nor between a topic and a mailbox |

Consequence for heterogeneous models: if business logic depends on an order between two distinct topics, you need a "parent" topic that multiplexes, or use `CorrelationId`s to reconstruct the sequence at the application level.

---

## 11. SQLite persistence

### 11.1 Schema

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

### 11.2 Delivery idempotence

Before processing a message, the activator consults `processed_messages`. If present, the message is ignored (already processed). At the end of successful processing, insertion into `processed_messages` happens in the same transaction as the write of the new snapshot. Essential to handle mid-handle restarts.

### 11.3 Transactional outbox

When a crew processes a message and publishes one or more responses, the single transaction is:

1. `INSERT INTO processed_messages(message_id, crew_id)`.
2. `UPDATE/INSERT INTO crew_snapshots(crew_id, ...)`.
3. `INSERT INTO outbox(message_id, ...)` for each outgoing message.

A background dispatcher (`IOutboxDispatcher`) reads the outbox and pushes into the real EventHub. Guarantees that no outgoing message is lost or duplicated after a crash.

### 11.4 Purge

| Crew state | Policy |
|---|---|
| `Completed` | Immediate deletion of the snapshot + associated waits (FK cascade) |
| `Failed` | Kept N days (configurable, default 7) for audit, then deleted |
| `Asleep` | Kept as long as `pending_waits` exist; otherwise orphan GC after a configurable TTL |

`processed_messages`: retention consistent with the `crew_snapshots` retention.

---

## 12. Middleware pipeline

```csharp
public interface IEventHubMiddleware
{
    Task<Message> OnPublishAsync(Message msg, Func<Message, Task<Message>> next, CancellationToken ct);
    Task<Message> OnReceiveAsync(Message msg, Func<Message, Task<Message>> next, CancellationToken ct);
}
```

Standard v1 order:

1. **`LoggingMiddleware`** — structured logging (correlation_id, source/target crew, topic, latency).
2. **`TelemetryMiddleware`** — OTel spans with the attributes `crew.source`, `crew.target`, `event.topic`, `event.pattern`.
3. **`AclMiddleware`** — checks `CrewLink`; rejects unauthorized messages with `EventAclException`.
4. **`IdempotencyMiddleware`** — consults `processed_messages` (reception only).
5. **`ValidationMiddleware`** — validates `SchemaId` against `IEventSchemaRegistry`.

Custom middlewares are injectable via DI (`AddEventHubMiddleware<T>()`).

---

## 13. C# / YAML / TypeScript parity

### 13.1 Canonical JSON schema

Single source: `schemas/orkeon-crew-schema.json`, versioned in the repo. Used to:

- Validate the YAML at load time (`JsonSchema.Net`).
- Generate the TypeScript types (`json-schema-to-typescript`).
- Document the contract (Swagger / Redoc).

Without this single source, the three paradigms drift apart within a few weeks.

### 13.2 C# — fluent builder + DI

```csharp
var crew = new CrewBuilder()
    .Name("order-processor")
    .IdleTimeout(TimeSpan.FromMinutes(5))
    .OnEvent("order.received", ctx => ctx.Crew.Resume(ctx.Message))
    .Links(link => link
        .To("fraud-crew")
        .Bidirectional()
        .AllowedTopics("fraud.check", "fraud.result"))
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

### 13.3 YAML — declarative

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

### 13.5 Naming convention: singular `event`

The SDK property is `event` in the **singular**, not `events`, for three reasons:

- avoid the collision with the native Node module `node:events`,
- avoid confusion with a "collection of listeners" (`events: Listener[]`),
- align semantically: `agent.event.publish(...)` reads as "emit an event", not "access the list of events".

The YAML also uses `event:` (singular) for cross-paradigm consistency.

---

## 14. Observability

### 14.1 OTel spans — standard attributes

Each message produces a span with these attributes:

- `crew.source` (string)
- `crew.target` (string, null if global)
- `event.topic`
- `event.pattern` (`publish` | `post` | `send` | `reply` | `subscribe` | `wait`)
- `event.correlation_id` (if applicable)
- `event.message_id`
- `event.outcome` (`delivered` | `timeout` | `rejected_acl` | `failed`)

### 14.2 Metrics

- `orkeon_event_published_total{topic, source_crew, target_crew}` — counter
- `orkeon_event_latency_seconds{pattern}` — histogram
- `orkeon_crew_asleep_total{crew}` — gauge
- `orkeon_wait_timed_out_total{topic}` — counter
- `orkeon_pending_waits_total{kind}` — gauge

---

## 15. Implementation roadmap

| Increment | Deliverables | Exit criteria |
|---|---|---|
| **v1.0** | Complete `IEventHub` port, `Message`, `MailboxAddress`, `WaitTimeout`, `WaitDescriptor`, `InMemoryEventHub`, 7 tools, minimal C# builder | Unit tests: publish/post/send/reply, finite + forever wait, fan-out, filtering by `TargetCrewId` |
| **v1.1** | `SqliteCrewStateStore`, `SqliteEventHub` (outbox + processed_messages + pending_waits), `ICrewLifecycleManager`, `IIdleDetector`, `IWaitScheduler` | Integration tests: crash mid-handle → redelivery via outbox, sleep → wake on message, sleep → wake on timeout, lazy boot |
| **v1.2** | Complete middleware pipeline (Logging / Telemetry / Acl / Idempotency / Validation), `CrewLink` ACL, OTel | Cross-crew tests: `CrewLink` whitelist, cross-crew leak blocked, OTel spans produced |
| **v1.3** | YAML loader + JSON Schema validation, TypeScript SDK `@orkeon/sdk` with type generation | Parity tests: the same config in C#/YAML/TS produces the same runtime behavior |
| **v2.0** | `RedisStreamsEventHub` (distributed multi-node) behind the same port | Multi-process tests: publish on one side, wait on the other, ACL preserved |

---

## 16. Required tests

### 16.1 Unit (port + InMemory)

- Global `Publish` → all subscribers see it.
- `Publish` scoped to crew X → only X's subscribers see it.
- `Post` to a non-existent mailbox → typed error.
- `Send` answered within the timeout → response returned.
- `Send` not answered before the timeout → typed `TimeoutException`.
- `Reply` without a pending `Send` → error.
- Expired Finite `WaitFor` → `WaitTimedOutMessage` delivered.
- Forever `WaitFor` with no message → blocks indefinitely (test cancelled via CT).
- `WaitFor` with exhausted budget → `BudgetExhaustedException` (distinct from the timeout).

### 16.2 Integration (SQLite)

- Sleep → wake on matching message.
- Sleep → wake on finite timeout.
- Sleep Forever → wake on message, never via the scheduler.
- Crash mid-handle → redelivery via outbox at reboot.
- Idempotence: same message delivered twice → processed once.
- Lazy boot: index rebuilt, crews asleep, no spontaneous wake-up.
- Boot with an already-past `expires_at` → immediate wake-up with `WaitTimedOutMessage`.
- Sleeping long-lived subscribe → message arrival → wake-up + delivery via the next `wait_for_event` iteration.
- Mailbox FIFO: 100 messages posted in quick succession → received in exact order.
- Reserved `_system.*` topic: publish attempt by an agent → typed `ReservedTopicException` rejection.

### 16.3 Inter-crew

- `Send` crew A → crew B allowed by a `CrewLink` → delivered.
- `Send` crew A → crew B without a `CrewLink` → `EventAclException`.
- `Publish` scoped `target_crew_id: B` → a subscriber in C never receives it.
- Cross-crew leak: standard non-regression test.

### 16.4 Paradigm parity

- An identical crew config in C#/YAML/TS produces the same runtime `Crew`.
- JSON Schema validation rejects invalid YAML.
- Generated TS types cover 100% of the canonical schema.

---

## 17. Out of scope for v1 (deferred)

- **Distributed brokerage**: v2 with `RedisStreamsEventHub` or `NatsJetStreamEventHub`, same port.
- **Request-Stream**: `Send` returning `IAsyncEnumerable<Response>` for token-by-token streaming. Composable later without breaking the port.
- **Scatter-Gather**: composable on top of `Send` + `Task.WhenAll`/`WhenAny` in application-side C#. No native hub pattern.
- **DLQ**: to be plugged in via middleware when an error volume justifies it.
- **Per-agent sleep**: v1 granularity = crew. Agent-level sleep deferred.
- **Priorities**: no priority queue in v1. FIFO per mailbox.

---

## 18. Key architecture decisions (recap)

| Decision | Choice | Rationale |
|---|---|---|
| Crew wake-up | Implicit via the snapshot's `PendingWaits` | No double `wait` + `wakeup` declaration; avoids desynchronization |
| Boot after crash | Lazy (index only, crews asleep) | Consistent with "the message is the only trigger" |
| Sleep granularity | Whole crew | Simplifies the contract, avoids `IAgentLifecycleManager` |
| Forever timeout | Allowed on `wait_for_event` / `receive_message` / `subscribe` only | Send with Forever would block the call chain |
| v1 persistence | Single-node SQLite | Consistent with the existing `SqliteStateStore` / `SqliteMemoryProvider`, no external dependency |
| Distributed | Deferred to v2 behind the same port | Stability > expressiveness; allows evolving without breaking the API |
| Multi-tenant | `CrewLink` whitelist + ACL middleware | Explicit inter-crew sovereignty, defense in depth |
| Paradigms | Canonical JSON schema as single source | Avoids C# / YAML / TS drift |
| TS naming | Singular `event` | Anti-collision with `node:events`, "emit an event" semantics |
| Idempotence | `processed_messages` table + transactional outbox | Survives mid-handle crashes, publish+ack atomicity |

---

## 19. Glossary

- **Sleeping crew**: a crew with a persisted `CrewSnapshot`, runtime instances released.
- **Lazy boot**: at restart, the `pending_waits` index is reloaded without rehydrating the crews.
- **PendingWait**: a crew's in-flight wait, captured in its snapshot, used as an implicit wake-up trigger.
- **CrewLink**: ACL declaration of a messaging authorization between two crews.
- **LastValueCache**: 1-key/1-value cache scopable per crew, fed via `PublishOptions.RetainAsLastValue`.
- **Outbox**: SQLite buffer table for outgoing messages, guaranteeing publish+ack atomicity.
- **Forever**: infinite timeout, allowed on `wait_for_event`, `receive_message` and `subscribe`. Wake-up exclusively on a matching message.
- **MailboxAddress**: structured URI identifying a mailbox (`agent://`, `crew://`, `topic://`).
- **`WaitTimedOutMessage`**: system message delivered to a crew when its `WaitTimeout.Finite` expires while it sleeps. Reserved topic: `_system.wait_timed_out`.
- **`CrewId.System`**: reserved constant representing the hub itself as the sender. Used as the `SourceCrewId` for all system messages (timeouts, lifecycle notifications). No user crew can take this ID.
- **Reserved `_system.*` topic**: prefix forbidden to agents. Only the hub can publish on these topics. Any attempt is rejected by the validation middleware.

---

## 20. References

- `docs/architecture/raggable-tree.md` — the reference Clean Architecture port/adapter pattern in Orkéon.
- `src/core/Orkeon.Domain/Autonomous/AgentExecutionBudget.cs` — multi-dimensional budget used for budget-aware `CancellationToken`s.
- `src/core/Orkeon.Infrastructure/Communication/InMemoryAgentChannel.cs` — existing lock-free communication pattern, to be reused for `InMemoryEventHub`.
- `src/core/Orkeon.Infrastructure/Memory/EncryptedSqliteMemoryProvider.cs` — reference for `SqliteCrewStateStore`.
