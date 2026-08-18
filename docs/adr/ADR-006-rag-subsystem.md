> 🇫🇷 [Version française](../fr/adr/ADR-006-rag-subsystem.md)

> **See also**: [ADR-002 — Tool abstractions shared kernel](./ADR-002-tool-abstractions-shared-kernel.md) · [ADR-003 — Secondary shared kernels](./ADR-003-shared-kernels-secondaires.md) · [ADR-004 — Scripting naming twins](./ADR-004-jumeaux-de-nommage-scripting.md) · [Back to the index](../INDEX.md)

# ADR-006 — RAG subsystem: `Rag.Abstractions` shared kernel and `Rag` DI wiring

**Status**: Accepted · **Date**: 2026-07 · **Scope**: `Orkeon.Application` → `Orkeon.Rag.Abstractions`; `Orkeon.Infrastructure` → `Orkeon.Rag`

## Context

The RAG feature set is being promoted from `Orkeon.Infrastructure/Knowledge` +
`Orkeon.Application/{Interfaces/Rag,Rag}` to a first-rank subsystem `src/rag/`, on the proven
model of `src/analysis/` (RaggableTree — see [ADR-003](./ADR-003-shared-kernels-secondaires.md)):

- **`src/rag/Orkeon.Rag.Abstractions`** — contracts, DTOs, and options
  (`IChunkingStrategy`, `IDocumentLoader`, `IDocumentStore`, `IQueryTransformer`, `IReranker`,
  `IRetrievalEvaluator`, `IGroundednessChecker`, `IQueryComplexityClassifier`,
  `IIngestionPipeline`, `IRagPipeline`, `RagAnswer`…). Depends on `Orkeon.Domain` **only**.
- **`src/rag/Orkeon.Rag`** — implementations (chunkers, loaders, retrieval, reranking,
  ingestion, evaluation) and named-component factories.
- **`src/tools/Orkeon.Tools.Rag`** — agent tools (`rag_search`, `rag_ingest`, `rag_eval`),
  in the `Tools.*` family per [ADR-004](./ADR-004-jumeaux-de-nommage-scripting.md)
  (`Orkeon.Tools.Rag`, **not** `Orkeon.Rag.Tools`).

Two cross-onion couplings are needed for the subsystem to plug into the core, exactly as with
RaggableTree. This ADR enacts them **ahead of realization**: the project skeleton and contracts
land first (RAG-02 / C1-C2); the references below are added by the subsequent migration batches.

1. **`Orkeon.Application → Orkeon.Rag.Abstractions`** — the Application layer needs the RAG
   ports (e.g. `IRagPipeline` for crew/agent knowledge injection) without seeing any
   implementation.
2. **`Orkeon.Infrastructure → Orkeon.Rag`** (concrete, not just the abstractions) —
   **exclusively** as composition wiring confined to a single DI file
   (`DependencyInjection/RagInfrastructureExtensions.cs`, mirror of
   `RaggableTreeInfrastructureExtensions.cs`), notably to wrap embedding calls in
   `LlmLoggingDelegatingHandler`.

## Decision

- `Orkeon.Rag.Abstractions` is a **secondary shared kernel** (same status as
  `Tools.Abstractions` in [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md) and
  `Analysis.Abstractions` in [ADR-003](./ADR-003-shared-kernels-secondaires.md)): an
  abstractions project depending only on `Domain`, hence consumable by `Application` with no
  cycle and no inversion of the dependency direction.
- The concrete `Infrastructure → Orkeon.Rag` reference is accepted as **composition wiring**
  localized to a single DI extensions file; the Infrastructure assumes its composition-root role
  for the RAG subsystem. `AddOrkeonRag()` is an explicit **opt-in** (auto-sufficient,
  `TryAdd*` everywhere — the host wins), never called unconditionally from
  `AddOrkeonInfrastructure`.
- **Plugin type identity**: `Orkeon.Rag.Abstractions` is listed in
  `OrkeonPluginsOptions.SharedAssemblyPrefixes` so rerankers/chunkers/loaders contributed by
  plugins keep a single type identity across `AssemblyLoadContext` boundaries.

## Consequences

- **Positive**: the RAG contracts get RaggableTree-grade visibility, a clean opt-in, and plugin
  extensibility; the couplings are traceable and challengeable instead of renegotiated at every
  review.
- **Vigilance**: `Orkeon.Rag.Abstractions` must **keep depending on `Orkeon.Domain` alone** —
  enforced by `tests/rag/Orkeon.Rag.Abstractions.Tests/ArchitectureTests.cs`. Any extension of
  the concrete `Orkeon.Rag` usage in the Infrastructure beyond the single DI wiring file must
  reopen this ADR.
