> 🇫🇷 [Version française](../fr/architecture/raggable-tree.md)

> **See also**: [Memory system](./memory-system.md) · [YAML reference](./yaml-schema.md) · [Tool inventory](../tools/inventory.md) · [Back to index](../INDEX.md)

# RaggableTree

Multi-language semantic graph of the source code: Tree-sitter parsing, embedding enrichment, vector storage, and exposure to agents through 28 tools. Replaces raw file reading with a structured index offering five stratified node levels (L0 monorepo → L1 package → L2 module → L3 symbol → L4 statement) plus the edge layer between them.

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
services.AddRaggableTreeTools();   // the 28 agent tools (Orkeon.Tools.Analysis)
```

`AddRaggableTree` (`Orkeon.Analysis.DependencyInjection`) registers the twelve
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
`respect_gitignore`, `languages`, `max_parallelism` — see the [tool catalogue](#tool-catalogue)),
never by the host. Languages in particular are **never configured**: they are
auto-detected from the codebase and scoped per call.

The rest of `RaggableTreeOptions` is reachable from C# via
`AddRaggableTree(options)`: `Summarizer` (`Provider` = `None` | `Anthropic` —
the latter registers `LlmNodeSummarizer` over the host's `ILlmProvider`, and an
`index_codebase` call with `enrich_with_llm` then summarizes every node; plus
`Model`, empty by default, which runs the summaries on the model of the host's
`ILlmProvider`, and `Concurrency`, default 5) and
`ValidateCitations` (default `true`, registers the `ICitationBlockValidator` /
`IInlineFqnValidator` citation validators). The runner host never sets a
summarizer, so no shipped host generates LLM summaries. Note the C# default of
`Embedding.Provider` is `None`; only the runner host defaults to
`LocalSmartComponents`.

All fields are immutable `record` types in `Orkeon.Analysis.DependencyInjection.RaggableTreeOptions`.

## Six-phase pipeline

1. **Discovery** (`IFileSystemDiscoverer`) — recursive traversal, package detection via markers (`package.json`, `*.csproj`, `pyproject.toml`, `go.mod`, `Cargo.toml`, `pom.xml`, `build.gradle`, `build.gradle.kts`, `composer.json`, `Gemfile`, `CMakeLists.txt`), exclusion via patterns and `.gitignore`. A file the file system policy refuses to read (a mount right, a blocked extension) is skipped and counted, never a failure of the whole build: `index_codebase` reports it in `filter_stats.excluded_by_policy` and `filter_stats.excluded_by_policy_paths`.
2. **Parse + Extract** (`UniversalSemanticMapper` + `ILanguageAdapter`) — Tree-sitter parse → application of the adapter's queries → L2 (module) and L3 (symbol) nodes with `Fqn`, `Signature`, `SourceSnippet`, `Sha256`; L4 statements (`StatementExtractor`), extracted on every build. Files are read and parsed `max_parallelism` at a time, then merged one after the other in discovery order, so the index (nodes, edges, their order, `IndexId`) is the same whatever the degree. `incremental_reindex` parses its changed files one after the other.
3. **Dependency resolution** (`DependencyGraphBuilder` + `IReferenceResolver`) — resolution of imports, calls, inheritance, implementations into edges (`EdgeKind.Imports`, `Calls`, `Extends`, `Implements`). Unresolved FQNs become `UnresolvedRef`.
4. **Fingerprinting** (`IFrameworkFingerprinter`) — application of per-decorator/annotation rules (NestJS, Angular, ASP.NET, Flask, FastAPI): placing tags such as `http-endpoint`, `guard`, `service` on the relevant nodes.
5. **Enrichment** — optionally an LLM summary (`INodeSummarizer`, when the call sets `enrich_with_llm` and a summarizer is registered), composition of the `EmbeddingText` (`IEmbeddingTextComposer`), then batch embedding (`IEmbeddingProvider`, skipped when none is registered).
6. **Persistence** — the tree lives in `InMemoryRaggableStore` (`index_codebase` replaces its content), which also answers every query. When an `IVectorStoreProvider` is registered, `IndexAsync` additionally receives a mirror of the embeddings — no shipped host registers one (`MemoryProviderVectorStoreAdapter`, `Orkeon.Analysis.Vectors`, adapts any `IMemoryProvider` for a host that wants it), and nothing reads that mirror back. Between sessions, `index_codebase` saves the tree in a SQLite database, `<root>/.orkeon/cache/raggable-tree/index.db` (see [Index cache](#index-cache)).

Each phase reports its progress (`IndexBuildPhase`: Discovery, Parse, Resolve, Enrich, Embed, Persist) to an optional `IProgress<IndexBuildProgress>` registered by the host (the REPL wires it to its status-line progress bar).

Incremental reindexing (`IncrementalReindexEngine`) restarts from phase 2 for the changed files only, reuses the nodes of unchanged files, and runs phases 4-6 only on the `newNodes`.

### Markdown documents

Markdown is a first-class language of the index, outside the Tree-sitter path: files ending in `.md` or `.markdown` are discovered as language `markdown` and parsed with Markdig by `MarkdownDocumentMapper` (`Orkeon.Analysis.Adapters`), one file read each. Each file yields:

- a `Section` node per heading (levels 1-6, nested FQN `<file>::Title/Sub`, level L3) whose range runs to the next heading of the same or a higher level;
- a `CodeFence` node per fenced block (the language is its tag; a `mermaid` fence is also tagged `mermaid`), a `Link` node per link (tag `target:<raw target>`, plus `resolved:<fqn>` when `MarkdownDocumentMapper.ResolveLinksAsync` finds the target in the store, by path or by local name; an unresolved link keeps only its raw target) and a `FrontMatter` node for a leading YAML block (the top-level keys are its tags), all L4.

Ranges are UTF-8 **byte** offsets (`NodeRange.StartByte`/`EndByte`), like the other languages. The four new `UniversalNodeKind` values (`Section`, `CodeFence`, `Link`, `FrontMatter`) are appended after `Unknown` because the persisted graph stores the enum by name. `codebase_search` restricted to the language `markdown` (its `FilterLanguages` option) returns the section hits. Limits: a `.md` file has no module node in the tree, so a file-to-file link resolves only against a store that holds one, and the build does not call `ResolveLinksAsync` yet.

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
| `access_cost` | Port-access cost (reads, writes, calls in loops, in-memory filters) of members by `fqns` or `changed_files`, restricted to `roots` | `{ "changed_files": ["/src/Orders/A.cs"], "roots": ["/src/Orders"] }` |
| `statement_query` | L4 structural queries (if, try, return...) — every index carries the statements | `{ "parent_fqns": ["..."], "kinds": ["TryCatch"] }` |
| `node_outline` | Members of a source file (any loaded grammar) or Markdown sections, with selector, kind, byte and line ranges | `{ "path": "/workspace/Calc.cs" }` |
| `node_read` | One member by selector: text, range and SHA-256, read uncut | `{ "path": "/workspace/Calc.cs", "selector": "Demo.Calc.Add(int)" }` |
| `node_replace` | Replace one node by selector and expected SHA-256 (Edit class) | `{ "path": "/workspace/Calc.cs", "selector": "Demo.Calc.Add(int)", "expected_hash": "<sha256>", "text": "..." }` |
| `node_insert` | Add a node `inside`, `before` or `after` an anchor selector (Edit class) | `{ "path": "/workspace/Calc.cs", "selector": "Demo.Calc", "position": "inside", "text": "public int Zero() => 0;" }` |
| `edit_batch` | Several `node_replace` / `node_insert` operations over several files as one transaction (Edit class) | `{ "operations": [{ "kind": "node_replace", "path": "/workspace/Calc.cs", "selector": "Demo.Calc.Add(int)", "expected_hash": "<sha256>", "text": "..." }], "validate": "parse" }` |
| `node_ensure` | Apply one idempotent intent operation (`ensure_using`, `ensure_import`, `ensure_field`, `ensure_member`, `ensure_section`, `insert_under_heading`, `replace_mermaid`), see [Intent operations](#intent-operations) | `{ "op": "ensure_using", "path": "/workspace/Calc.cs", "name": "System.Text" }` |
| `structural_search` | Find syntax nodes by pattern or by rule (read class), see [Structural search](#structural-search) | `{ "language": "cs", "pattern": "Log.Write($X)" }` |
| `structural_rewrite` | Rewrite the matches with a `fix` template, dry run by default (Edit class), see [Structural rewrite](#structural-rewrite) | `{ "language": "cs", "pattern": "Log.Write($X)", "fix": "logger.Info($X)", "glob": "src/**/*.cs" }` |
| `affected_tests` | Test projects and classes reached through the graph by changed files, see [Graph queries](#graph-queries) | `{ "changed_files": ["/src/Orders/A.cs"] }` |
| `batch_anchors` | Closure of anchors (contains, callers, tests) and overlap of two anchor sets | `{ "action": "closure", "fqns": ["Demo.Calc"] }` |
| `documented_in` | Markdown sections that document a symbol | `{ "fqn": "Demo.Calc" }` |
| `documents_of` | Symbols documented by the sections of a Markdown file | `{ "path": "/workspace/docs/calc.md" }` |

The 28 tools are registered via `AddRaggableTreeTools` (`Orkeon.Tools.Analysis.DependencyInjection.RaggableToolsExtensions`).

`index_codebase` carries the build knobs per call (`IndexCodebaseRequest`):
`root_path` (required, a virtual path), `languages` (empty = auto-detect),
`exclude` (default `node_modules`, `dist`, `.git`, `bin`, `obj`),
`respect_gitignore` (default `true`), `enrich_with_llm` (default `false`; needs
a registered summarizer), `root_alias` (replaces the virtual-root prefix of every
FQN), `embedding_model`, `max_parallelism` (how many files are read and parsed
at once: `0`, the default, one per processor; `1` one after the other; a negative
value is refused) and `use_cache` (default `true`; `false` rebuilds from scratch, then
saves) — exactly what the build reads. Statements are always
extracted, and an argument the request does not declare is ignored.
`incremental_reindex` accepts the same `root_path`, `languages` and
`enrich_with_llm`.

### Index cache

`AddRaggableTree` registers `CachingTreeBuilder`, which `index_codebase` uses to keep the
index between sessions. Each build saves the tree, the SHA-256, size and write time of every
indexed file and the SHA-256 of every package marker in one SQLite database, `<root>/.orkeon/cache/raggable-tree/index.db`,
in a folder holding its own `.gitignore` (`*`). The next call with the same options reads
it back and answers `cache`:

| `cache` | When | What it does |
|---|---|---|
| `hit` | No file changed | Reloads the saved tree |
| `refreshed` | At most half of the files changed (added, modified, removed), no package marker changed, no `root_alias` | Reloads the tree and reindexes the changed files (`reindexed_file_count`), as `incremental_reindex` does, then saves it |
| `miss` | Nothing saved, a key change, a changed marker, or more files changed | Builds from scratch, then saves |
| `bypassed` | `use_cache: false` | Builds from scratch, then saves |
| `disabled` | The tool was built without a cache (no `AddRaggableTree`) | Builds from scratch, saves nothing |

The key covers what shapes the tree beyond the files: the options (`languages`, `exclude`,
`respect_gitignore`, `root_alias`, `enrich_with_llm`, `embedding_model`, not
`max_parallelism`), the Analysis assembly version, the serializer version, and the adapters,
fingerprinters, summarizer and embedder (with its dimension) the host wires. A changed key
is a miss. A root the process cannot write is indexed without a cache.

#### Database schema

`RaggableTreeCache` owns the database. One database keeps up to `RaggableTreeCache.MaxEntries`
(`3`) saved indexes side by side, one per key: two callers indexing one root with different
options each find their own. A save drops every entry saved by another runtime (another cache
format or Analysis version, the `runtime` column), which no key of the current runtime can hit, then
evicts past that count the entry least recently used (`used_at`, which every save and every hit
refresh). Its schema version is `PRAGMA user_version` (`3`); a database of another version has
its tables dropped and created again at the next
save, and reads as a miss until then. The connection runs with `foreign_keys = ON` and the
journal in WAL mode.

| Table | Primary key | Holds |
|---|---|---|
| `cache_entries` | `entry_id` (rowid alias); `cache_key` `UNIQUE` | One saved index: its key, identity and edges |
| `indexed_files` | (`entry_id`, `path`) (`WITHOUT ROWID`) | The stamp of each indexed file of an entry |
| `package_markers` | (`entry_id`, `path`) (`WITHOUT ROWID`) | The SHA-256 of each package marker of an entry |
| `node_groups` | `group_id` (rowid alias); (`entry_id`, `virtual_path`) `UNIQUE` | The nodes of one source file of an entry |
| `node_runs` | (`entry_id`, `position`) (`WITHOUT ROWID`) | The order of an entry's nodes in its tree |

Every other table names its entry through `entry_id`, a `FOREIGN KEY` → `cache_entries (entry_id)`
`ON DELETE CASCADE`: deleting an entry deletes its files, markers, groups and runs with it.

`cache_entries`:

| Column | Type | Constraint | Meaning |
|---|---|---|---|
| `entry_id` | `INTEGER` | `PRIMARY KEY` | Surrogate key, referenced by every other table |
| `cache_key` | `TEXT` | `NOT NULL UNIQUE` | The SHA-256 of the key described above |
| `runtime` | `TEXT` | `NOT NULL` | The cache format and Analysis version that saved the entry, the part of the key another runtime never matches |
| `index_id` | `TEXT` | `NOT NULL` | The `IndexId` of the saved tree |
| `generation` | `INTEGER` | `NOT NULL`, `CHECK (generation > 0)` | Counts the saves under this key, from `1` |
| `node_count`, `edge_count` | `INTEGER` | `NOT NULL` | The size of the tree, for whoever inspects the database; the load does not read them |
| `edges` | `BLOB` | `NOT NULL` | Every edge, gzip of the JSON array of the serializer's edges |
| `saved_at` | `TEXT` | `NOT NULL` | The UTC time of the last save, ISO 8601 |
| `used_at` | `TEXT` | `NOT NULL` | The UTC time of the last save or hit, ISO 8601; orders the eviction |

`indexed_files`:

| Column | Type | Constraint | Meaning |
|---|---|---|---|
| `entry_id` | `INTEGER` | `NOT NULL`, `FOREIGN KEY` `ON DELETE CASCADE` | The entry |
| `path` | `TEXT` | `NOT NULL` | The file, relative to the root |
| `sha256` | `TEXT` | `NOT NULL` | The SHA-256 of its content, lowercase hex |
| `size_bytes` | `INTEGER` | `NOT NULL` | Its size when it was hashed |
| `modified_ticks` | `INTEGER` | nullable | Its last write time (UTC ticks) when it was hashed; `NULL` when that time is not trusted |

`package_markers` has `entry_id`, `path` and `sha256`, as `indexed_files`. The builder
compares them with the files it discovers to tell a hit, a refresh or a miss.

`node_groups`:

| Column | Type | Constraint | Meaning |
|---|---|---|---|
| `group_id` | `INTEGER` | `PRIMARY KEY` | Surrogate key, referenced by `node_runs` |
| `entry_id` | `INTEGER` | `NOT NULL`, `FOREIGN KEY` `ON DELETE CASCADE` | The entry; (`entry_id`, `group_id`) is `UNIQUE` too, the target of `node_runs`' foreign key |
| `virtual_path` | `TEXT` | `NOT NULL`, `UNIQUE` with `entry_id` | The source file of the nodes (`VirtualFilePath`); the nodes without a file share the empty path |
| `node_count` | `INTEGER` | `NOT NULL`, `CHECK (node_count > 0)` | How many nodes `nodes` holds |
| `content_sha256` | `TEXT` | `NOT NULL` | The SHA-256 of the uncompressed JSON of `nodes` |
| `generation` | `INTEGER` | `NOT NULL` | The generation of the save that last wrote the row |
| `nodes` | `BLOB` | `NOT NULL` | gzip of the JSON array of the group's nodes, in tree order |

`node_runs`:

| Column | Type | Constraint | Meaning |
|---|---|---|---|
| `entry_id` | `INTEGER` | `NOT NULL`, `FOREIGN KEY` `ON DELETE CASCADE` | The entry |
| `position` | `INTEGER` | `NOT NULL` | The rank of the run in its entry, from `0` |
| `group_id` | `INTEGER` | `NOT NULL`; (`entry_id`, `group_id`) `FOREIGN KEY` → `node_groups (entry_id, group_id)`, indexed (`ix_node_runs_group`) | The group the run takes its nodes from |
| `node_count` | `INTEGER` | `NOT NULL`, `CHECK (node_count > 0)` | How many nodes the run takes |

The tree's node list is not grouped by file: the nodes of one file can come back after
another file's. `node_runs` keeps that order: the load reads an entry's runs by `position`
and each takes the next `node_count` nodes of its group, so the tree reads back in the order
it was saved. A run that asks for more nodes than its group holds, or a group whose nodes no
run takes up, makes the load a miss. The composite foreign key forbids a run to name a group
that does not exist or belongs to another entry, and forbids deleting a group a run still
names: a save deletes the runs before the groups. The check runs at the end of each
statement, so the cascade from an entry deletes its runs and groups together.

A save is one transaction:

1. The entry of the key is upserted (`ON CONFLICT (cache_key) DO UPDATE`): a new key starts
   at generation `1`, a known one adds one; `saved_at` and `used_at` take the current time.
2. The entry's `indexed_files` and `package_markers` are rewritten.
3. The nodes are grouped by file. A group whose `content_sha256` is unchanged is left as it
   is; a changed or new group is upserted (`ON CONFLICT (entry_id, virtual_path) DO UPDATE`)
   with the current generation; a group whose file is gone is deleted after the runs.
4. The entry's `node_runs` are rewritten.
5. The entries of another `runtime` are deleted, then the entries past `MaxEntries`, by
   `used_at` from the most recent, their rows with them.

After the commit, a database whose free pages are more than half of its pages (a large entry just
dropped) is compacted with `VACUUM`, so the file shrinks back on disk; below that, the next save
reuses the free pages. A compaction that fails (a busy database, no room for the copy) leaves the
saved entries as they are, and the next save tries again.

A refresh that reindexes three files therefore rewrites three groups, not the whole tree.
A load that fails (a file that is not a SQLite database, a missing table, a blob that does
not decompress or deserialize, runs and groups that disagree) is a miss. A save that fails on a
SQLite error deletes the database with its `-wal` and `-shm` files and tries once more.

#### File stamps

Discovering the files hashes each of them, which reads every byte of the root at every call.
As `git status` does, the builder hands the discoverer the stamps it saved: a file whose size
and last write time equal its stamp keeps the saved SHA-256 without being read (the
discoverer still checks that the VFS lets it read the file). A stamp keeps the write time
only when it is older than the start of the discovery by more than two seconds; a file
written within that window, or later, is stored with `modified_ticks` `NULL` and hashed
again at the next call, so a change made during a build, or within the clock's resolution,
is never hidden. The stamp cannot see a change that keeps both the size and the write time
(a tool that restores the time); `use_cache: false` rebuilds from the content.


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
- **Bound** — the reindex replaces the nodes and edges of each changed file and reuses
  every other node, so its cost is proportional to the changed files, not to the
  repository: one file of 2 000 lines reindexes in under 2 s. `incremental_reindex`
  reports the index totals (`node_count`, `edge_count`), the files parsed and the
  engine's elapsed time.
- **Failure barrier** — a failed refresh serves the current (stale) index and keeps the
  dirty debt for the next attempt: stale results beat a dead search.

Historical note: an earlier version of this document described three watcher-driven
synchronization modes (`frozen`/`live`/`breakOnChange` via `RaggableTreeIndexMode`).
Those modes were never consumed by any code — the enum existed, nothing read it, and it
was removed with the `IndexMode` key (GAP-15). The
freshness design above replaces that fiction; `ICodebaseWatcher`
(`FileSystemWatcherCodebaseWatcher`, which no shipped host registers) remains available for
hosts that want push-based invalidation on top of the lazy pass.

## Selectors

`node_outline` and `node_read` address a declaration of one file by a selector, in one of three forms:

- **Readable**: `Ns.Type.Member(paramTypes)`, e.g. `Demo.Calc.Add(int)`. The parameter types are optional and required only when overloads exist; an ambiguous overload is an error that lists the candidate selectors.
- **Markdown**: `Section/Sub` (heading path), served for `.md` and `.markdown` files: a section runs from its heading to the line before the next heading of the same or a higher level. Sibling headings of the same name under one parent are told apart by a 1-based ordinal on any segment: `A/Notes[2]`, `A[2]/Notes[1]`. Without an ordinal, an ambiguous path is an error listing `A/Notes[1]`, `A/Notes[2]`; the outline shows the ordinal form for duplicates (and `A[2]/Notes` for their descendants) and the plain form for a unique name. An ordinal of 0 or beyond the siblings is a not-found error.
- **Fallback**: `@ts:<node-path>`, the tree-sitter path `root/<index>/<kind>/...` (`index` the 0-based position among the named children), for a node the readable form cannot name.

A selector resolves to exactly one node or fails listing candidates. A node's span includes its leading comments and its attributes or decorators.

Shipped grammars (`LanguageRegistry.Shipped`): typescript (`.ts`, `.tsx`), python (`.py`), csharp (`.cs`), go (`.go`), rust (`.rs`), javascript (`.js`, `.jsx`, `.mjs`, `.cjs`), java (`.java`), c (`.c`), cpp (`.cpp`, `.cc`, `.cxx`, `.hpp`, `.hh`, `.hxx`, `.h`), php (`.php`), ruby (`.rb`), bash (`.sh`, `.bash`), markdown (`.md`, `.markdown`). A `.h` header is read as C++, a superset that parses C headers too. The registry also loads tsx, agda, css, embedded-template, haskell, html, jsdoc, json, julia, ql, razor, scala and verilog: no adapter indexes them and discovery does not claim their files. OCaml (no macOS build in the package) and the Windows-only swift, toml and tsq grammars are pruned at publish.

The node tools and the structural tools read a file in any grammar of `LanguageRegistry.Parseable`: the indexed ones above, with `.tsx` read by the `tsx` grammar, then tsx (`.tsx`), agda (`.agda`), css (`.css`), embedded-template (`.erb`, `.ejs`), haskell (`.hs`), html (`.html`, `.htm`), json (`.json`), julia (`.jl`), ql (`.ql`, `.qll`), razor (`.cshtml`, `.razor`), scala (`.scala`, `.sc`) and verilog (`.v`, `.sv`, `.vh`, `.svh`); jsdoc claims no extension. A file whose extension no grammar claims is refused by name. The readable form names declarations in C#, TypeScript, JavaScript, Python, Go (a method is `Type.Method`), Rust (the functions of an `impl` block sit under its type), Java, C and C++ (by declarator: `n::A::g` for an out-of-line definition), PHP, Ruby, Bash, Scala, Julia and JSON (the key path, `a.b`); sections in Markdown. A file in which no declaration kind is known (CSS, HTML, Haskell, Verilog...) is outlined by the `@ts:` paths of its top-level nodes, through a lone wrapping node (`@ts:document/0/element/2/element`). A method's receiver (Rust `self`, Python `self` or `cls`) is not one of its parameter types.

`node_outline` lists the members with selector, kind, byte range and line range. `node_read` returns the text of one member, its range and the SHA-256 of its bytes, uncut: the tool carries its own result bound (`node_read` = 256 000 characters in `AgentDefaults`), so the default tool-result cut does not apply.

## Structural editor

`node_replace` replaces one node (selector from `node_outline`, hash from `node_read`) through `StructuralEditEngine`:

- **Byte splice**: only the node's bytes change; the rest of the file is byte-identical.
- **Stale-hash refusal**: the node's current SHA-256 is compared with `expected_hash` before any write; a mismatch is refused with a `StaleHashException` naming the expected and the actual hash.
- **Re-parse validation**: the result is parsed again and diagnostics are compared by position; a replacement is refused when it introduces a diagnostic that was not there before (Markdown has no parse validation).
- **Replaced span**: it includes the node's leading comments and attributes, so a replacement that omits them removes them. A Markdown section replaces its sub-sections too.
- **Line endings** of the replacement are normalised to the file's.
- **Write**: guarded by a backup copy restored when the write fails. It is not atomic.
- A refusal (stale hash, parse error, read-only mount, path outside the agent working root) is a failed result with the file untouched; a successful write marks the index dirty.

## Intent operations

`node_ensure` and the `op` field of `edit_batch` apply one idempotent operation; each is a no-op that writes nothing and reports `applied: false` when its target already holds.

| Operation | Parameters | Idempotence rule | Placement rule |
|---|---|---|---|
| `ensure_using` | `name` | the using is already present | in the using block, sorted place |
| `ensure_import` | `name`, `module` | the import is already present | in the import block |
| `ensure_field` | `selector` (type), `text` | a member of that name exists | next to the type's fields |
| `ensure_member` | `selector` (type), `text` | a member of that name exists | next to the type's members |
| `ensure_section` | `name` (heading path), `level?` | every heading of the path exists | missing headings appended at the end, each at the level of its depth |
| `insert_under_heading` | `selector` (heading), `text` | none: always appends | at the end of the section, before the next heading of the same or higher level |
| `replace_mermaid` | `selector` (section), `index` (0-based), `text` | the fence body is already `text` | only the body of the n-th `mermaid` fence (``` or ~~~) of the section and its sub-sections changes; fewer fences: refused, file untouched |

