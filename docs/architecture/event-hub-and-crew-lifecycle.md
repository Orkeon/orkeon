> 🇫🇷 [Version française](../fr/architecture/event-hub-and-crew-lifecycle.md)

# EventHub & Crew Lifecycle — Reference Specification

**Scope**: `Orkeon.Application` (ports), `Orkeon.Infrastructure` (adapters)
**Audience**: Orkeon developers

This document consolidates the architectural decisions made for Orkeon's inter-agent and inter-crew messaging layer, as well as for putting crews to sleep and waking them up.

> **It is a specification, and it describes more than the repository currently contains.** What ships: the `IEventHub` port and its in-memory adapter, the seven agent tools, the middleware pipeline and its five stages (§12), the `links:` grammar and the ACL (§10). What does not: SQLite persistence (§11), crew sleep and wake-up (§7, §8), and the JSON-Schema-driven parity toolchain (§13.1). Each section that describes something unbuilt says so on the spot — read those notes as part of the contract.

---

## 1. Goals and out of scope

### 1.1 Goals

- Provide agents and the system with a unified in-memory messaging primitive.
- Allow an inactive crew to be put to sleep and woken up later upon arrival of an expected message.
- Guarantee that messages and in-flight waits survive a process restart (optional SQLite persistence).
- Expose the same functional surface across Orkeon's three paradigms: C#, YAML, TypeScript.
- Support inter-crew messaging secured by a declarative whitelist.

### 1.2 Out of scope for v1

- Distributed multi-node brokerage (Redis Streams, NATS) — deferred to v2 behind the same port.
- Advanced patterns: Request-Stream, Scatter-Gather, priorities, DLQ — composable on top of the port when the need arises.
- Sleep at the individual agent level — v1 granularity = whole crew.

---

## 2. Overview

```
                      Agent (heterogeneous LLM)
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
    // Broadcast 1→N (anonymous, topic-based)
    Task PublishAsync(
        string topic,
        object payload,
        PublishOptions? options,
        CancellationToken ct);

    // Fire-and-forget 1→1 (to an addressed mailbox)
    Task PostAsync(
        MailboxAddress to,
        object payload,
        CancellationToken ct);

    // Request-response 1→1 (mandatory timeout — no Forever here)
    Task<TResponse> SendAsync<TRequest, TResponse>(
        MailboxAddress to,
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct);

    // Response to a pending Send (internal correlation)
    Task ReplyAsync(
        CorrelationId correlation,
        object payload,
        CancellationToken ct);

    // Long-lived subscription (automatically filtered by TargetCrewId)
    IAsyncEnumerable<Message> SubscribeAsync(
        string topic,
        CancellationToken ct);

    // Short-term wait on topic / mailbox / correlation (Finite or Forever)
    Task<Message> WaitForAsync(
        WaitDescriptor descriptor,
        WaitTimeout timeout,
        CancellationToken ct);

    // Last retained value (optional per-crew scope)
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
    public bool RetainAsLastValue { get; init; }                  // feeds the LastValueCache
    public string? LastValueKey { get; init; }
    public string? SchemaId { get; init; }                        // engages the §12.3 validation stage
}

public abstract record WaitDescriptor;

public sealed record WaitOnTopic(
    string Topic,
    ImmutableDictionary<string, string>? MetadataMatch) : WaitDescriptor;

public sealed record WaitOnMailbox(MailboxAddress Address) : WaitDescriptor;

public sealed record WaitOnReply(CorrelationId Correlation) : WaitDescriptor;

public abstract record WaitTimeout;

public sealed record FiniteWaitTimeout(TimeSpan Duration) : WaitTimeout;
// FiniteWaitTimeout.Of(TimeSpan) validates Duration > 0

public sealed record ForeverWaitTimeout : WaitTimeout
{
    public static readonly ForeverWaitTimeout Instance = new();
}
```

### 3.3 Associated components (other ports)

