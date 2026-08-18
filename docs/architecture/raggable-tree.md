> 🇫🇷 [Version française](../fr/architecture/raggable-tree.md)

> **See also**: [Memory system](./memory-system.md) · [YAML reference](./yaml-schema.md) · [Tool inventory](../tools/inventory.md) · [Back to index](../INDEX.md)

# RaggableTree

Multi-language semantic graph of the source code: Tree-sitter parsing, embedding enrichment, vector storage, and exposure to agents through 15 tools. Replaces raw file reading with a structured index offering six zoom levels (monorepo → package → module → symbol → declaration → statement).

## Overview

An agent that only has `file_read` and `directory_read` must read entire files, guess their relations, and overload its LLM context. The RaggableTree precomputes these elements:

- **Structure**: each file is decomposed into classes, methods, functions, with stable Fully-Qualified Names (`pkg::Module::Class::method`).
- **Relations**: imports, calls, inheritance, and implementations are resolved from the AST and stored as graph edges.
- **Semantics**: signatures, docstrings, code snippets and, optionally, LLM summaries are composed into `EmbeddingText` and then vectorized.
- **Evolution**: an incremental reindexing engine updates the index after each commit or watcher event.

The exposed tools (`codebase_map`, `symbol_detail`, `flow_trace`, `impact_analysis`, etc.) give the agent structural queries instead of grep over plain text.

## Quick start

```csharp
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Domain.FileSystem;

// fileSystem: IFileSystemService (VFS) with the project mounted at /src
var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fileSystem);
var result = await builder.BuildAsync(
    "/src",
    new IndexCodebaseRequest { RootPath = "/src" },
    CancellationToken.None);

Console.WriteLine($"{result.Tree.Nodes.Count} nodes, {result.Tree.Edges.Count} edges, indexId={result.IndexId}");
```

For DI usage:

```csharp
services.AddRaggableTree(new RaggableTreeOptions
{
    Languages = ["typescript", "python"],
    Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.OpenAI, ApiKey = apiKey },
});
```

## YAML configuration

```yaml
raggableTree:
  enabled: true                    # false to disable the feature entirely
  languages: [typescript, csharp]  # [] = auto-detection via the registered adapters
  exclude: [node_modules, dist, .git, bin, obj]
  indexMode: frozen                # frozen | live | breakOnChange (§31.3)
  enrichWithLlm: false             # true to generate SemanticSummary via summarizer
  includeStatements: false         # true to index L4 (per-statement CFG)

  embedding:
    provider: openai               # none | openai | ollama | onnx
    model: text-embedding-3-small
    apiKey: ${OPENAI_API_KEY}
    baseUrl: https://api.openai.com/
    dimensions: 1536

  summarizer:
    provider: anthropic            # none | anthropic
    model: claude-haiku-4-5
    concurrency: 5

  vectorStore:
    provider: inMemory             # inMemory | redis | chromaDb | pinecone | lanceDb | sqlite

  cache:
    enabled: true
    path: .orkeon/raggable-tree.json
```

All fields are immutable `record` types in `Orkeon.Analysis.DependencyInjection.RaggableTreeOptions`.

## Six-phase pipeline

1. **Discovery** (`IFileSystemDiscoverer`) — recursive traversal, package detection via markers (`package.json`, `*.csproj`, `pyproject.toml`, `go.mod`, `Cargo.toml`), exclusion via patterns and `.gitignore`.
2. **Parse + Extract** (`UniversalSemanticMapper` + `ILanguageAdapter`) — Tree-sitter parse → application of the adapter's queries → L2 (module) and L3 (symbol) nodes with `Fqn`, `Signature`, `SourceSnippet`, `Sha256`.
3. **Dependency resolution** (`DependencyGraphBuilder` + `IReferenceResolver`) — resolution of imports, calls, inheritance, implementations into edges (`EdgeKind.Imports`, `Calls`, `Extends`, `Implements`). Unresolved FQNs become `UnresolvedRef`.
4. **Fingerprinting** (`IFrameworkFingerprinter`) — application of per-decorator/annotation rules (NestJS, Angular, ASP.NET, Flask, FastAPI): placing tags such as `http-endpoint`, `guard`, `service` on the relevant nodes.
5. **Enrichment** — composition of the `EmbeddingText` (`IEmbeddingTextComposer`), optionally an LLM summary (`INodeSummarizer`), then batch embedding (`IEmbeddingProvider`).
6. **Persistence** — `IVectorStoreProvider.IndexAsync` for the embeddings, serialized JSON cache of the full tree (`RaggableTreeCache`).

