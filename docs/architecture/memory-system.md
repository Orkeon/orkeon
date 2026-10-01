> 🇫🇷 [Version française](../fr/architecture/memory-system.md)

# Memory System

## Interface and types

`IMemoryProvider` (`Orkeon.Domain.Memory`) defines the contract with seven methods: `StoreAsync`, `GetAsync`, `SearchAsync`, `DeleteAsync`, `ClearAsync`, `StoreWithEmbeddingAsync` and `SearchSimilarAsync` (vector search by cosine similarity). The last two carry default interface bodies (`SearchSimilarAsync`'s returns nothing), so every in-repo provider derives from `MemoryProviderBase` (`Orkeon.Infrastructure.Memory.Base`), which re-declares `SearchSimilarAsync` as **abstract** — a provider that forgot to implement vector search would otherwise silently return zero results through the interface. The base class also adds `UpdateAsync`, `CountAsync` and `ListKeysAsync`. There is no initialization step: a provider receives its options in its constructor and opens any connection on its first call.

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
| ChromaDB | `ChromaDbMemoryProvider` | REST API v2 (tenant/database routes), vector database |
| Pinecone | `PineconeMemoryProvider` | Cloud vector database — `Api-Key` header. Requests go to the index host: `Orkeon:Pinecone:Host` when set, otherwise the `host` Pinecone returns for `IndexName` from one `describe_index` call (`GET https://api.pinecone.io/indexes/{IndexName}`) made on first use |
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
- **Metadata filters**: `source` translates into a SQL equality; `tag`/`tags` and
  custom keys into `LIKE '%…%'` over the JSON columns (substring semantics — the
  SQL wildcard characters `%`/`_` in values match broadly).
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
holds the type only; the connection is the section of the chosen provider.

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

### Per-crew provider selection

A crew can declare its own provider via `memoryProvider` in YAML (or `CrewBuilder.WithMemoryProvider`).
The selection travels to the run rather than being fixed globally by `Memory:Provider` config:

1. `memoryProvider` maps into `CrewConfiguration.MemoryProvider`, which `CrewFactory` carries onto the
   domain `Crew` aggregate (`Crew.MemoryProvider`).
2. At kickoff the orchestrator records `Crew.Id → Crew.MemoryProvider` in the singleton
   `CrewMemoryProviderRegistry` (keyed by crew, so selections never leak across crews).
3. When `MemoryService` materializes that crew's memory system, it asks `MemoryProviderFactory` for
   that type's shared provider — connected from the host's section — and backs the crew's
   **long-term** memory with it (short-term memory stays an in-process sliding window). Unknown types
   keep the factory's In-Memory-with-warning fallback.

`memoryProvider:` is a type and nothing more: `memoryProvider: "Redis"` connects with `Orkeon:Redis`,
`"SQLite"` with `Orkeon:Sqlite` (an in-process `:memory:` database when that section is absent).
Because the instance is shared, a crew's long-term memory is visible to the next run and to the
other crews of that type — that is what makes it durable. Clearing a crew's memory deletes only the
entries that crew stored, and releasing it never disposes the shared provider.

A crew that declares no `memoryProvider` uses the in-process default store — behavior is unchanged.

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
`CheckContradictionsAsync` — over the memory provider. It needs an `ILlmProvider` (analysis) and
an `IEmbeddingProvider` (recall). Its result types live in the Domain (`CognitiveMemoryTypes.cs`):

- `MemoryAnalysis` — LLM-scored importance (0.0-1.0), categorization, entity extraction (`MemoryAnalyzer`);
- `ContradictionCheck` + `ConflictResolution` — conflict detection between memories (`ContradictionDetector`);
- `ScoredMemory` — composite recall score computed by `CompositeScorer`: semantic similarity 0.5 +
  recency 0.3 + importance 0.2 by default (`RecallOptions.SemanticWeight`/`RecencyWeight`/`ImportanceWeight`,
  plus `TopK` 10 and `MinScore` 0.1); recency decays with a half-life of `RecencyHalfLifeHours`;
- `ConsolidationResult` — consolidation, pruning and conflict resolution (`MemoryConsolidator`).

Options (`Orkeon:CognitiveMemory`, `CognitiveMemoryOptions`): `EnableLlmAnalysis` (`true`),
`EnableContradictionDetection` (`true`), `ContradictionCandidateCount` (10), `AnalysisModel`
(null = the provider's model), `AnalysisTemperature` (0.1), `PruningThreshold` (0.1),
`PruningMinAgeDays` (30), `RecencyHalfLifeHours` (69), `DefaultRecallOptions`.

---

> **See also**: [LLM Providers](./llm-providers.md) · [Security](./security.md) · [Configuration reference](../reference/configuration.md) · [Back to index](../INDEX.md)
