> 🇫🇷 [Version française](../fr/architecture/memory-system.md)

# Memory System

## Interface and types

`IMemoryProvider` (`Orkeon.Domain.Memory`) defines the contract with seven methods: `StoreAsync`, `GetAsync`, `SearchAsync`, `DeleteAsync`, `ClearAsync`, `StoreWithEmbeddingAsync` and `SearchSimilarAsync` (vector search by cosine similarity).

Five memory types are defined by `MemoryType`: `ShortTerm` (immediate context), `LongTerm` (persistent information), `Episodic` (event sequences), `Entity` (information about specific entities), `Procedural` (learned skills).

## Implementations

| Provider | Class | Characteristics |
|----------|-------|-----------------|
| In-Memory | `InMemoryProvider` | `ConcurrentDictionary`, cosine vector search, development/tests |
| Redis | `RedisMemoryProvider` | Prefixed keys, Polly policies, camelCase JSON serialization |
| SQLite | `SqliteMemoryProvider` | `Microsoft.Data.Sqlite`, local persistence (file or `:memory:`), embeddings stored as BLOB, `LIKE` full-text search + cosine vector search (in-memory scan), identifiers and metadata preserved on read-back |
| ChromaDB | `ChromaDbMemoryProvider` | REST API v2 (tenant/database routes), vector database |
| Pinecone | `PineconeMemoryProvider` | Cloud vector database |
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
| `InitializeAsync` (table auto-created on first use) | `POST /v1/table/{t}/exists`, `POST /v1/table/{t}/create`, `POST /v1/table/{t}/create_index` (FTS) | JSON / Arrow IPC stream |
| `StoreAsync` / `UpdateAsync` (upsert) | `POST /v1/table/{t}/merge_insert?on=id&when_matched_update_all=true&when_not_matched_insert_all=true` | Arrow IPC stream |
| `GetAsync` / `ListKeysAsync` | `POST /v1/table/{t}/query` (SQL filter / `k`+`offset` pagination) | JSON → Arrow IPC file |
| `SearchAsync` (BM25 full-text) | `POST /v1/table/{t}/query` with `full_text_query` | JSON → Arrow IPC file |
| `SearchSimilarAsync` (vector) | `POST /v1/table/{t}/query` with `vector.single_vector` + `distance_type` | JSON → Arrow IPC file |
| `DeleteAsync` / `ClearAsync` | `POST /v1/table/{t}/delete` (SQL predicate) | JSON |
| `CountAsync` | `POST /v1/table/{t}/count_rows` | JSON → raw integer |

Authentication uses the `x-api-key` header (+ optional `x-lancedb-database`).
Data payloads are encoded/decoded as **Arrow IPC** via the `Apache.Arrow`
package (Apache-2.0, cf. `THIRD-PARTY-NOTICES.md`). The table schema is
fixed: `id`, `content`, `vector` (FixedSizeList<float32>[dim]), `importance`,
`source`, `tags` (JSON), `created_at`, `metadata_json`.

### Configuration

```json
{
  "Orkeon": {
    "LanceDb": {
      "Endpoint": "https://my-deployment.us-east-1.api.lancedb.com",
      "ApiKey": "…",
      "Database": "optionnel",
      "TableName": "orkeon_memories",
      "EmbeddingDimension": 1536,
      "DistanceType": "cosine",
      "CreateFullTextIndexOnInit": true
    }
  }
}
```

Registration: `services.AddOrkeonLanceDb(configuration)` (section `Orkeon:LanceDb`).
Without `Endpoint`, provider resolution fails explicitly (no local fallback).

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

`MemoryProviderFactory` (port `IMemoryProviderFactory`) resolves the provider from
`MemoryProviderConfigDto.Type`: `inmemory`, `redis`, `sqlite`, `chromadb`, `pinecone`, `lancedb`.
For SQLite, `ConnectionString` is the SQLite connection string
(e.g. `Data Source=orkeon-memory.db`) and `Options["TableName"]` allows changing the table
(identifier validated against SQL injection). For LanceDB, `ConnectionString` is the
LanceDB Cloud/Enterprise REST endpoint and `Options` may carry `ApiKey`, `TableName`, `Database`
(without an endpoint: explicit warning and In-Memory fallback; DI wiring via
`AddOrkeonLanceDb`). An unknown type falls back to In-Memory with an explicit warning.

### Per-crew provider selection

A crew can declare its own provider via `memoryProvider` in YAML (or `CrewBuilder.WithMemoryProvider`).
The selection travels to the run rather than being fixed globally by `Memory:Provider` config:

1. `memoryProvider` maps into `CrewConfiguration.MemoryProvider`, which `CrewFactory` carries onto the
   domain `Crew` aggregate (`Crew.MemoryProvider`).
2. At kickoff the orchestrator records `Crew.Id → Crew.MemoryProvider` in the singleton
   `CrewMemoryProviderRegistry` (keyed by crew, so selections never leak across crews).
3. When `MemoryService` materializes that crew's memory system, it resolves the recorded string to a
   concrete `IMemoryProvider` through `MemoryProviderFactory` and backs the crew's **long-term** memory
   with it (short-term memory stays an in-process sliding window). Unknown/unavailable types keep the
   factory's In-Memory-with-warning fallback.

A crew that declares no `memoryProvider` uses the in-process default store — behavior is unchanged.

## Encryption at rest

`EncryptedMemoryProviderDecorator` wraps any `IMemoryProvider` (SQLite,
Redis, In-Memory…): content is encrypted via `IEncryptionProvider` before storage and
decrypted on read. Embeddings and metadata remain in clear text (required for
indexing); vector search (`SearchSimilarAsync`) is delegated to the inner provider
and result content is decrypted on the way back. Full-text search
(`SearchAsync`) on an encrypted store only matches the encrypted text — use vector
search in that case.

## Cognitive memory

The cognitive subsystem in `Orkeon.Infrastructure.Memory.Cognitive` adds advanced capabilities: `MemoryAnalysis` (importance scoring 0.0-1.0, categorization, entity extraction), `ContradictionCheck` (conflict detection between memories), `ScoredMemory` (composite scoring: semantic similarity 0.5 + recency 0.3 + importance 0.2), and `MemoryConsolidator` (consolidation and conflict resolution).

---

> **See also**: [LLM Providers](./llm-providers.md) · [Security](./security.md) · [Back to index](../INDEX.md)