Incremental reindexing (`IncrementalReindexEngine`) restarts from phase 2 for the changed files only, reuses the nodes of unchanged files, and runs phases 4-6 only on the `newNodes`.

## Tool catalogue

| Tool | Usage | Typical request |
|------|-------|---------------|
| `index_codebase` | Builds the initial index | `{ "root_path": "/src" }` |
| `incremental_reindex` | Updates the index after file changes | `{ "changed_file_paths": ["src/a.ts"] }` |
| `index_status` | Lists every indexed virtual root (date, node/edge counts) | `{}` |
| `is_path_indexed` | Checks whether a path is covered by an indexed root | `{ "virtual_path": "/src/app/main.ts" }` |
| `codebase_map` | Overview: packages, file/symbol counts | `{ "depth": 2 }` |
| `package_summary` | Summary of a package (modules, dependencies, key symbols) | `{ "package_fqn": "app::core" }` |
| `symbol_detail` | Detail of a symbol (signature, doc, callers, callees) | `{ "fqn": "app::UserService::create" }` |
| `symbol_source` | Source code excerpt (signature, body, full span) | `{ "fqn": "...", "mode": "SignatureAndBody" }` |
| `codebase_search` | Semantic search by embedding | `{ "query": "session expiration", "top_k": 10 }` |
| `dependency_graph` | Incoming/outgoing edges of a node | `{ "fqn": "...", "kinds": ["Imports", "Calls"] }` |
| `sub_graph` | BFS expansion around seeds | `{ "seeds": ["pkg::X"], "max_depth": 3 }` |
| `flow_trace` | Call paths between two FQNs | `{ "from": "A", "to": "B", "max_paths": 5 }` |
| `impact_analysis` | Transitive impacts of a change | `{ "fqn": "...", "direction": "Backward" }` |
| `complexity_report` | Top-N by metric (Cyclomatic, LoC, Callers...) | `{ "metric": "Cyclomatic", "top_n": 20 }` |
| `statement_query` | L4 structural queries (if, try, return...) | `{ "parent_fqn": "...", "kinds": ["TryCatch"] }` |

The 15 tools are registered via `AddRaggableTreeTools` (`Orkeon.Tools.Analysis.DependencyInjection.RaggableTreeToolsServiceCollectionExtensions`).

## Agent strategies

The framework exposes `ICodebaseContextProvider`, which produces a compact summary of the codebase (packages, top complexity, top coupling, detected patterns) to inject into an agent's system prompt at startup. Three formats: `markdown` (default, ~300 tokens), `compact` (~150 tokens), `json` (~400 tokens). Injection only happens if an index is present and the agent owns at least one RaggableTree tool.

## Hybrid search

`codebase_search` (and `IRaggableStore.SemanticSearchAsync`) is **hybrid** since the
freshness work: an embedding cosine ranking and a BM25 lexical ranking are fused by
Reciprocal Rank Fusion (`SemanticQuery.Mode`: `Hybrid` default / `Vector` / `Lexical`).
The lexical half uses a **code-aware tokenizer** (`CodeTokenizer`): identifiers are split
on camelCase / snake_case / digit boundaries and indexed both as sub-tokens and whole
(`getUserById` → `get user by id getuserbyid`), so exact-identifier queries keep their
strong signal while concept queries gain recall. Each `SearchHit` carries `MatchOrigin`
(`hybrid`/`vector`/`bm25`); hybrid scores are RRF **rank aggregates** (not similarities —
never compare them across origins). With no embedder wired, `Hybrid` degrades to
`Lexical` instead of returning empty; explicit `Vector` keeps the historical contract.