```csharp
// Facade orchestrating the lifecycle transitions.
// Called by the IIdleDetector (sleep) and the IEventHub (wake on message / timeout).
// Delegates the actual activation to ICrewActivator.
public interface ICrewLifecycleManager
{
    Task SleepAsync(CrewId id, SleepReason reason, CancellationToken ct);
    Task<Crew> WakeAsync(CrewId id, Message trigger, CancellationToken ct);
}

// Raw snapshot persistence. No business logic.
public interface ICrewStateStore
{
    Task SaveAsync(CrewId id, CrewSnapshot snapshot, CancellationToken ct);
    Task<CrewSnapshot?> LoadAsync(CrewId id, CancellationToken ct);
    Task DeleteAsync(CrewId id, CancellationToken ct);
}

// Concrete activation implementation: loads from ICrewStateStore,
// rebuilds the Agents and Mailboxes, restores the budget and the OrchestrationState,
// then delivers the triggering message. Not called directly by the EventHub —
// always goes through ICrewLifecycleManager.WakeAsync.
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
    // Retrieves the JSON schema registered for a given SchemaId
    Task<JsonNode?> GetAsync(string schemaId, CancellationToken ct);

    // Registers a (versioned) schema. Rejected if the SchemaId is already registered with a different schema.
    Task RegisterAsync(string schemaId, JsonNode schema, CancellationToken ct);
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

Seven tools, registered by the explicit `AddOrkeonEventHubTools()` alongside the hub (`AddOrkeonInMemoryEventHub()` — the runner host pairs the two calls; nothing is automatic). All follow `ToolBase<TRequest, TResponse>` with sealed DTOs and snake_case JSON.

| Tool | Pattern | Input | Output |
|---|---|---|---|
| `publish_event` | Publish | `topic`, `payload`, `target_crew_id?`, `metadata?`, `retain_as_last_value?`, `last_value_key?` | `event_id`, `published_at` |
| `post_message` | Post | `target_mailbox`, `payload`, `metadata?` | `message_id`, `posted_at` |
| `send_request` | Send | `target_mailbox`, `payload`, `timeout_ms` (required), `metadata?` | `correlation_id`, `response_payload` or `TimeoutError` |
| `reply_to` | Reply | `correlation_id`, `payload` | `replied_at` |
| `receive_message` | (pull mailbox) | `timeout_ms` or `wait_forever:true`, `mailbox?` (default: current agent) | `message?` |
| `wait_for_event` | Wait | `topic`, `timeout_ms` or `wait_forever:true`, `metadata_match?` | `message` or `timed_out:true` |
| `get_last_value` | LastValueCache | `key`, `crew_scope?` (default: current crew) | `found`, `value`, `set_at` (`found` separates a cached null from an absent key) |

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
    public AgentId? SourceAgentId { get; init; }                  // null = system
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
agent://{crewId}/{agentId}      mailbox of a specific agent
crew://{crewId}                  crew mailbox (internal router)
topic://{topicName}              alias of a broadcast topic
client://{name}                  a watching client process (see §10.2.1 and the run event bus)
```

Validated by regex in `MailboxAddress.Parse`, the port's single entry point (there is no crew-builder path for addresses, and the TS SDK of §13.4 is a not-built design). `TargetCrewId` is extracted automatically from the `TargetMailbox`; any inconsistency between the two is rejected by the middleware.

---

## 7. Crew lifecycle

```
                 ┌─────────┐
                 │ Active  │ ◄── startup / restoration
                 └────┬────┘
                      │ idle_timeout exceeded
                      │ AND the crew is in WaitingForMessage
                      ▼
                 ┌─────────┐
                 │Snapshot.│  atomic write of CrewSnapshot + pending_waits
                 └────┬────┘
                      ▼
                 ┌─────────┐
                 │ Asleep  │  ── survives process restarts ──
                 └────┬────┘
                      │ message matching a pending_wait
                      │   OR finite expiration reached (scheduler)
                      ▼
                 ┌─────────┐
                 │Activat. │  ICrewActivator loads snapshot, recomposes Crew
                 └────┬────┘
                      ▼
                  Active (delivers the triggering message to the agent)
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

The `_system.` prefix is reserved: an agent cannot publish on a topic starting with `_system.` — the **`publish_event` tool** throws `ReservedTopicException` before the hub is reached (the hub itself, and thus in-process callers, are not guarded; the validation middleware only checks `SchemaId`).

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
name: billing-crew
links:
  - to: fraud-crew
    direction: bidirectional
    allowed_topics: [fraud.check, fraud.result]
  - to: audit-crew
    direction: outbound
    allowed_topics: [audit.event]
  - to: "client:studio"        # an external peer, not a crew — see below
    direction: bidirectional
```