- **Break**: the legacy namespaces (`Orkeon.Application.Interfaces.Rag.*`,
  `Orkeon.Application.Rag.*`, `Orkeon.Infrastructure.Knowledge.*`) are removed without shims
  (assumed break, version `0.9.x-beta`; migration table in `CHANGELOG.md`).
  **Done in RAG-02/C5 (2026-07-25)** — `rag_search` now lives in `Orkeon.Tools.Rag`
  (`RagSearchTool` + `AddOrkeonRagTools()`), and the subsystem opt-in is
  `AddOrkeonRag(configuration)` in `Orkeon.Rag.DependencyInjection`.

## Amendment — 2026-07-25 (RAG-02/C3)

The migration batch that ports the implementations into `Orkeon.Rag` adds two outbound
couplings **of the concrete `Orkeon.Rag` project** (never of `Orkeon.Rag.Abstractions`,
whose Domain-only rule is unchanged):

1. **`Orkeon.Rag → Orkeon.Application`** — `Orkeon.Rag` is an outer-ring implementation
   project (same ring as `Orkeon.Infrastructure`) and consumes the Application ports
   directly: `Orkeon.Application.Interfaces.Ports.IEmbeddingProvider` (canonical embedding
   interface, plan §4.1) for the ingestion/query pipelines, and the
   `Orkeon.Application.Interfaces.Security` validation contracts (`IDataValidator`,
   `IProvenanceTracker`, `DataValidationResult`…) for the ingestion-path validation.
   This follows the onion direction (outer ring → Application) and creates no cycle:
   `Application` references `Rag.Abstractions` only, never `Orkeon.Rag`.
2. **`Orkeon.Rag → Orkeon.Analysis.Abstractions`** — hosts `AnalysisEmbeddingProviderAdapter`
   (moved out of `Orkeon.Infrastructure/LLMs/Embeddings/`), the bridge from the Analysis
   embedding abstraction to the Application port ("in `Orkeon.Rag`, which references both
   worlds", plan §4.1).

Vigilance note: besides the DI wiring file, `Orkeon.Infrastructure` currently also uses
`Orkeon.Rag.Embeddings.AnalysisEmbeddingProviderAdapter` from
`LLMs/Embeddings/DefaultEmbeddingProviderResolver.cs` — composition-time resolution logic
invoked by `AddOrkeonInfrastructure`. It is accepted as part of the composition-root role;
any use of `Orkeon.Rag` from Infrastructure **runtime** code (non-composition) still requires
reopening this ADR.

## Amendment — 2026-07-26 (RAG-06)

The corrective phase layers four decisions on top of this ADR without touching its
dependency rules (`Orkeon.Rag.Abstractions` stays Domain-only — verified by
`ArchitectureTests`):

1. **CRAG on the Domain `StateGraph`, not a bespoke loop.** `CorrectiveRagPipeline`
   (`src/rag/Orkeon.Rag/Corrective/`) is built on `StateGraph<RagGraphState>`
   (`Orkeon.Domain.Graph`) — the same Graph orchestration mode crews use — with
   conditional edges on the retrieval verdict (`Correct|Incorrect|Ambiguous`) and a
   **double bound**: `Orkeon:Rag:Corrective:MaxIterations` plus the graph's own
   circuit breaker derived from it. Exhaustion degrades to a best-effort answer;
   the graph never throws at the caller.
2. **Web fallback split: policy vs transport.** Two independent, off-by-default
   switches: `Orkeon:Rag:Corrective:WebFallback` (the *policy* — may the graph
   leave the local store; lives in `Orkeon.Rag.Abstractions`, Domain+BCL-only) and
   `Orkeon:Rag:WebFallback` (the *transport* — `WebSearchRetrieverOptions`,
   SearxNG endpoint; lives in `Orkeon.Rag`, since `SuspiciousAction` and HTTP
   concerns would violate the Abstractions dependency rule). Both must be enabled
   for the `web_fallback` node to run, and every fetched page goes through
   `PromptInjectionDocumentValidator` before entering the working set.
3. **`corrective` profile without a linear rerank stage.** The preset disables the
   ONNX cross-encoder and the linear groundedness stage on purpose: the graph
   corrects by looping (evaluate → rewrite/refine → re-retrieve) and has a native
   `check_groundedness` node instead. The `adaptive` profile's `Iterative` route
   delegates to `corrective` (the RAG-05 interim fallback to `quality` is lifted).
4. **No separate `rag-adr.md`.** The RAG-06 batch deliberately keeps the decision
   record here (single ADR, amended per phase) instead of adding the
   `docs/architecture/rag-adr.md` page the task sheet sketched — a second decision
   document would duplicate this one and widen the FR-parity debt. The narrative
   architecture guide is `docs/architecture/rag-pipeline.md`.