## Freshness (edit ↔ search coordination)

The index stays truthful about an actively edited workspace through a **mark-dirty +
lazy-reindex** design:

- **Write hook** — `FileWriteTool` takes an optional `IIndexInvalidation` (registered by
  `AddRaggableTree`, implemented by the store): every successful write marks its path
  dirty. O(1), synchronous, no-op outside indexed roots.
- **Lazy pass** — the read tools (`codebase_search`, `symbol_source`, `flow_trace`,
  `codebase_map`) call `IndexFreshnessService.EnsureFreshAsync` before answering: the
  dirty set ∪ the **git working-tree changes** (catches `shell_command` edits) is
  reindexed incrementally — grouped, single-flight, debounced (a clean git probe is
  trusted for 2 s). `codebase_search` responses report `refreshed_files`;
  `index_status` reports `dirty_count`/`dirty_paths`, so "stale" is observable.
- **Store safety** — `InMemoryRaggableStore` holds a `ReaderWriterLockSlim`: searches
  enumerate safely DURING an incremental reindex (previously a concurrent search could
  throw on the mutated dictionaries). The refresh publishes `RaggableTreeUpdated` on the
  `IRaggableTreeEventBus`.
- **Failure barrier** — a failed refresh serves the current (stale) index and keeps the
  dirty debt for the next attempt: stale results beat a dead search.

Historical note: an earlier version of this document described three watcher-driven
synchronization modes (`frozen`/`live`/`breakOnChange` via `RaggableTreeIndexMode`).
Those modes were never consumed by any code — the enum existed, nothing read it. The
freshness design above replaces that fiction; `ICodebaseWatcher` remains available for
hosts that want push-based invalidation on top of the lazy pass.

## Extensibility

### Adding a language

Implement `ILanguageAdapter` (`Orkeon.Analysis.Abstractions.Interfaces`): provide `LanguageName`, `FileExtensions`, the seven Tree-sitter queries (`DeclarationQuery`, `ImportQuery`, `CallQuery`, `InheritanceQuery`, `DocCommentQuery`, `DecoratorQuery`, `StatementQuery`), the `MapNodeKind` mapping, and the `ExtractSignature` / `ResolveImportPath` extractors. Register the adapter via DI (`services.AddSingleton<ILanguageAdapter, MyAdapter>()`); `AddRaggableTree` discovers it automatically.

### Adding a fingerprinter

Create an `IReadOnlyList<FingerprintRule>` listing the decorators/annotations to match together with their tags, then build a `FrameworkFingerprinter(framework, rules)`. See `Orkeon.Analysis.Fingerprinters.NestJsRules` as a reference (15 rules).

### Adding an embedding provider

Implement `IEmbeddingProvider.EmbedBatchAsync(texts, ct)` → `IReadOnlyList<ReadOnlyMemory<float>>`. Three reference implementations: `OpenAIEmbeddingProvider`, `OllamaEmbeddingProvider`, and `LocalEmbeddingProvider` (see the section below). The extension method `EmbedAsync(nodes, model, ct)` (`Orkeon.Analysis.Vectors`) fills `node.Embedding` from the composed `EmbeddingText`.

## Embedding providers

Four options are available via `EmbeddingProviderKind`:

| Provider | Network | API key | Dims | Notes |
|----------|--------|---------|------|-------|
| `None` | n/a | n/a | n/a | Pipeline without embeddings (phases 5-6 disabled) |
| `OpenAI` | required | required | 1536 (`text-embedding-3-small`) | Maximum quality, MTEB ~62.3 |
| `Ollama` | local (HTTP) | no | model-dependent | Local Ollama / llama.cpp daemon required |
| `LocalSmartComponents` | no | no | 384 (BGE-micro-v2) | In-process, ONNX CPU, cold start ~200 ms |