`links:` sits beside `name:` and `agents:` — at the root of a single-file crew YAML, or in `crew.yaml` of the multi-file layout. Declaring it in the fluent C# builder is **not** possible: links reach the ACL through a crew's YAML and `CrewFactory`.

`to:` names the target crew by its **`name:`**, matched verbatim — the only identity a YAML author has when writing the block, since crew ids are minted at creation time. Every crew registers its name with the ACL at creation, even one that declares no links: other crews' links have to be able to name it.

The ACL stage rejects `Post` / `Send` / scoped `Publish` calls that no `CrewLink` authorizes. Four rules, each with its reason:

| Case | Decision | Why |
|---|---|---|
| Message with **no target** (global `Publish`, or a `topic://` mailbox) | passes | a broadcast is an offer, not a delivery; subscribers filter on their own side |
| Crew that **never declared** a `links:` block | passes by default | the hub shipped without any ACL, so refusing undeclared traffic the day the stage is switched on would break every existing crew. A deployment that wants a closed door registers `RestrictiveCrewLinkPolicy` — and under it, an undeclared sender still gets through when the *target* granted it an `inbound` link (§10.3) |
| Crew that **did** declare links | held to them — a declared-but-empty block included, which refuses everything | declaring is the act that closes the door; a block whose entries all failed to parse must close it, never open it |
| Crew whose declared link matches | passes | for a crew-scoped publish, the link must name the target **and** authorize the topic; for `Post`/`Send`, the link itself authorizes — see below |

An **empty `allowed_topics`** authorizes every topic: a link that authorized nothing would be pointless, so the empty case is trust rather than an accident. The list constrains **topics only**: point-to-point mail (`Post`/`Send`) carries a synthetic hub topic no author could name, so it is authorized by the link itself — direction and target. An entry with no `to:`, or with a `direction:` nobody can read, is **dropped with a warning** rather than guessed at — a guessed direction is an authorization the author never wrote — and the block's presence still closes the door: a malformed authorization must never become a permissive one.

One YAML shape escapes that rule, and it is stated rather than hidden: a **bare `links:` key with no value** deserializes to null, indistinguishable from an absent block, and therefore reads as *never declared*. To close the door explicitly, write `links: []` — the empty list is a real declaration, and it refuses everything.

### 10.2.1 Naming an external peer

`CrewLink` authorizes a crew to talk to a crew. A process outside the hub — Studio watching a run, a gateway — is not one, and it reaches the hub through the `client://{name}` mailbox scheme. A link names it with the reserved `client:` prefix, as in the example above.

Without that, an external peer would escape the ACL simply by not being modelled. This is the one place the implementation extends this specification rather than applying it.

### 10.3 `CrewLink` direction

| Direction | Allowed direction |
|---|---|
| `outbound` | A can send to B, but not the other way around |
| `inbound` | B can send to A, but not the other way around |
| `bidirectional` | Both directions |

A `CrewLink` is checked on the **sender** side, at `PublishAsync` / `PostAsync` / `SendAsync` time — mailbox traffic included, which matters because `client://` is only ever reached that way. The sender's own declaration rules its outbound traffic: an `outbound` or `bidirectional` link grants it, an `inbound` link does not.

`inbound` is a **grant**: "the named peer may send to me". It is consulted on the *target's* declarations when the sender itself never declared a `links:` block and the deployment refuses undeclared traffic — which is exactly when the keyword matters. It never reopens a door the sender closed itself: a crew that declared links not naming the target stays refused, whatever the target grants.

Authorization happens **once**, on the send: the receive path deliberately does not check again, since re-checking would refuse a message that already passed. One assumption travels with that decision: **the reader of a mailbox is its owner.** The hub auto-registers waiters and reading is destructive, so `receive_message` only accepts mailboxes of the calling crew — without that restriction, any agent could siphon `client://studio`, the very traffic the write-side ACL guards.

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
    snapshot       BLOB NOT NULL,                -- serialized JSON
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

The runner is `EventHubMiddlewarePipeline`. Order is the registration order on the publish path and the reverse on the receive path, so a stage wraps a message symmetrically going in and coming out. A stage short-circuits by **throwing**; the pipeline does not catch, because a refusal has to reach the caller.

