> 🇫🇷 [Version française](../fr/architecture/raggable-tree-adr.md)

> **See also**: [RaggableTree guide](./raggable-tree.md) · [Back to index](../INDEX.md)

# ADR — RaggableTree semantic graph as the agents' context substrate

**Status**: Accepted · **Date**: 2026-04 · **Scope**: Orkeon.Analysis + Orkeon.Tools.Analysis

## Context

Orkeon agents reason over real codebases that frequently exceed hundreds of thousands of lines across several languages (TypeScript, C#, Python, Go, Rust). Three hard constraints:

1. **Token budget** — The LLM context (even 1M tokens) cannot absorb a monorepo; a compact index is needed that answers "which symbol is affected by X?" without reloading the sources.
2. **Variable granularity** — Depending on the task, an agent may need a package view, a method snippet, a call chain, or a complexity measurement. A single representation (raw AST, flat embeddings, Markdown documents) does not cover all these cases.
3. **Iteration cost** — A project evolves continuously; rebuilding the full index on every commit is unacceptable (measured: 20× slower than a targeted incremental reindex).

The available naive solutions (file reading via `file_read`, full-text `grep`, RAG search over arbitrary chunks) were evaluated and deemed insufficient: they do not preserve semantic relations (imports, calls, inheritance) and force the agent to rediscover the structure on every query.

## Decision

Build a **6-level stratified semantic graph** fed by Tree-sitter, exposed to agents through 13 structural tools.

The six levels (`NodeLevel`):

- **L0 Monorepo** — root of the indexed project
- **L1 Package** — build unit detected by marker (`package.json`, `*.csproj`, ...)
- **L2 Module** — source file
- **L3 Symbol** — class, interface, function, method, enum, property, constant
- **L4 Statement** — if/try/loop/return extracted for flow and CFG queries
- **Edges** — Imports, Calls, Extends, Implements, Contains

Seven key components implement the pipeline:

- `ILanguageAdapter` — per-language adaptation (Tree-sitter queries + import heuristics)
- `RaggableTreeBuilder` — initial build (6 phases)
- `IncrementalReindexEngine` — diff reindexing (changed files only)
- `IRaggableStore` — queries over the built graph
- `IEmbeddingProvider` + `IEmbeddingTextComposer` — semantic search
- `IFrameworkFingerprinter` — tagging nodes by framework (Angular, NestJS, ASP.NET, Flask, FastAPI)
- `ICodebaseWatcher` + `IRaggableTreeEventBus` — real-time synchronization

## Rejected alternatives

### Full LSP (Language Server Protocol)

Integrating one LSP server per language (tsserver, OmniSharp, pylsp, ...) provides very precise information about symbols and references.

- **Rejected because**: it requires one runtime per language (Node.js for tsserver, MSBuild for OmniSharp, a Python venv for pylsp) and long-running process orchestration. Impractical for a batch indexing pipeline on CI or on a server without the SDKs installed.

### Roslyn-only (C# only)

Roslyn provides a perfect AST and a resolved symbol graph for C#.

- **Rejected because**: porting Python/JavaScript/Go/Rust would be a major functional loss. The target is a multi-language framework by default.

### Full-text search + flat embeddings

Split each file into 500-token chunks, vectorize them, store them in a vector store, and let agents do semantic search only.

- **Rejected because**: it loses the structural relations (who calls what, who inherits from what, which imports). The `flow_trace`, `impact_analysis`, `sub_graph` tools become impossible. Answer quality drops on questions of the form "if I change X, what breaks?".

### Regex/ctags over AST

The fastest option to implement but the most fragile: it does not distinguish real calls from mentions in comments, does not resolve imports, and misses modern constructs (async/await, decorators, spread).

- **Rejected because**: technical debt grows with every new language or framework. Tree-sitter offers an officially maintained grammar for 40+ languages.

## Consequences

### Positive

- **Compact context** — `ICodebaseContextProvider` produces a summary < 500 tokens (markdown) or < 200 tokens (compact) that can be injected into all agents.
- **Typed queries** — The 13 tools replace dozens of lines of "read this file and tell me..." prompting with deterministic calls.
- **Incremental reindexing** — The engine re-parses only the changed files (measured target: < 5% of the initial cost).
- **Multi-language by construction** — Adding a language = implementing an `ILanguageAdapter`, not extending a C#-only pipeline.

### Negative

- **Large API surface** — 7 interfaces + 5 adapters + 13 tools = 25 extension points to understand before extending the system. Mitigated by the `docs/architecture/raggable-tree.md` docs and the present ADR.
- **Tree-sitter as a native dependency** — The parsers are compiled in C; musl or ARM32 builds may require work. Mitigated by `TreeSitter.DotNet`, which wraps the compilation for most target platforms.
- **Memory cost** — A 100k-symbol monorepo with 1536-dim embeddings weighs ~600 MB in memory. Mitigated by the option of using an external vector store (Redis/LanceDB) rather than `InMemoryRaggableStore`.
- **Embedding provider optional but strategic** — Without embeddings, `codebase_search` and `semantic_search` are disabled. The pipeline remains useful for the structural tools (map, detail, graph, flow) but loses its semantic component.

### Accepted risks

- TypeScript re-exports (`export { Foo } from './bar'`) and dynamic Python imports can produce `UnresolvedRef`. Decision: document the limitation rather than build a heavy resolver in V1.
- The JSON cache is unversioned and therefore invalid across adapter changes. Decision: recompute on demand, with no automatic migration.

## Amendment — 2026-08-18

The tool surface has since grown from the 13 tools this record describes to
**15** (`Orkeon.Tools.Analysis`); the derived "25 extension points" figure is
now 27. The counts above are kept as written — this ADR is a frozen decision
record; [the RaggableTree guide](./raggable-tree.md) is the authoritative,
maintained inventory.

---

> **See also**: [RaggableTree guide](./raggable-tree.md) · [Back to index](../INDEX.md)
