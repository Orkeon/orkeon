> 🇫🇷 [Version française](../fr/architecture/memory-system.md)

# Memory System

## Interface and types

`IMemoryProvider` (`Orkeon.Domain.Memory`) defines the contract with seven methods: `StoreAsync`, `GetAsync`, `SearchAsync` (text search), `DeleteAsync`, `ClearAsync`, `StoreWithEmbeddingAsync` and `SearchSimilarAsync` (vector search by cosine similarity). The last two carry default interface bodies (`SearchSimilarAsync`'s returns nothing), so every in-repo provider derives from `MemoryProviderBase` (`Orkeon.Infrastructure.Memory.Base`), which re-declares `SearchSimilarAsync` as **abstract** — a provider that forgot to implement vector search would otherwise silently return zero results through the interface. The base class also adds `UpdateAsync`, `CountAsync` and `ListKeysAsync`. There is no initialization step: a provider receives its options in its constructor and opens any connection on its first call.

### Metadata filter

`SearchAsync(query, limit, filter, cancellationToken)` and `SearchSimilarAsync` take the same optional
`filter` (`Dictionary<string, object>`): `source` is an equality on the item's source, `tag`/`tags` a
membership in its tags, and any other key an equality on the item's **custom property** of that name.
Every provider applies it **before** its limit — a filtered search returns every match the store holds,
up to the limit, never fewer because entries the filter rejects filled the page:

| Provider | How the filter is applied |
|---|---|
| In-Memory | In memory, on every item, before the limit; values compared case-insensitively |
| Redis | On each item of the key-space scan, which stops at the limit; values compared case-insensitively |
| SQLite | Matching rows read newest first and checked one by one until the limit; values compared case-insensitively |
| ChromaDB | The request's `where` clause (`$eq` per key, under one `$and`) — of the vector query, and of the `/get` that serves a text search; exact values |
| Pinecone | The vector query's metadata filter (`$eq` per key); exact values. Pinecone has no text search: its `SearchAsync` throws `NotSupportedException` |
| LanceDB | The query's SQL predicate, prefiltered; a custom property matches as its `"key":"value"` pair in `metadata_json` (exact, `LIKE` wildcards in values match loosely) |
| `EncryptedMemoryProviderDecorator` | Passed to the wrapped provider: metadata is stored in clear |

ChromaDB and Pinecone keep an item's custom properties in its metadata — in the default collection or
namespace as in the named ones — and give them back on read, so a filter on a custom property finds it.

Five memory types are defined by `MemoryType` (`Orkeon.Domain.Memory`): `ShortTerm` (immediate context), `LongTerm` (persistent information), `Episodic` (event sequences), `Entity` (information about specific entities), `Procedural` (learned skills).

### Optional capabilities

Beyond the base contract, a provider opts into capability interfaces (`Orkeon.Domain.Memory`) when its backend honours them natively:

| Capability | Members | Implemented by |
|---|---|---|
| `IScoredVectorSearch` | `SearchSimilarWithScoresAsync` (key + score per hit) | In-Memory, SQLite, ChromaDB, Pinecone, LanceDB |
| `IBatchUpsert` | `UpsertBatchAsync` | In-Memory, SQLite |
| `IHybridSearchCapable` | `HybridSearchAsync` (vector + full-text in the store) | LanceDB |
| `ICollectionAwareMemory` | collection-scoped store / upsert / search / `DeleteByFilterAsync` / `DropCollectionAsync` | ChromaDB (collections), Pinecone (namespaces), LanceDB (tables) |

Discover them with `provider.TryGetCapability<TCapability>(out var capability)` (`MemoryCapabilityExtensions`), not with a bare `is` test: a decorator such as `EncryptedMemoryProviderDecorator` statically implements every capability and reports, through `IMemoryCapabilityProbe`, the ones its wrapped provider really has. Consumers fall back when a capability is missing — the RAG subsystem, for instance, uses prefixed keys (`rag:{collection}:…`) on a provider without `ICollectionAwareMemory` and its in-process BM25 + RRF fusion on one without `IHybridSearchCapable`.

## Implementations

| Provider | Class | Characteristics |
|----------|-------|-----------------|
| In-Memory | `InMemoryProvider` | `ConcurrentDictionary`, cosine vector search, development/tests |
| Redis | `RedisMemoryProvider` | Keys prefixed `orkeon:memory:` by default, Polly retry policy (`ResiliencePolicies.GetRedisRetryPolicy`), camelCase JSON serialization. Options `RedisMemoryOptions` (`Orkeon:Redis`: `ConnectionString`, default `localhost:6379`; `KeyPrefix`). The connection opens on the first call, once, whatever the number of concurrent callers; an unreachable server surfaces as a `RedisConnectionException` on that call and the next call tries again |
| SQLite | `SqliteMemoryProvider` | `Microsoft.Data.Sqlite`, local persistence (file or `:memory:`, default `Data Source=:memory:`), embeddings stored as BLOB, `LIKE` full-text search + cosine vector search (in-memory scan), identifiers and metadata preserved on read-back. A file `Data Source` is a **virtual path** resolved through the VFS and must lie on a writable mount (e.g. `Data Source=/output/orkeon-memory.db`), otherwise the constructor throws `FileAccessDeniedException` |
| ChromaDB | `ChromaDbMemoryProvider` | REST API v2 (tenant/database routes), vector database. A record is added with its embedding only — `StoreAsync` refuses an item without one, before any request. A text search (`SearchAsync`) is a `/get` of the documents that contain the query (`where_document` `$contains`, case-sensitive as the server matches it), within the filter; an empty query sends the filter alone. The HTTP API has no text query: `query_texts` exists only in the clients, which embed it themselves |
| Pinecone | `PineconeMemoryProvider` | Cloud vector database — `Api-Key` header. Requests go to the index host: `Orkeon:Pinecone:Host` when set, otherwise the `host` Pinecone returns for `IndexName` from one `describe_index` call (`GET https://api.pinecone.io/indexes/{IndexName}`) made on first use. A vector requires values: `StoreAsync` refuses an item without an embedding, before any request. Search by vector (`SearchSimilarAsync`): `SearchAsync` throws `NotSupportedException` — there is no text search to serve it |
| LanceDB | `LanceDbMemoryProvider` | Remote LanceDB Cloud/Enterprise server — REST + Arrow IPC, **server-side** vector and full-text search |

## LanceDB (real remote integration)

`LanceDbMemoryProvider` targets a **LanceDB Cloud / Enterprise** server via the
[Lance REST Namespace](https://docs.lancedb.com/api-reference/rest/) protocol
(raw `HttpClient`, no SDK). There is **no local store anymore**: the old
"JSON file (+gzip) on the VFS with local cosine scoring" implementation has been
replaced (decision R4.11 — implement LanceDB for real).

### Protocol

| Provider operation | REST endpoint | Payload |
|---|---|---|
| First call (table auto-created) | `POST /v1/table/{t}/exists`, `POST /v1/table/{t}/create`, `POST /v1/table/{t}/create_index` (FTS) | JSON / Arrow IPC stream |
| `StoreAsync` / `UpdateAsync` (upsert) | `POST /v1/table/{t}/merge_insert?on=id&when_matched_update_all=true&when_not_matched_insert_all=true` | Arrow IPC stream |
| `GetAsync` / `ListKeysAsync` | `POST /v1/table/{t}/query` (SQL filter / `k`+`offset` pagination) | JSON → Arrow IPC file |
| `SearchAsync` (BM25 full-text) | `POST /v1/table/{t}/query` with `full_text_query` | JSON → Arrow IPC file |
| `SearchSimilarAsync` (vector) | `POST /v1/table/{t}/query` with `vector.single_vector` + `distance_type` | JSON → Arrow IPC file |
| `DeleteAsync` / `ClearAsync` | `POST /v1/table/{t}/delete` (SQL predicate) | JSON |
| `CountAsync` | `POST /v1/table/{t}/count_rows` | JSON → raw integer |

Authentication uses the `x-api-key` header (+ optional `x-lancedb-database`).
Data payloads are encoded/decoded as **Arrow IPC** via the `Apache.Arrow`
package (Apache-2.0, cf. `THIRD-PARTY-NOTICES.md`). The table schema is
fixed: `id`, `content`, `vector` (`FixedSizeList<float32>[dim]`), `importance`,
`source`, `tags` (JSON), `created_at`, `metadata_json`.

### Configuration

```json
{
  "Orkeon": {
    "LanceDb": {
      "Endpoint": "https://my-deployment.us-east-1.api.lancedb.com",
      "ApiKey": "…",
      "Database": "optional",
      "TableName": "orkeon_memories",
      "EmbeddingDimension": 1536,
      "DistanceType": "cosine",
      "CreateFullTextIndexOnInit": true
    }
  }
}
```

Other keys: `DefaultTopK` (10), `MinSimilarityScore` (0), `VectorWeight` / `FullTextWeight`
(hybrid fusion weights, 0.7 / 0.3). Defaults of the keys shown: `TableName` `orkeon_memories`,
`EmbeddingDimension` 1536, `DistanceType` `cosine`, `CreateFullTextIndexOnInit` `true`.

Every path that selects `lancedb` reads this section (see [Selection by configuration](#selection-by-configuration)).
`services.AddOrkeonLanceDb(configuration)` also exposes the shared `LanceDbMemoryProvider` by its
class, with `LanceDbMigrationService`; it does not rebind `IMemoryProvider`. Without `Endpoint`,
resolving that class throws `InvalidOperationException`; selecting `lancedb` by type logs a warning
and falls back to In-Memory (see below).

### Known limitations

- **Full-text**: `SearchAsync` requires an FTS index on `content`. The provider tries
  to create it at table creation time (`CreateFullTextIndexOnInit`); if creation
  fails, a warning is logged and the server error is propagated as-is
  to subsequent `SearchAsync` calls (no local simulation).
- **Hybrid**: the `query` endpoint does not perform the vector+full-text fusion in a
  single call; `HybridSearchAsync` issues **two server requests** (`_distance`
  and `_score` rankings computed server-side) then locally merges the two ranked lists
  with `VectorWeight`/`FullTextWeight` — the same approach as the rerankers in the
  official LanceDB SDKs.
- **Scores**: the returned similarity is `1 − _distance`, meaningful for the
  `cosine` metric (default); for `l2`/`dot` the scale differs.
- **`DeleteAsync`/`UpdateAsync`**: the delete API returns a commit version, not a
  counter — the provider first checks that the key exists (1 extra `query`
  request) to preserve the boolean contract.
- **Metadata filters**: `source` translates into a SQL equality; `tag`/`tags` into
  `LIKE '%…%'` over the tags JSON; a custom key into `LIKE '%"key":"value"%'` over
  `metadata_json`, the pair spelled the way the provider's JSON encoder wrote it — the key
  and the whole value, case-sensitive. The SQL wildcard characters `%`/`_` in a value still
  match broadly.
- **Fixed dimension**: an embedding whose size differs from `EmbeddingDimension`
  raises an explicit `InvalidOperationException` (the server schema is frozen).

## ChromaDB — supported version and configuration

`ChromaDbMemoryProvider` targets ChromaDB's **REST API v2**
(`/api/v2/tenants/{tenant}/databases/{database}/collections/...`), that is,
**ChromaDB servers ≥ 0.6.x, including the 1.x series**. The legacy
`/api/v1` routes have been removed server-side (HTTP 410 response) and are no longer used
by the provider — servers ≤ 0.5.x (v1 only) are therefore **not supported**.

The targeted tenant and database are configurable via `ChromaDbOptions`
(configuration section `Orkeon:ChromaDb`) and default to
`default_tenant`/`default_database`, the values created out of the box by a
single-tenant ChromaDB server. A non-default tenant or database must already exist on
the server (the provider does not create them; only the collection is created on the fly
via `get_or_create`).

```json
{
  "Orkeon": {
    "ChromaDb": {
      "BaseUrl": "http://localhost:8000",
      "Tenant": "default_tenant",
      "Database": "default_database",
      "CollectionName": "orkeon_memories",
      "DefaultTopK": 10
    }
  }
}
```

The provider also exposes `HeartbeatAsync()`, which probes the server's liveness via
`GET /api/v2/heartbeat` and returns `false` (without throwing) if the server is unreachable
or responds with an error.

## Selection by configuration

### One section per provider

The host configures each provider once, in its own section; everything else names a **type**:

| Type | Aliases | Section | Keys |
|---|---|---|---|
| `inmemory` | `in-memory`, empty | — | — |
| `redis` | | `Orkeon:Redis` | `ConnectionString` (default `localhost:6379`, any StackExchange.Redis string), `KeyPrefix` (default `orkeon:memory:`) |
| `sqlite` | | `Orkeon:Sqlite` | `ConnectionString` (default `Data Source=:memory:`, a file `Data Source` is a virtual path on a writable mount, e.g. `Data Source=/output/orkeon-memory.db`), `TableName` (identifier validated against SQL injection), `DefaultTopK`, `MinSimilarityScore` |
| `chromadb` | `chroma` | `Orkeon:ChromaDb` | `BaseUrl` (default `http://localhost:8000`), `Tenant`, `Database`, `CollectionName`, `DefaultTopK` |
| `pinecone` | | `Orkeon:Pinecone` | `ApiKey`, `IndexName` (default `orkeon-memories`), `Host` (optional, see the table above), `Namespace` (default `default`) |
| `lancedb` | `lance` | `Orkeon:LanceDb` | `Endpoint` (**required**: without it, explicit warning and In-Memory fallback), `ApiKey`, `TableName`, `Database`, … ([above](#configuration)) |

```json
{
  "Memory": { "Provider": "redis" },
  "Orkeon": {
    "Redis": { "ConnectionString": "redis.internal:6379,password=…", "KeyPrefix": "team-a:" },
    "Sqlite": { "ConnectionString": "Data Source=/output/orkeon-memory.db" }
  }
}
```

Secrets stay on the host: a crew file never carries a connection string or an API key.

`MemoryProviderFactory` (port `IMemoryProviderFactory`, `GetProvider(type)`, case-insensitive)
hands out **one instance per type**: the application-wide provider, every crew naming that type
and the RAG store of that type share it — one Redis connection, one HTTP client, one SQLite
connection. The factory owns these instances and disposes them with the container. Creating a
provider never connects; the connection opens on its first call. An unknown type falls back to
In-Memory with an explicit warning; `SupportedTypes` lists the aliases.

### The application-wide provider

`AddOrkeonInfrastructure()` binds the five sections and registers `IMemoryProviderFactory` and the
`IMemoryProvider` singleton, whose type is `Memory:Provider` (unset → In-Memory). `Memory:Provider`
holds the type only; the connection is the section of the chosen provider. It is also where the memory
of a named crew that declares no `memoryProvider:` lives ([below](#a-crews-memory-provider-and-scope)).

### Dependency-injection extensions

| Extension | Registers |
|---|---|
| `AddOrkeonInfrastructure()` | The five sections, `IMemoryProviderFactory` + the `IMemoryProvider` singleton above |
| `AddOrkeonRedisMemory(configuration)` | Binds `Orkeon:Redis` from `configuration`, exposes the shared `RedisMemoryProvider` by its class and **rebinds** `IMemoryProvider` to it — the same as `Memory:Provider = redis` |
| `AddOrkeonChromaDb(configuration)` | Binds `Orkeon:ChromaDb` and exposes the shared `ChromaDbMemoryProvider` by its class (HTTP client from `IHttpClientFactory`). Called by `AddOrkeonInfrastructure(configuration)` when the section exists |
| `AddOrkeonPinecone(configuration)` | Same shape on `Orkeon:Pinecone` (`ApiKey`, `IndexName`, `Host`, `Namespace`) |
| `AddOrkeonLanceDb(configuration)` | Same shape on `Orkeon:LanceDb`, plus `LanceDbMigrationService` (`MigrateToLanceDbAsync`, copies an existing provider into the LanceDB table) |
| `AddOrkeonMemoryMigration()` | `MemoryMigrationService` — `MigrateAsync` copies every entry from one provider to another. Not registered by `AddOrkeonInfrastructure()`: call it when you move a store (e.g. In-Memory or SQLite to a vector database), resolve the service and pass it the source and target providers (two `MemoryProviderBase` instances — the source is enumerated key by key) |

Only `AddOrkeonRedisMemory` replaces the application-wide `IMemoryProvider`; the three vector-store
extensions make the shared provider injectable by its class, next to whatever `Memory:Provider` selected.

### A crew's memory: provider and scope

`memory:` decides whether a crew remembers — `memory: true` in YAML, `.memory(true)` in `.ork.ts`,
`CrewBuilder.EnableMemory()` in C#; off by default in all three, as in CrewAI:

- **`memory: true`**: the crew stores the result each task keeps, and recalls its memories before each
  task ([below](#what-a-crew-recalls)).
- **`memory: false`, or no `memory:`**: nothing is stored, nothing is recalled, and no memory system is
  materialized for the crew.

`memoryProvider:` says **where** a crew's memory lives, and needs `memory: true`: a provider named for a
crew without memory is refused — at load (`memoryProvider: 'sqlite' needs memory: true …`), and by
`Crew.Create(CrewCreateOptions)` / `CrewBuilder.Build()` in C#, both naming the remedy.

1. `memory` and `memoryProvider` map into `CrewConfiguration.Memory` / `.MemoryProvider`, which
   `CrewFactory` carries onto the domain `Crew` aggregate (`Crew.MemoryEnabled`, `Crew.MemoryProvider`),
   with the crew's name (`Crew.Name`: the `name:` of its configuration — YAML file, crew directory,
   `.ork.ts` crew — or `CrewBuilder.Name` in C#).
2. At kickoff the orchestrator records all three in the singleton `CrewMemoryProviderRegistry`
   (`Record(crewId, providerType, crewName, memoryEnabled)`; keyed by crew, so selections never leak
   across crews; `IsMemoryEnabled(crewId)` is what the run asks).
3. When `MemoryService` materializes that crew's memory system, it backs the crew's **long-term** memory
   (short-term memory stays an in-process sliding window) with:
   - the shared provider of the type the crew declared, from `MemoryProviderFactory` — connected from the
     host's section; unknown types keep the factory's In-Memory-with-warning fallback;
   - else, for a **named** crew, the host's default provider — the application-wide `IMemoryProvider`,
     whose type is `Memory:Provider` (In-Memory when unset);
   - else — a crew built in C# without a name — an in-process store of its own, which ends with the run:
     scoped by an id no later run will carry, it would be read by nobody.

`memoryProvider:` is a type and nothing more: `memoryProvider: "Redis"` connects with `Orkeon:Redis`,
`"SQLite"` with `Orkeon:Sqlite` (an in-process `:memory:` database when that section is absent).

What it means for the shipped hosts: `orkeon-host` remembers from one message to the next — its In-Memory
store lives as long as the daemon —, while `orkeon run` remembers from one process to the next only with a
durable provider: `memoryProvider: sqlite` (or Redis, a vector database), or a durable `Memory:Provider`.
On In-Memory, a crew's memory lives in RAM for the life of the process.

**The scope is the crew's name.** The provider instance is shared — by every crew of that type, and
by the RAG store of that type: with `Orkeon:Rag:Provider` unset the RAG store is the ambient provider,
whose chunks, manifests and registries live in the same key space. Every entry a crew's long-term
memory stores carries two custom properties, `kind = crew-memory` and `crew = <the crew's name>`
(`CrewMemoryScope`: `Stamp(item, scope)`, `Filter(scope)`), and every search of that memory asks the
provider for both ([metadata filter](#metadata-filter), applied before the limit). A crew therefore reads
what it stored, in this run and in the earlier runs of a crew of that name — which is what makes the
memory durable — and never another crew's entries, nor a RAG chunk. A crew without a name (built in C#
without `CrewBuilder.Name`) is scoped by its id: its memory lasts one run. Two crews of one name share
their memory — on In-Memory, Redis and SQLite, two names that differ only by case as well.
`MemoryCoordinator` also tags each memory it saves `crew:<name>` (the crew id when unnamed).

Clearing a crew's memory (`IMemoryService.ClearMemoryAsync`) deletes the entries that memory system
stored — the current run's — never the rest of the shared store, and releasing it never disposes the
shared provider. There is no retention: a crew's memory grows by one entry per task that succeeds, run
after run, and no verb resets it.

**What is stored.** After each task that succeeds, its output — exactly the task's output, under the agent
that wrote it (`IMemoryCoordinator.StoreTaskResultAsync`) —, embedded on its task (the description, with
the run's variables in it) and the start of its output (1,000 characters), with the custom properties
`agent_id`, `agent_role`, `task_id`, `task_description` and `stored_at`. Only what a task keeps:

- in `Consensual` mode, the answer the vote retained: the candidate answers and the ballots run with
  `SimpleExecutionContext.StoreResultInMemory` off, and the strategy stores the retained answer once,
  under the agent that wrote it ([Process types](../orchestration/process-types.md#4-consensual--voting-and-consensus));
- in `Hierarchical` mode, the output the manager accepted: each attempt runs with
  `StoreResultInMemory` off, and the strategy stores the accepted one once, under the agent it was
  assigned to — nothing when the manager rejects all three, nor when the worker fails
  ([Process types](../orchestration/process-types.md#2-hierarchical--manager--workers));
- in `Autonomous` mode, the output of a peer that took a failed task over: it runs in the context of
  the attempt that failed, derived from it under the crew's id (GAP-21), and its output, when it succeeds,
  is the task's result — stored once, under the peer ([Autonomous](../orchestration/autonomous.md));
- never a coworker's sub-answer: `delegate_work_to_coworker` runs the coworker in the delegating task's
  context, derived with `StoreResultInMemory` off — the same crew, the same scope, the parent's settings;
- never a ballot, never a forge trial ([Forge](../getting-started/forge-a-team-from-a-need.md)).

#### What a crew recalls

Before each task, `AgentExecutionService` asks `IMemoryCoordinator.RecallAsync` for the crew's memories
closest to the task, and the user prompt carries them:

- **The query** is the task the way the knowledge retrieval sees it: its description and expected output,
  with the run's variables. A memory is embedded on its own task and output, so the same task of an
  earlier run comes first.
- **The search** is by vector, in the crew's scope: the provider's `SearchSimilarAsync` with the `kind`
  and `crew` filter (on the in-process store of an unnamed crew, a cosine computed in process).
- **What is left out**: a memory equal to one of the outputs the prompt already carries from this run.
  The recall asks for as many more results as there are previous outputs, so it still returns
  `RecallLimit` memories when the crew has that many others; a content recalled twice is kept once.
- **The bounds** come from the `Orkeon:CrewMemory` section (`CrewMemoryOptions`, bound by
  `AddOrkeonInfrastructure()`): `RecallLimit` 5 memories, `MinScore` 0.6 (cosine), `MaxChars` 4,000
  characters of memory content in all — the last memory that does not fit is cut, the next ones dropped.
  `MinScore` is on the embedder's scale; 0.6 was measured on the local model (BGE-micro-v2), where the
  same task of an earlier run scores 0.72 and above, an unrelated English task 0.53 and below, and related
  work in between (0.59 to 0.72). The query carries the run's variables: a long input sharing the memory's
  subject lifted an unrelated task to 0.61. That model reads English: a French text scores high whatever
  it says (0.67 for an unrelated pair). Another embedder needs its own measure.
- **The format**: a section of the user prompt, after the previous task results and before the retrieved
  knowledge — the header *From this crew's memory — earlier work, possibly outdated; use it only where it
  helps:*, then for each memory a `--- 2026-09-30 · Analyst · Summarize the weekly news ---` line (when it
  was stored, the agent's role, the task cut to 80 characters) followed by its content. The Guardian
  screens it with the rest of the prompt (input phase).
- **Who recalls**: every execution that answers a task — a consensual candidate, a hierarchical attempt,
  an autonomous peer taking a task over — and a coworker when the task delegating to it does, but no
  ballot (`AgentBallotCollector` runs it with `SimpleExecutionContext.RecallFromMemory` off).

A crew relaunched without being reloaded — the C# `KickoffAsync` loop, the fixed-crew `CrewAgent` —
finds its earlier turns the same way: what is excluded is only what the prompt already carries.

#### The embedder, and failures

Every memory is embedded when it is stored, and every recall embeds its query, with the host's
`IEmbeddingProvider` — the port of the RAG: the local model, which `orkeon run` and `orkeon-host`
register through the `RaggableTree` section (`RaggableTree:Enabled: false` removes it) and the REPL
always, else the `Orkeon:Embeddings` section. Pinecone and ChromaDB therefore receive the vector they require.

A crew with `memory: true` is checked **before its first LLM call**: the orchestrator calls
`IMemoryCoordinator.EnsureReadyAsync`, which embeds a probe text and searches the crew's memory once. A
missing embedder, a refused key, an unreachable store or a vector of the wrong dimension fails the crew
there, the crew's name and the cause in its error. The store's dimension must be the embedder's: LanceDB
is created at 1,536 by default (`Orkeon:LanceDb:EmbeddingDimension`) where the local model gives 384, and
a Pinecone index's dimension is fixed when it is created.

During the run, a memory that fails is a warning, never a failed task: a store or a recall that throws is
logged at `Warning` with the crew, the task, the agent, the store and the cause; the task keeps its output,
and a recall that failed leaves it without memories. A cancellation is never swallowed. The rule lives in
`MemoryCoordinator`, the one entry point of the run's memory.

Embedding calls are not counted by the token meter ([Known limitations](../reference/limitations.md)).

#### What is not wired

- **The streaming kickoff** (`KickoffStreamingAsync`, a C# API no shipped host calls) neither recalls
  nor stores a crew's memory — it ignores knowledge attachments too. A crew with `memory: true` gets a
  warning saying so; `KickoffAsync` does both.
- **No LLM in the recall**: no fact extraction, no LLM-ranked recall, no composite recency/importance
  score (CrewAI's): the recall is vector-only. The [cognitive memory](#cognitive-memory) is the C# option
  that analyses with an LLM.

## Encryption at rest

`EncryptedMemoryProviderDecorator` wraps any `IMemoryProvider` (SQLite,
Redis, In-Memory…): content is encrypted via `IEncryptionProvider` before storage and
decrypted on read. Embeddings and metadata remain in clear text (required for
indexing); vector search (`SearchSimilarAsync`) is delegated to the inner provider
and result content is decrypted on the way back. Full-text search
(`SearchAsync`) on an encrypted store only matches the encrypted text — use vector
search in that case.

The decorator is never applied automatically: the host wraps the provider it wants protected
(`new EncryptedMemoryProviderDecorator(inner, encryptionProvider, logger)`), with the
AES-256-GCM `IEncryptionProvider` registered by `AddOrkeonInfrastructure()`
(`Orkeon:Encryption`). Re-encrypting an existing store under a new key is the opt-in
`AddOrkeonKeyRotation()` — see [Opt-in subsystems](../reference/opt-in-subsystems.md).

## Cognitive memory

The cognitive subsystem (`Orkeon.Infrastructure.Memory.Cognitive`, opt-in
`AddOrkeonCognitiveMemory(configuration)`) layers `ICognitiveMemoryService` — an `IMemoryService`
extended with `RememberAsync`, `RecallAsync`, `ConsolidateAsync`, `AnalyzeAsync` and
`CheckContradictionsAsync` — over the crew's memory. It needs an `ILlmProvider` (analysis) and
an `IEmbeddingProvider` (every remembered item, recall query and merged memory is embedded). Its result
types live in the Domain (`CognitiveMemoryTypes.cs`):

- `MemoryAnalysis` — LLM-scored importance (0.0-1.0), categorization, entity extraction (`MemoryAnalyzer`);
- `ContradictionCheck` + `ConflictResolution` — conflict detection between memories (`ContradictionDetector`);
- `ScoredMemory` — composite recall score computed by `CompositeScorer`: semantic similarity 0.5 +
  recency 0.3 + importance 0.2 by default (`RecallOptions.SemanticWeight`/`RecencyWeight`/`ImportanceWeight`,
  plus `TopK` 10 and `MinScore` 0.1); recency decays with a half-life of `RecencyHalfLifeHours`;
- `ConsolidationResult` — consolidation, pruning and conflict resolution (`MemoryConsolidator`).

It works in **the crew's own memory**: the long-term memory `IMemoryService` materializes for the crew —
its declared provider, else the host's default one for a named crew, scoped by its name
([above](#a-crews-memory-provider-and-scope)); an in-process store for a crew without a name. So a crew has
one memory: what it remembered is stamped `kind = crew-memory` and `crew = <scope>` like the memories its
runs store — its runs recall it, and its recall finds theirs —, and no other crew's memory, nor a RAG
chunk of the same store, ever comes back. The scope is the one the crew's kickoff recorded: before its
first kickoff, a crew id is a crew without a name.

- `RememberAsync` stores the item **once**, in that memory.
- `RecallAsync` and the contradiction candidates search it **by similarity**, within the scope.
- A conflict is resolved **where its candidates came from**: the memories to keep, remove or merge are
  the candidates the detector was shown, removed from the crew's memory by the key it returned; a merged
  memory is embedded and stored there.
- `ConsolidateAsync` finds the crew's memories by similarity within the scope, with no score floor (up to
  1,000 of them — what Pinecone answers to one query), clusters them by cosine, stores each merged memory
  embedded and removes what it replaces, in the same memory. A memory that comes back without its vector
  (ChromaDB returns none with a similarity match) stays a cluster of its own.

Options (`Orkeon:CognitiveMemory`, `CognitiveMemoryOptions`): `EnableLlmAnalysis` (`true`),
`EnableContradictionDetection` (`true`), `ContradictionCandidateCount` (10), `AnalysisModel`
(null = the provider's model), `AnalysisTemperature` (0.1), `PruningThreshold` (0.1),
`PruningMinAgeDays` (30), `RecencyHalfLifeHours` (69), `DefaultRecallOptions`.

---

> **See also**: [LLM Providers](./llm-providers.md) · [Security](./security.md) · [Configuration reference](../reference/configuration.md) · [Back to index](../INDEX.md)