The order below is not cosmetic: on the **publish path** logging comes first so it sees everything a later stage rejects, and validation last because it is the only stage that consults a store. On the receive path the chain runs in reverse by construction, so observability sits innermost there — a message a receive stage refuses (an idempotency duplicate) is dropped before logging would fire; the hub's own Debug line covers that case.

| # | Stage | Registration | What it does |
|---|---|---|---|
| 1 | `LoggingEventHubMiddleware` | `AddOrkeonEventHubObservability()` | structured logging: path, topic, message id, elapsed ms |
| 2 | `TelemetryEventHubMiddleware` | idem | OTel spans on the `Orkeon.EventHub` source, attributes `crew.source`, `crew.target`, `event.topic`, `event.pattern` |
| 3 | `AclEventHubMiddleware` | `AddOrkeonEventHubAcl(policy?)` | checks `CrewLink` on publish; refuses with `EventAclException` (§10) |
| 4 | `IdempotencyEventHubMiddleware` | `AddOrkeonEventHubIdempotency(capacity?)` | refuses a message a mailbox already consumed, on receive |
| 5 | `ValidationEventHubMiddleware` | `AddOrkeonEventHubValidation()` | checks that a declared `SchemaId` names a registered contract, on publish |

Every stage is opt-in — a hub nobody watches pays nothing — and custom stages are injectable with `AddEventHubMiddleware<T>()`.

### 12.1 Where the stages run

The publish stages run on `Publish`, **on `Post` and `Send`**, and **on `Reply`**: mailbox traffic has to travel them, since the ACL guards who may reach a mailbox and `client://` — the one address that leaves the process — is only ever reached that way. A reply is hub traffic like any other — `reply_to` and the client bridge both reach it from outside the process, and an unlogged, unspanned reply would be the one message nobody observes. One deliberate exemption: the ACL does not *authorize* a reply. A reply is addressed by its correlation id — a ULID minted by the requester and handed only to whoever received the request — so possession of the id **is** the authorization, the way a capability works; a link-based check would need a "who may answer me" grammar nobody has asked for.

The receive stages run where a recipient *consumes* a message: draining a subscription, a wait on a topic, a wait on a mailbox. Awaiting a `Send` reply does not go through them — that is the tail of an exchange whose reply already travelled the publish stages, and it resolves a `TaskCompletionSource` rather than draining a channel.

### 12.2 Idempotency guards point-to-point delivery only

A message addressed to a mailbox has exactly one legitimate reader, so "already processed?" is a well-posed question there. A topic message legitimately reaches **every** subscriber, and deduplicating it by identifier would starve all but the first — a bug that would look like a feature. Topic traffic therefore passes untouched.

The middleware contract can only pass a message on or stop it by throwing, so "drop this duplicate" is said with `DuplicateMessageException`, which the hub catches at the delivery site. A consumer never sees it.

> **The memory does not survive the process.** It is a bounded set of identifiers in the hub's own process — which is coherent, since the hub is in-memory too. A restart clears it and a replayed message would be processed again. That is a stated limit: durable idempotence belongs with the durable hub of §11, and the port absorbs the change the day it exists.

### 12.3 What validation validates

That the declared contract **exists** in `IEventSchemaRegistry` — not that the payload conforms to it. No JSON Schema engine ships here, and half of one would look like a guarantee while being none. What the stage does catch is real: a typo in a schema id, or an event type the deployment never declared.

A message carrying `Message.NoDeclaredSchemaId` (`"_none"` — deliberately not a plausible real id: a deployment could legitimately register `application/json`, and a colliding sentinel would be silently unchecked) declares no contract at all — every `Post`, `Send` and `Reply` does — and passes. Declaring a schema is what engages the check, the same way declaring a link closes the ACL's door. The refusal is raised on **publish**, because a subscriber cannot fix a schema someone else declared.

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
// Not built: .IdleTimeout and .Links do not exist on CrewBuilder, and the bindings
// recorded by .OnEvent are not dispatched to by anything. Declare links in YAML
// (§10.2); consume the hub directly, as below.
var crew = new CrewBuilder()
    .Name("order-processor")
    .Build();

// Direct consumption for user code
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

