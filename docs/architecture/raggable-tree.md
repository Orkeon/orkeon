> 🇫🇷 [Version française](../fr/architecture/raggable-tree.md)

> **See also**: [Memory system](./memory-system.md) · [YAML reference](./yaml-schema.md) · [Tool inventory](../tools/inventory.md) · [Back to index](../INDEX.md)

# RaggableTree

Multi-language semantic graph of the source code: Tree-sitter parsing, embedding enrichment, vector storage, and exposure to agents through 15 tools. Replaces raw file reading with a structured index offering five stratified node levels (L0 monorepo → L1 package → L2 module → L3 symbol → L4 statement) plus the edge layer between them.

## Overview

An agent that only has `file_read` and `directory_read` must read entire files, guess their relations, and overload its LLM context. The RaggableTree precomputes these elements:

- **Structure**: each file is decomposed into classes, methods, functions, with stable Fully-Qualified Names (`pkg::Module::Class::method`).
- **Relations**: imports, calls, inheritance, and implementations are resolved from the AST and stored as graph edges.
- **Semantics**: signatures, docstrings, code snippets and, optionally, LLM summaries are composed into `EmbeddingText` and then vectorized.
- **Evolution**: an incremental reindexing engine updates the index for the changed files only — on demand (`incremental_reindex`, a file list or a git commit range) and lazily before every read tool answers (see [Freshness](#freshness-edit--search-coordination)).

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

Runnable examples: [`basic-indexing`](https://github.com/Orkeon/orkeon/blob/main/examples/raggable-tree/basic-indexing/README.md) (the builder above, from C#), [`crew-yaml`](https://github.com/Orkeon/orkeon/blob/main/examples/raggable-tree/crew-yaml/README.md) (a YAML crew using the tools through the stock `orkeon` CLI, which registers them by default) and [`custom-adapter`](https://github.com/Orkeon/orkeon/blob/main/examples/raggable-tree/custom-adapter/README.md) (a new language).

For DI usage:

```csharp
services.AddRaggableTree(new RaggableTreeOptions
{
    Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.OpenAI, ApiKey = apiKey },
});
services.AddRaggableTreeTools();   // the 15 agent tools (Orkeon.Tools.Analysis)
```

`AddRaggableTree` (`Orkeon.Analysis.DependencyInjection`) registers the five
language adapters, the five framework fingerprinters, the summarizer, the
embedding provider, the singleton `InMemoryRaggableStore` (also exposed as
`IRaggableStore` and `IIndexInvalidation`), the `IndexFreshnessService`, the git
diff provider, the `IRaggableTreeEventBus`, the `ICodebaseContextProvider` and
the citation validators. The runner host calls
`AddRaggableTreeWithLogging(options)` (`Orkeon.Infrastructure`) instead: same
registrations, with the OpenAI/Ollama embedding HTTP traffic routed through the
LLM exchange log. The `orkeon-repl` REPL calls `AddRaggableTree` with on-device
embeddings and reads no configuration section.

## Configuration

The only configuration surface the shipped hosts bind is the **`RaggableTree`
appsettings section**, read key by key by the runner host (`orkeon run` and the
other runners) — there is no crew-YAML `raggableTree:` key, and the REPL binds
nothing. RaggableTree is **on by default** in both:

```jsonc
{
  "RaggableTree": {
    "Enabled": true,                    // false disables the feature entirely (runner opt-out)
    "Embedding": {
      "Provider": "LocalSmartComponents", // the runner default — or None | OpenAI | Ollama (Onnx is a reserved no-op arm)
      "Model": "",                      // empty → the provider's own default
      "ApiKey": null,                   // OpenAI only
      "BaseUrl": null,                  // OpenAI / Ollama
      "Dimensions": null,               // OpenAI (default 1536) / Ollama (default 768)
      "MaxTextChars": null              // OpenAI per-text cap
    }
  }
}
```

The section carries those two keys and nothing else: the runner refuses to start
on any other key under `RaggableTree`, naming it. What an index covers is set by
each `index_codebase` call (`exclude`, `root_alias`, `enrich_with_llm`,
`respect_gitignore`, `languages` — see the [tool catalogue](#tool-catalogue)),
never by the host. Languages in particular are **never configured**: they are
auto-detected from the codebase and scoped per call.

The rest of `RaggableTreeOptions` is reachable from C# via
`AddRaggableTree(options)`: `Summarizer` (`Provider` = `None` | `Anthropic` —
the latter registers `LlmNodeSummarizer` over the host's `ILlmProvider`, and an
`index_codebase` call with `enrich_with_llm` then summarizes every node; plus
`Model`, default `claude-haiku-4-5`, and `Concurrency`, default 5) and
`ValidateCitations` (default `true`, registers the `ICitationBlockValidator` /
`IInlineFqnValidator` citation validators). The runner host never sets a
summarizer, so no shipped host generates LLM summaries. Note the C# default of
`Embedding.Provider` is `None`; only the runner host defaults to
`LocalSmartComponents`.

All fields are immutable `record` types in `Orkeon.Analysis.DependencyInjection.RaggableTreeOptions`.

## Six-phase pipeline

1. **Discovery** (`IFileSystemDiscoverer`) — recursive traversal, package detection via markers (`package.json`, `*.csproj`, `pyproject.toml`, `go.mod`, `Cargo.toml`), exclusion via patterns and `.gitignore`.
2. **Parse + Extract** (`UniversalSemanticMapper` + `ILanguageAdapter`) — Tree-sitter parse → application of the adapter's queries → L2 (module) and L3 (symbol) nodes with `Fqn`, `Signature`, `SourceSnippet`, `Sha256`; L4 statements (`StatementExtractor`), extracted on every build.
3. **Dependency resolution** (`DependencyGraphBuilder` + `IReferenceResolver`) — resolution of imports, calls, inheritance, implementations into edges (`EdgeKind.Imports`, `Calls`, `Extends`, `Implements`). Unresolved FQNs become `UnresolvedRef`.
4. **Fingerprinting** (`IFrameworkFingerprinter`) — application of per-decorator/annotation rules (NestJS, Angular, ASP.NET, Flask, FastAPI): placing tags such as `http-endpoint`, `guard`, `service` on the relevant nodes.
5. **Enrichment** — optionally an LLM summary (`INodeSummarizer`, when the call sets `enrich_with_llm` and a summarizer is registered), composition of the `EmbeddingText` (`IEmbeddingTextComposer`), then batch embedding (`IEmbeddingProvider`, skipped when none is registered).
6. **Persistence** — the tree lives in `InMemoryRaggableStore` (`index_codebase` replaces its content), which also answers every query. When an `IVectorStoreProvider` is registered, `IndexAsync` additionally receives a mirror of the embeddings — no shipped host registers one (`MemoryProviderVectorStoreAdapter`, `Orkeon.Analysis.Vectors`, adapts any `IMemoryProvider` for a host that wants it), and nothing reads that mirror back. The `RaggableTreeCache` JSON serializer exists but is wired by no shipped host.

Each phase reports its progress (`IndexBuildPhase`: Discovery, Parse, Resolve, Enrich, Embed, Persist) to an optional `IProgress<IndexBuildProgress>` registered by the host (the REPL wires it to its status-line progress bar).

Incremental reindexing (`IncrementalReindexEngine`) restarts from phase 2 for the changed files only, reuses the nodes of unchanged files, and runs phases 4-6 only on the `newNodes`.

## Tool catalogue

| Tool | Usage | Typical request |
|------|-------|---------------|
| `index_codebase` | Builds the initial index | `{ "root_path": "/src" }` |
| `incremental_reindex` | Updates the index after file changes (explicit list, or a `from_commit`/`to_commit` git range) | `{ "changed_files": ["src/a.ts"] }` |
| `index_status` | Lists every indexed virtual root (date, node/edge counts) | `{}` |
| `is_path_indexed` | Checks whether a path is covered by an indexed root | `{ "virtual_path": "/src/app/main.ts" }` |
| `codebase_map` | Overview: packages, file/symbol counts | `{ "level": "L2_Module" }` |
| `package_summary` | Summary of a package (modules, dependencies, key symbols) | `{ "fqn": "app::core" }` |
| `symbol_detail` | Detail of a symbol (signature, doc, callers, callees) | `{ "fqn": "app::UserService::create" }` |
| `symbol_source` | Source code excerpt (signature, body, full span) | `{ "fqn": "...", "mode": "SignatureAndBody" }` |
| `codebase_search` | Hybrid search — embedding + BM25 fused (see [below](#hybrid-search)) | `{ "query": "session expiration", "top_k": 10 }` |
| `dependency_graph` | Level-scoped dependency graph (L1/L2/L3), Mermaid/DOT rendering | `{ "scope": "L2_Module", "edge_kinds": ["Imports", "Calls"] }` |
| `sub_graph` | BFS expansion around seeds | `{ "seeds": ["pkg::X"], "depth": 3 }` |
| `flow_trace` | Call paths between two FQNs | `{ "from": "A", "to": "B", "max_paths": 5 }` |
| `impact_analysis` | Transitive impacts of a change | `{ "target": "...", "direction": "Backward" }` |
| `complexity_report` | Top-N by metric (`Cyclomatic`, `NestingDepth`, `FanOut`, `LoC`, `Callers`) | `{ "metric": "Cyclomatic", "top_n": 20 }` |
| `statement_query` | L4 structural queries (if, try, return...) — every index carries the statements | `{ "parent_fqns": ["..."], "kinds": ["TryCatch"] }` |

The 15 tools are registered via `AddRaggableTreeTools` (`Orkeon.Tools.Analysis.DependencyInjection.RaggableToolsExtensions`).

`index_codebase` carries the build knobs per call (`IndexCodebaseRequest`):
`root_path` (required, a virtual path), `languages` (empty = auto-detect),
`exclude` (default `node_modules`, `dist`, `.git`, `bin`, `obj`),
`respect_gitignore` (default `true`), `enrich_with_llm` (default `false`; needs
a registered summarizer), `root_alias` (replaces the virtual-root prefix of every
FQN) and `embedding_model` — exactly what the build reads. Statements are always
extracted, and an argument the request does not declare is ignored.
`incremental_reindex` accepts the same `root_path`, `languages` and
`enrich_with_llm`.

## Agent strategies

The framework exposes `ICodebaseContextProvider`, which produces a compact summary of the codebase (packages, top complexity, top coupling, detected patterns) suitable for an agent's system prompt. Three formats: `markdown` (default, ~300 tokens), `compact` (~150 tokens), `json` (~400 tokens). **No framework component calls it automatically** — it is registered by `AddRaggableTree` and the host resolves it and injects the summary where it wishes.

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
Those modes were never consumed by any code — the enum existed, nothing read it, and it
was removed with the `IndexMode` key (GAP-15). The
freshness design above replaces that fiction; `ICodebaseWatcher`
(`FileSystemWatcherCodebaseWatcher`, which no shipped host registers) remains available for
hosts that want push-based invalidation on top of the lazy pass.

## Extensibility

### Adding a language

Implement `ILanguageAdapter` (`Orkeon.Analysis.Abstractions.Interfaces`): provide `LanguageName`, `FileExtensions`, the seven Tree-sitter queries (`DeclarationQuery`, `ImportQuery`, `CallQuery`, `InheritanceQuery`, `DocCommentQuery`, `DecoratorQuery`, `StatementQuery`), the `MapNodeKind` and `MapStatementKind` mappings, and the `ExtractSignature` / `ResolveImportPath` extractors (`RefineKind`, `ResolveDocComment`, `ExtractName` and `GetExtraModifiers` have default implementations). Register the adapter via DI (`services.AddSingleton<ILanguageAdapter, MyAdapter>()`): the builder and the incremental engine take every registered `ILanguageAdapter`, next to the five built-ins (`LanguageAdapterFactory`). A runnable Java adapter lives in [`examples/raggable-tree/custom-adapter/`](https://github.com/Orkeon/orkeon/blob/main/examples/raggable-tree/custom-adapter/README.md).

### Adding a fingerprinter

Create an `IReadOnlyList<FingerprintRule>` listing the decorators/annotations to match together with their tags, then build a `FrameworkFingerprinter(framework, rules)`. See `Orkeon.Analysis.Fingerprinters.NestJsRules` as a reference (15 rules).

### Adding an embedding provider

Implement `IEmbeddingProvider.EmbedBatchAsync(texts, ct)` → `IReadOnlyList<ReadOnlyMemory<float>>`. Three reference implementations: `OpenAIEmbeddingProvider`, `OllamaEmbeddingProvider`, and `LocalEmbeddingProvider` (see the section below). The default interface method `EmbedAsync(nodes, model, ct)` (on `IEmbeddingProvider`, `Orkeon.Analysis.Abstractions.Interfaces`) fills `node.Embedding` from the composed `EmbeddingText`.

## Embedding providers

Five members exist on `EmbeddingProviderKind`:

| Provider | Network | API key | Dims | Notes |
|----------|--------|---------|------|-------|
| `None` | n/a | n/a | n/a | No embeddings (the C# default): embedding and mirror skipped, `codebase_search` degrades to BM25 |
| `Onnx` | no | no | — | **Reserved** — currently a no-op registration arm |
| `OpenAI` | required | required | 1536 (`text-embedding-3-small`) | Maximum quality, MTEB ~62.3 |
| `Ollama` | local (HTTP) | no | model-dependent (768 assumed for the default `nomic-embed-text`) | Local Ollama / llama.cpp daemon required (default `http://localhost:11434/`) |
| `LocalSmartComponents` | no | no | 384 (BGE-micro-v2) | In-process, ONNX CPU, cold start ~200 ms — the runner host default |

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

To point to an ONNX model other than the embedded BGE-micro-v2, declare a read-only VFS mount and pass a **virtual** path in `LocalEmbeddingOptions.ModelPath` (`Orkeon.Analysis.Abstractions.DependencyInjection`). Physical paths (`C:/...`, `/var/...`) are rejected by `IFileSystemService` — see [vfs-compliance.md](./vfs-compliance.md). These options are set **from C#**: the runner host does not read a `RaggableTree:Embedding:Local` section and always registers the embedded model with its defaults.

```jsonc
{
  "Orkeon": {
    "FileSystem": {
      "Mounts": [
        "C:\\data\\embedding-models:/models:ro"     // <physical>:<virtual>:<rights>
      ]
    }
  }
}
```

```csharp
services.AddOrkeonLocalEmbeddings(new LocalEmbeddingOptions
{
    ModelPath = "/models/my-custom.onnx",   // virtual path — resolved by IFileSystemService
    MaxConcurrency = 8,                     // default: Environment.ProcessorCount
    MaxTextChars = 2000,                    // default 2000 (BGE-micro-v2: 512-token window)
});
services.AddRaggableTree(new RaggableTreeOptions
{
    Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.LocalSmartComponents },
});
```

Without `AddOrkeonLocalEmbeddings`, `EmbeddingOptions.Local` carries the same options and `AddRaggableTree` instantiates the provider by reflection — which fails loudly if the `Orkeon.Tools.Embeddings.Local` assembly is not referenced by the host.

The licenses of the model and of the upstream package are tracked in [`THIRD-PARTY-NOTICES.md`](../../THIRD-PARTY-NOTICES.md). A minimal console example lives in [`examples/local-embeddings/`](https://github.com/Orkeon/orkeon/tree/main/examples/local-embeddings).

### Adding a vector store

Implement `IVectorStoreProvider` (`IndexAsync`, `SearchAsync`, `DeleteAsync`) and register it: the builder and the incremental engine then mirror the embeddings into it (`DeleteAsync` for removed nodes on reindex). The shipped implementation is `MemoryProviderVectorStoreAdapter` (`Orkeon.Analysis.Vectors`), which maps `VectorDocument`/`VectorMetadata` onto any of the six memory providers (`Redis`, `SQLite`, `ChromaDB`, `Pinecone`, `LanceDB`, `InMemory`) — construct it over the `IMemoryProvider` of your choice. Queries keep running on `InMemoryRaggableStore`: the mirror is write-only in V1.

## V1 limitations

- No support for C++ macros / fully resolved templates, nor for Ruby/Elixir metaprogramming.
- The import resolver is heuristic for dynamic languages (Python, TypeScript): re-exports and monkey patches can produce `UnresolvedRef`.
- The summarizer is C#-only (`Summarizer.Provider` + an `ILlmProvider`); OpenAI/Ollama embeddings need their endpoint, while the runner host's on-device default needs nothing. The pipeline works without embeddings (`codebase_search` then ranks by BM25 only).
- The index is process-local: `InMemoryRaggableStore` is rebuilt by `index_codebase` in each process, and the optional vector-store mirror is never read back.
- `FileSystemWatcherCodebaseWatcher` (the `ICodebaseWatcher` implementation, registered by no shipped host) relies on `System.IO.FileSystemWatcher` — on Linux/WSL, rename events may arrive decomposed (observed as Deleted then Created).
- The JSON cache (`RaggableTreeCache`, wired by no shipped host) is not versioned: an adapter update may invalidate existing caches, which are then rebuilt on demand.

---

> **See also**: [RaggableTree ADR](./raggable-tree-adr.md) · [Tool inventory](../tools/inventory.md) · [Back to index](../INDEX.md)