### `LocalSmartComponents` local provider

Implemented by `Orkeon.Tools.Embeddings.Local` (opt-in package, upstream package `SmartComponents.LocalEmbeddings` v0.1.0-preview10148, BGE-micro-v2 model). Suited to dev CLI contexts, offline CI indexing, and demos without an API key.

**Quality / cost trade-off** (see spec §10):

- 384 dimensions vs 1536 (OpenAI) — less fine-grained semantic search on out-of-domain factual Q&A.
- MTEB score ~58.5 vs ~62.3 — acceptable for code indexing (signatures, FQNs, and docstrings are structured signals that are less ambiguous than long-form text).
- API cost → 0 (model embedded in the NuGet, ~22 MB).
- No network dependency, no key to manage.
- Cold start ~150-300 ms (loading the ONNX model into memory); ~5-15 ms / text on a modern x86_64 CPU.
- RAM footprint ~200 MB once the model is loaded.

#### Minimal configuration (`appsettings.json`, see spec §7.2)

```jsonc
{
  "RaggableTree": {
    "Embedding": {
      "Provider": "LocalSmartComponents"
      // Dimensions, BaseUrl, ApiKey, Model: all optional and ignored.
      // Defaults: 384 dims (BGE-micro-v2), CPU, in-process.
    }
  }
}
```

On the code side, the DI registration is:

```csharp
services.AddOrkeonLocalEmbeddings();        // Orkeon.Tools.Embeddings.Local package
services.AddRaggableTree(new RaggableTreeOptions
{
    Embedding = new EmbeddingOptions
    {
        Provider = EmbeddingProviderKind.LocalSmartComponents,
    },
});
```

#### Extended configuration with a VFS `ModelPath` (see spec §7.3)

To point to an ONNX model other than the embedded BGE-micro-v2, declare a read-only VFS mount and pass a **virtual** path in `Local.ModelPath`. Physical paths (`C:/...`, `/var/...`) are rejected by `IFileSystemService` at startup — see `vfs-compliance.md`.

```jsonc
{
  "Orkeon": {
    "FileSystem": {
      "Mounts": [
        "/models:r=C:\\data\\embedding-models"     // read-only mount (r=)
      ]
    }
  },
  "RaggableTree": {
    "Embedding": {
      "Provider": "LocalSmartComponents",
      "Dimensions": 384,
      "MaxTextChars": 2000,
      "Local": {
        "ModelPath": "/models/my-custom.onnx",     // virtual path — resolved by IFileSystemService
        "MaxConcurrency": 8
      }
    }
  }
}
```

The licenses of the model and of the upstream package are tracked in [`THIRD-PARTY-NOTICES.md`](../../THIRD-PARTY-NOTICES.md). A minimal console example lives in [`examples/local-embeddings/`](https://github.com/Orkeon/orkeon/tree/main/examples/local-embeddings).

### Adding a vector store

Implement `IVectorStoreProvider` (`IndexAsync`, `SearchAsync`, `DeleteAsync`). See the six existing memory providers (`Redis`, `SQLite`, `ChromaDB`, `Pinecone`, `LanceDB`, `InMemory`) for the `VectorDocument`/`VectorMetadata` mapping patterns.

## V1 limitations

- No support for C++ macros / fully resolved templates, nor for Ruby/Elixir metaprogramming.
- The import resolver is heuristic for dynamic languages (Python, TypeScript): re-exports and monkey patches can produce `UnresolvedRef`.
- Embedding and summarizer require an external provider (API keys or a local Ollama endpoint) to be enabled; the pipeline works without them (phases 5-6 disabled).
- The watcher relies on `System.IO.FileSystemWatcher` — on Linux/WSL, rename events may arrive decomposed (observed as Deleted then Created).
- The JSON cache is not versioned: an adapter update may invalidate existing caches, which are then rebuilt on demand.

---

> **See also**: [RaggableTree ADR](./raggable-tree-adr.md) · [Tool inventory](../tools/inventory.md) · [Back to index](../INDEX.md)