### 13.3 YAML — declarative (design shape, not the shipped grammar)

The **shipped** grammar is the flat-root `links:` block (see [the YAML schema](./yaml-schema.md)); the `crew:`-rooted shape below, with `idle_timeout` and the `event:` blocks, belongs to this section's not-built toolchain design:

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
  event: {                              // singular, tied to the EventHub
    publish: [
      { topic: 'order.processed', targetCrewId: 'billing-crew' },
      { topic: 'audit.event' }
    ],
    subscribe: [
      { topic: 'order.received', deliverTo: 'agent://order-processor/router' }
    ],
  },
});

// Custom agent side
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

What `TelemetryEventHubMiddleware` sets **today** (four attributes):

- `event.topic`
- `event.pattern` (`publish` | `receive` — the only two values emitted)
- `crew.source`
- `crew.target` (only when non-null)

Designed but **not built**: `event.correlation_id`, `event.message_id`,
`event.outcome`, and the finer pattern vocabulary (`post`/`send`/`reply`/
`subscribe`/`wait`).

### 14.2 Metrics — **not built**

No EventHub instrument exists yet (`OrkeonMetrics` covers `orkeon.llm.*`,
`orkeon.tool.*`, `orkeon.task.*`, `orkeon.crew.*` only). The designed set, kept
here as the target:

- `orkeon_event_published_total{topic, source_crew, target_crew}` — counter
- `orkeon_event_latency_seconds{pattern}` — histogram
- `orkeon_crew_asleep_total{crew}` — gauge
- `orkeon_wait_timed_out_total{topic}` — counter
- `orkeon_pending_waits_total{kind}` — gauge

---

## 15. Implementation roadmap

**Built.** The `IEventHub` port, `PublishOptions`/`WaitDescriptor`, `IEventSchemaRegistry`, `InMemoryEventHub`, the seven agent tools, the `links:` grammar and the ACL (§10), and the whole middleware pipeline with its five stages (§12) — logging, telemetry, ACL, idempotency, validation. (The §3.3 lifecycle interfaces — `ICrewLifecycleManager`, `ICrewStateStore`, `ICrewActivator`, `IIdleDetector`, `IWaitScheduler` — are design, listed as missing below.)

**Not built**, each behind the same port so that building it changes no caller:

| Missing | What it would bring | Where it is described |
|---|---|---|
| Durable hub and state store | messages and in-flight waits surviving a restart; idempotency that outlives the process | §11 |
| Crew sleep and wake-up | `ICrewLifecycleManager`, `IIdleDetector`, `IWaitScheduler`, snapshots | §7, §8 |
| Canonical JSON schema toolchain | YAML validated and TypeScript types generated from one source | §13.1 |
| Distributed brokerage | multi-node, publish on one side and wait on the other, ACL preserved | §1.2 |

Two smaller inertias worth naming rather than discovering: `CrewBuilder.OnEvent` records bindings nothing dispatches, and the fluent builder cannot declare a `CrewLink` at all.

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
- **MailboxAddress**: structured URI identifying a mailbox (`agent://`, `crew://`, `topic://`, `client://`).
- **`WaitTimedOutMessage`**: system message delivered to a crew when its `WaitTimeout.Finite` expires while it sleeps. Reserved topic: `_system.wait_timed_out`.
- **`CrewId.System`**: reserved constant representing the hub itself as the sender. Used as the `SourceCrewId` for all system messages (timeouts, lifecycle notifications). No user crew can take this ID.
- **Reserved `_system.*` topic**: prefix forbidden to agents. Only the hub can publish on these topics. Any attempt is rejected by the validation middleware.

---

## 20. References

- `docs/architecture/raggable-tree.md` — the reference Clean Architecture port/adapter pattern in Orkeon.
- `src/core/Orkeon.Domain/Autonomous/AgentExecutionBudget.cs` — multi-dimensional budget used for budget-aware `CancellationToken`s.
- `src/core/Orkeon.Infrastructure/Communication/InMemoryAgentChannel.cs` — existing lock-free communication pattern, to be reused for `InMemoryEventHub`.
- `src/core/Orkeon.Infrastructure/Memory/EncryptedMemoryProviderDecorator.cs` — reference for `SqliteCrewStateStore`.