## Graph kinds

`GraphKindNames` (`Orkeon.Analysis.Abstractions`) maps lowercase workflow names onto the existing enums.

| Edge name | `EdgeKind` |
|---|---|
| `contains` | `Contains` |
| `calls` | `Calls` |
| `imports` | `Imports` |
| `inherits` | `Extends` + `Implements` |
| `tests` | `Tests` |
| `documents` | `Documents` |
| `project_depends` | `ProjectDepends` |

| Node name | Graph node |
|---|---|
| `file` | file node (`Module`) |
| `project` | `Package` node of a marker csproj or package.json |
| `namespace` | `Namespace` |
| `type` | class, interface, trait, struct, enum, record, type |
| `member` | function, method, constructor, destructor, operator, property, field, constant, variable |
| `doc_section` | `Section` |
| `test` | `Method` node carrying the `test` tag |

`EdgeKind` gains `Tests`, `Documents` and `ProjectDepends` (flags `1<<5` to `1<<7`). There is no `references` kind: nothing produces it. `documents` edges link a Markdown section to the symbols it names (see [Graph queries](#graph-queries)).

### Using the names

- `edge_kinds` and `node_kinds` of `dependency_graph` and `sub_graph` take the lowercase workflow names. Legacy enum names (`Imports`) are still accepted, case-insensitively. An unknown name is refused, naming it. Responses keep the enum names.
- When `node_kinds` is given, `dependency_graph` still honours `Scope`: the kinds and tags go into the node query, and `MaxNodes` bounds the result.

### How the graph is built

- `ProjectGraphBuilder` reads each csproj the discoverer already found (one read per csproj) and turns each `ProjectReference` into a `project_depends` edge (`\` and `..` are normalised), plus `Contains` edges from parent to child.
- `TestEdgeLinker` tags `[Fact]` and `[Theory]` methods (and the owner of a TypeScript `test` or `it` call) `test`, and links `tests` edges to the symbols they call in referenced projects.
- Both run on the full build and on the incremental reindex, where they run after the fingerprinters.

### Limits

- The FQN is name-only: a `tests` edge to an overloaded member links every overload.
- A test calling a symbol newly added in another file gets no `tests` edge until a full reindex.
- A TypeScript test callback has no node: its calls are owned by the enclosing symbol, else by the file node. Today only the tag is applied to the file node; the `tests` edges of a file-level TypeScript test are not yet produced.

## Graph queries

Four Read-class tools answer questions over the graph without reading the code. All of them report names they could not resolve in `unresolved`.

### `affected_tests`

Input: `changed_files` or `base` (a git ref whose diff gives the files); with neither, `base` is `HEAD`, i.e. the working-tree changes. Response: `all`, `test_projects` (folders), `test_classes` (reached through `tests` edges), `unresolved`.

- A neutral extension (`.md`, `.txt`, `.png`, ...) is exempt. `.claude/` and the work folder are skipped.
- `all` is `true` when a non-neutral file is not in the index, has no owning project, or when the expansion is truncated.
- Limits: `project_depends` comes from csproj files only; TypeScript tests are tagged but carry no `tests` edges; a cross-file `tests` edge exists only after a full reindex.
- Parity: the answer matches the harness script `affected-tests.py`.

### `batch_anchors`

Input: `action`, then `fqns` / `files` for `closure`, or `a` / `b` (each `{fqns, files}`) for `overlap`.

- `closure`: the nodes contained down from the anchors, plus their callers at depth 1, plus the inbound `tests`. Response: `nodes`, `unresolved`, `truncated`.
- `overlap`: lists the nodes both closures share in `shared`, or sets `disjoint`. Response also carries `unresolved_a`, `unresolved_b`, `truncated`.
- A truncated closure, or an unresolved anchor on either side, never proves disjointness; an unresolved anchor present on both sides is listed in `shared`.

### `documented_in` and `documents_of`

- `documented_in` (`fqn`): `sections[]` with `path`, `heading_path`, `start_line`.
- `documents_of` (`path`): `symbols[]` with `fqn`, `kind`, `file` (repo-relative), `heading_path`.
- The `Documents` edge goes from a `Section` to a symbol. It is built from code spans, link texts and headings equal to a symbol's local name, resolved by local name: an ambiguous name links every candidate. `DocumentsEdgeLinker` builds the edges on the full build and on the incremental reindex.
- The Markdown mapper tags a `Section` `code:<text>` or `link:<text>` for each such span or link.

## Structural search

`structural_search` (read-class) finds syntax nodes by pattern or by rule. The engine is native (no ast-grep binary): each candidate file is parsed again by the matcher, so a result never comes from a stale index. Give exactly one of `pattern`, `rule` (an object) or `rule_file`.

The `language` is any grammar of `LanguageRegistry.Parseable` or an alias (`cs`, `ts`, `js`, `c++`, `rb`, `sh`, `md`); it chooses the files scanned by extension, and a `typescript` rule scans `.ts` and `.tsx`. A pattern is parsed with that grammar: a PHP pattern needs no `<?php` tag, a JSON pattern is a value whose metavariables stand for values (`{"name": $N}`).

Rule subset:

| Key | Meaning |
|---|---|
| `kind` | The node kind (tree-sitter grammar name) |
| `pattern` | A code pattern with metavariables |
| `regex` | A regular expression on the node text |
| `field` | The field name the node holds in its parent |
| `inside` | The node has a parent matching the sub-rule: the direct parent unless `stopBy: end` |
| `has` | The node has a child matching the sub-rule: a direct child unless `stopBy: end` |
| `not` | The sub-rule must not match |
| `all` / `any` | Every / at least one of the sub-rules matches |
| `stopBy` | Where `inside` / `has` stop climbing or descending: `neighbor` (default), `end`, or a rule object |

Any other key (`follows`, `precedes`, `matches`, `constraints`, `transform`, …) is refused by name.

Metavariables: `$A` captures one node, and a repeated name must match equal text; `$$$ARGS` captures a sequence of siblings. Captures are returned with each match.

Languages: C# (`cs`), TypeScript (`ts`), Markdown (`md`) and the other grammars the index ships. In Markdown the kinds are `heading`, `fence`, `link` and `front_matter`; `regex` applies to the heading text or the fence info string, and neither `pattern` nor `field` is available (`field` is refused).

A YAML rule file (`id`, `language`, `rule`, `message`, `severity`, `fix`) is read through the VFS under a mounted root; `fix` is read, never applied.

Results are capped by `max_results` (default 100) and `max_files` (default 500); the result reports `truncated` and `files_scanned`. Each match carries path, line, column, text, the enclosing symbol and its captures.

`node_insert` adds a node at a position against an anchor selector:

- **`inside`** appends the text as the last member of a braced body (a class, struct, interface, namespace, impl or JSON object), after the last member of an indented or `end`-closed body (Python, Ruby), or at the end of a Markdown heading's section; a C#, Rust or C enum member and a JSON pair get the separating comma. Indentation is the first sibling's, else the parent's plus the file's detected unit (spaces or tabs); an empty body uses the second rule.
- **`before`** inserts above the anchor's leading comments and attributes, **`after`** below the anchor; multi-line text is re-indented line by line to the anchor's indentation. No anchor hash is needed for `before`/`after`.
- The result is re-parsed as for `node_replace` and refused when it introduces a diagnostic.

`edit_batch` applies `node_replace` and `node_insert` operations over several files as one transaction (`EditTransaction`):

- `expected_hash` is required for `node_replace`, absent for `node_insert`. Operations on one file apply in order on the progressively edited bytes: the second hash is checked against the state after the first.
- The Write right of every path is checked before any write; every file is re-parsed in memory (`validate: "parse"`) before the first write.
- Before the first write a journal `/output/edit-batch/<id>.journal` keeps every file's original bytes; it is deleted on success, and the next `edit_batch` on the mount replays a leftover one. A later write failure restores the files already written from memory; if that restore fails too, the journal stays and the call throws an `AggregateException`. A concurrent `edit_batch` touching a held file is refused (the lock is in-process).
- `validate: "build"` writes the files, then `DotnetBuildValidator` builds the nearest `.csproj`/`.sln` above the edited files (`dotnet build --no-restore`, timeout, process killed on cancel). A red build restores the original bytes; with no project found the check is skipped and reported.
- The result lists each operation's path and range, and the validation outcome.

## Structural rewrite

`structural_rewrite` (Edit class) rewrites every node a `pattern` or `rule` matches with a `fix` template, over the files of `paths` / `glob` (bounded by `max_files`).

- `dry_run` defaults to `true`: nothing is written, the result carries a per-file unified diff, match count and applied count.
- Each rewritten file is re-parsed in memory; a file whose rewrite does not parse is refused alone (`refused_reason`), the others are still applied.
- Every grammar `structural_search` serves except Markdown, which is refused.

## Extensibility

### Adding a language

Implement `ILanguageAdapter` (`Orkeon.Analysis.Abstractions.Interfaces`): provide `LanguageName`, `FileExtensions`, the seven Tree-sitter queries (`DeclarationQuery`, `ImportQuery`, `CallQuery`, `InheritanceQuery`, `DocCommentQuery`, `DecoratorQuery`, `StatementQuery`), the `MapNodeKind` and `MapStatementKind` mappings, and the `ExtractSignature` / `ResolveImportPath` extractors (`RefineKind`, `ResolveDocComment`, `ExtractName` and `GetExtraModifiers` have default implementations). Register the adapter via DI (`services.AddSingleton<ILanguageAdapter, MyAdapter>()`): the builder and the incremental engine take every registered `ILanguageAdapter`, next to the twelve built-ins (`LanguageAdapterFactory`: TypeScript, Python, C#, Go, Rust, JavaScript, Java, C, C++, PHP, Ruby, Bash); an adapter registered after `AddRaggableTree` with a built-in's `LanguageName` replaces it. A runnable Java adapter, written before Java was built in and kept as a minimal example of the contract, lives in [`examples/raggable-tree/custom-adapter/`](https://github.com/Orkeon/orkeon/blob/main/examples/raggable-tree/custom-adapter/README.md).

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
- The import resolver is heuristic for dynamic languages (Python, TypeScript, JavaScript, Ruby, PHP): re-exports and monkey patches can produce `UnresolvedRef`.
- File import edges come from relative paths only: Java `import`, PHP `use`, Ruby `require` (as opposed to `require_relative`) and a C `#include <...>` name a package, a namespace or a system header, not a file, and draw no edge to a file of the tree; a Bash `source` whose path holds a variable is not resolved. A PHP path starting with `/` (`__DIR__ . '/lib/a.php'`) is read from the file's directory.
- Edges resolve within a graph group (`DependencyGraphBuilder.GraphGroupOf`): C and C++ share one, so a `.c` file reaches the `.h` header it includes (parsed as C++) and the functions declared there; JavaScript and TypeScript share another. The other languages resolve each in their own.
- The summarizer is C#-only (`Summarizer.Provider` + an `ILlmProvider`); OpenAI/Ollama embeddings need their endpoint, while the runner host's on-device default needs nothing. The pipeline works without embeddings (`codebase_search` then ranks by BM25 only).
- The index is process-local: `InMemoryRaggableStore` is rebuilt by `index_codebase` in each process, and the optional vector-store mirror is never read back.
- `FileSystemWatcherCodebaseWatcher` (the `ICodebaseWatcher` implementation, registered by no shipped host) relies on `System.IO.FileSystemWatcher` — on Linux/WSL, rename events may arrive decomposed (observed as Deleted then Created).
- The index cache refreshes in place only when no `root_alias` is set and at most half of the files changed; otherwise it rebuilds from scratch. A file whose content is unchanged keeps its saved nodes even when a file it imports changed: the refresh relinks the edges, it does not parse the file again. The database keeps `MaxEntries` (3) keys of the current runtime: a fourth evicts the least recently used, and an entry of another runtime is dropped at the next save. A file stamp does not see a change that keeps both the size and the write time; `use_cache: false` rebuilds from the content.

---

> **See also**: [RaggableTree ADR](./raggable-tree-adr.md) · [Tool inventory](../tools/inventory.md) · [Back to index](../INDEX.md)
