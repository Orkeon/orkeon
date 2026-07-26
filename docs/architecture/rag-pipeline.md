> **See also**: [ADR-006 — RAG subsystem](../adr/ADR-006-rag-subsystem.md) · [Memory system](./memory-system.md) · [Opt-in subsystems](../reference/opt-in-subsystems.md) · [Back to index](../INDEX.md)

# RAG pipeline

Retrieval-Augmented Generation subsystem (`src/rag/`): incremental ingestion with
security validation, a staged query pipeline (transform → retrieve → fuse → rerank →
assemble → generate → groundedness) whose every stage is traced, hybrid BM25 + vector
retrieval, pluggable rerankers, quality profiles (`fast` / `balanced` / `quality` /
`adaptive`), YAML crew integration with cited knowledge injection, and an offline
evaluation harness gated in CI.

## Overview

An agent that pastes whole documents into its prompt drowns its context and cannot
cite anything. The RAG subsystem precomputes and structures that knowledge:

- **Ingestion**: documents are loaded (files, CSV, HTML, PDF, web pages), validated
  against prompt injection, chunked, embedded, and upserted into a collection of the
  document store. A per-collection manifest makes re-ingestion incremental — an
  unchanged source costs zero embeddings.
- **Query**: a question flows through a fixed sequence of togglable stages and comes
  back as a `RagAnswer` — generated text with `[n]` citation markers, the citations
  resolving each marker to its source chunk (score and offsets included), and the
  full execution trace of the run.
- **Quality dials**: one knob (`Orkeon:Rag:Profile`) selects a preset; every value
  can then be overridden key by key through configuration. Profile = preset,
  configuration = override.

Three consumption surfaces share the same pipelines: agent tools (`rag_search`,
`rag_ingest`, `rag_eval` in `Orkeon.Tools.Rag`), the crew YAML `rag:` /
`knowledge:` blocks, and the scripting DSL `rag.*` namespace.

## Projects (ADR-006)

| Project | Role | Depends on |
|---|---|---|
| `Orkeon.Rag.Abstractions` | Contracts, DTOs, options (`IRagPipeline`, `RagAnswer`, `RagOptions`, `RagProfilePresets`…) | `Orkeon.Domain` only |
| `Orkeon.Rag` | Implementations: loaders, chunkers, validation, stores, retrieval, reranking, routing, evaluation, DI | `Application`, `Analysis.Abstractions` (see ADR-006) |
| `Orkeon.Rag.Onnx` | Opt-in ONNX cross-encoder reranker (ms-marco-MiniLM-L-6-v2), `AddOrkeonOnnxReranker()` | `Orkeon.Rag.Abstractions` |
| `Orkeon.Rag.Onnx.Model` | Companion package embedding the int8 model weights — guaranteed offline | — |
| `Orkeon.Tools.Rag` | Agent tools `rag_search` / `rag_ingest` / `rag_eval`, `AddOrkeonRagTools()` | `Orkeon.Rag.Abstractions` |

`Orkeon.Rag.Abstractions` is a secondary shared kernel (same status as
`Orkeon.Analysis.Abstractions`, [ADR-003](../adr/ADR-003-shared-kernels-secondaires.md)):
`Orkeon.Application` references it for the ports; `Orkeon.Infrastructure` references
`Orkeon.Rag` solely as DI composition wiring. The legacy
`Orkeon.Infrastructure.Knowledge` / `Orkeon.Application.{Interfaces.Rag,Rag}`
namespaces were removed without shims (migration table in `CHANGELOG.md`).

## Quick start

The subsystem is opt-in — nothing is wired by `AddOrkeonInfrastructure()` alone:

```csharp
services.AddOrkeonLocalEmbeddings();      // or any IEmbeddingProvider (local BGE: no API key)
services.AddOrkeonInfrastructure();       // semantic-first defaults for embeddings + chat
services.AddOrkeonRag(configuration);     // the subsystem (Orkeon.Rag.DependencyInjection)
services.AddOrkeonRagTools();             // optional: rag_search / rag_ingest / rag_eval tools
```

```csharp
var ingestion = provider.GetRequiredService<IIngestionPipeline>();
await ingestion.IngestAsync(new IngestionRequest
{
    Collection = "product-kb",
    Sources = [new SourceDescriptor { Location = "/kb/faq.md" }],
});

var rag = provider.GetRequiredService<IRagPipeline>();
var answer = await rag.QueryAsync(new RagQuery
{
    Text = "How many days do customers have to request a refund?",
    Collection = "product-kb",
    TopN = 3,
});
// answer.Text        — generated text with [n] markers
// answer.Citations   — marker -> chunk id, source id, snippet, score, offsets
// answer.Trace       — stages executed, variants, route, verdicts
```

The host must provide an `IEmbeddingProvider` and an `IChatClient`;
`AddOrkeonInfrastructure()` registers semantic-first defaults for both (resolution
order for embeddings: container-registered local/Analysis provider →
`Orkeon:Embeddings` configuration → fail-fast at first use, never silently).

Runnable, fully offline demos: `examples/rag/basic-ingestion`,
`examples/rag/hybrid-retrieval`, `examples/rag/custom-reranker`,
`examples/rag/crew-yaml`, and the scripting variant `examples/scripting/08-rag.ork.ts`.

## Contracts

All in `Orkeon.Rag.Abstractions` (`Interfaces/`, `Models/`, `Options/`):

| Contract | Role |
|---|---|
| `IRagPipeline` | Query façade: `RagQuery` → `RagAnswer` (citations + trace) |
| `IIngestionPipeline` | Ingestion façade: `IngestionRequest` → `IngestionReport` |
| `IDocumentStore` | Upsert / search / delete-by-source over a named collection |
| `IDocumentLoader` | Source → `RagDocument` (text, CSV, HTML, PDF, web page) |
| `IChunkingStrategy` | Document → `Chunk` slices (`recursive`, `sentence`, `structural`, `semantic`) |
| `IQueryTransformer` | Query → retrieval texts + a `QueryTransformKind` (see below) |
| `IReranker` | Candidates → best `topN`, reranker-scored (cascade CandidateK → TopN) |
| `IQueryComplexityClassifier` | Query → `QueryRoute` (`NoRetrieval` / `SingleShot` / `Iterative`) |
| `IRagProfileResolver` | Profile name → memoized pipeline instance |
| `IKnowledgeContextAugmenter` | Agent knowledge attachments → cited prompt block |
| `IRagCollectionsBootstrapper` | Crew `rag:` block → kickoff ingestion |
| `IRetrievalEvaluator`, `IGroundednessChecker` | Corrective hooks (verdicts, hallucination check — see [Corrective RAG](#corrective-rag-crag)) |
| `IRagEvaluator`, `IRagEvalHarness`* | Offline evaluation (metrics, golden datasets) |

\* the harness contract and its runner live in `Orkeon.Rag.Evaluation`; the
evaluator port is in the abstractions.

Named components (chunkers, transformers, rerankers) resolve through factories
following the `MemoryProviderFactory` pattern: names and aliases matched
case-insensitively, and an unknown name **always fails loudly** with the list of
known names — never a silent fallback.

## Staged query pipeline

`StagedRagPipeline` (default `IRagPipeline`) is a fixed sequence of togglable
stages driven by `RagOptions`:

```mermaid
flowchart LR
    Q[RagQuery] --> T[transform]
    T --> R[retrieve]
    R --> F[fuse]
    F --> RR[rerank]
    RR --> A[assemble]
    A --> G[generate]
    G --> GR[groundedness]
    GR --> ANS[RagAnswer<br/>text + citations + trace]
```

| # | Stage | What it does | Trace data (excerpt) |
|---|---|---|---|
| 1 | `transform` | Named transformer produces retrieval texts (`none` skips) | `mode`, `kind`, `variants`, `variant_n` |
| 2 | `retrieve` | `CandidateK` candidates **per retrieval text**; hybrid honoured per query by capable stores | `candidates`, `top_k`, `mode`, `hybrid`, `variants` |
| 3 | `fuse` | Per-kind combination of the per-text rankings + dedup by chunk id, then opt-in MMR | `method` (`rrf`/`union`/`dedup`), `in`, `out`, `mmr*` |
| 4 | `rerank` | Named reranker, cascade CandidateK → TopN (disabled: truncation in retrieval order) | `reranker`, `candidates`, `kept` |
| 5 | `assemble` | Token budget (≈ 4 chars/token) + anti-Lost-in-the-Middle `edges` ordering | `chunks`, `ordering`, `dropped_by_budget` |
| 6 | `generate` | Grounded generation with `[n]` markers (anti-hallucination system prompt) | `model` |
| 7 | `groundedness` | Optional post-generation verification hook (`IGroundednessChecker`) | `grounded`, `score` |

Every stage appends a `RagTraceStep` to `RagAnswer.Trace` — durations, counts, and
chosen component names are always observable. When retrieval yields no candidate,
generation is skipped and a deterministic no-context answer is returned (the
pipeline never lets the model answer ungrounded).

### Query transformers by kind

Transformers declare how their output texts combine at the `fuse` stage
(`QueryTransformKind`):

| Kind | Built-in | Semantics |
|---|---|---|
| `Union` | `multi-query` | Retrieve once per text (original + variants); merge by chunk-id union, original store scores kept — the maximum wins on duplicates |
| `Fusion` | `rag-fusion` | Retrieve once per text; fuse the rankings by Reciprocal Rank Fusion (same `RrfK` as the hybrid stage) |
| `Replacement` | `hyde` | The substitute text(s) are the retrieval probes **instead of** the question — generation and citations always use the original question |

Built-ins registered by `AddOrkeonRag`: `none`, `multi-query`, `rag-fusion`,
`hyde`. The LLM-backed ones resolve the host's `IChatClient` lazily; parsing of
the LLM's variants is tolerant, and an empty output degrades to the original
query.

### Anti-Lost-in-the-Middle assembly

With `Context.Ordering: edges` (the default), ranked chunks r1 (best) … rm are
laid out as `r1, r3, r5, …` from the head and `…, r6, r4, r2` closing the tail —
the two strongest chunks sit at the extremities where LLM attention is highest,
the weakest in the middle. Citation markers stay **rank-based** (`[1]` = best
chunk) whatever the layout. `linear` restores plain rank order.

## Hybrid retrieval (BM25 + RRF)

`AddOrkeonRag` **always** wraps the registered `IDocumentStore` — including a
host-registered one — in the `HybridSearchDocumentStore` decorator, so ingestion
feeds an in-process per-collection BM25 index alongside the vector store. Whether
a search actually fuses is decided **per query** (`RetrievalQuery.Hybrid`, set by
the profile presets), with `Orkeon:Rag:Retrieval:Hybrid:Enabled` as the default
mode; a disabled default is a strict behavioural passthrough.

Score provenance (`ScoredChunk.ScoreOrigin`):

1. `hybrid-native` — the backing memory provider implements
   `IHybridSearchCapable` (e.g. LanceDB): text + vector fused provider-side.
2. `rrf` — emulated path: inner vector ranking + in-process BM25, fused with
   Reciprocal Rank Fusion. RRF scores are rank aggregates, **not** similarities.
3. Inner origin (`vector` / `local-cosine`) — passthrough when the lexical side
   has nothing to contribute.

The in-process BM25 index only covers chunks upserted through the decorator in
the current process; at scale, prefer a provider with native hybrid search — the
decorator switches to it automatically.

## MMR diversification

Opt-in (`Orkeon:Rag:Retrieval:Mmr:Enabled`), applied at the `fuse` stage:
Maximal Marginal Relevance re-orders the fused candidates by trading relevance
against redundancy — each pick maximises
`λ·relevance − (1 − λ)·max-similarity-to-already-picked` (default `λ = 0.7`,
relevance-leaning). Candidate embeddings are not re-computed: the lexical
(Jaccard) similarity fallback is the honest default.

## Reranking

The `rerank` stage narrows `CandidateK` candidates (default 50) to `TopN`
(default 5) with a named `IReranker`:

| Name (aliases) | Implementation | Notes |
|---|---|---|
| `none` (`noop`) | `NoopReranker` | Truncation in retrieval order |
| `llm` (`listwise`) | `LlmListwiseReranker` | One listwise chat call; tolerant parsing; fallback when no ONNX package |
| `onnx` (`cross-encoder`) | `Orkeon.Rag.Onnx` | ms-marco-MiniLM-L-6-v2 cross-encoder, int8 weights embedded, fully offline; `AddOrkeonOnnxReranker()` |

Hosts contribute rerankers through `IRerankerRegistrar` — registered in DI,
applied when the singleton `RerankerFactory` is built, registration order
irrelevant (this is how the ONNX package plugs in; see
`examples/rag/custom-reranker` for a host-provided one). The factory itself is
`TryAdd`-registered: a host-registered `RerankerFactory` wins outright.

## Profiles

`RagProfilePresets` expands a profile name into a complete `RagOptions`; the
`Orkeon:Rag` configuration section then overrides any value individually.

| | `fast` (default) | `balanced` | `quality` | `adaptive` |
|---|---|---|---|---|
| Retrieval | vector only, `CandidateK = TopK` | hybrid BM25 + RRF, `CandidateK = 50` | hybrid, `CandidateK = 100` | routed (see below) |
| Rerank | off | ONNX cross-encoder 50 → 5 | ONNX cross-encoder | routed |
| Groundedness | off | off | on | routed |
| Needs | nothing | `Orkeon.Rag.Onnx` | `Orkeon.Rag.Onnx` | classifier + chat client |

`fast` is the out-of-the-box default because `balanced` requires the opt-in ONNX
package — defaulting to it would make every bare `AddOrkeonRag()` host fail
loudly at first query. Opt into `balanced` with one configuration line.

`IRagProfileResolver` (`ProfileRagPipelineResolver`) memoizes **one pipeline per
profile name**; the reserved name `default` resolves to the host's registered
`IRagPipeline`, so a host-provided pipeline keeps the last word for non-profiled
queries. Unknown names fail loudly with the list of known profiles.

## Adaptive routing (Adaptive-RAG)

The `adaptive` profile resolves to an `AdaptiveRagPipeline`: an
`IQueryComplexityClassifier` routes each query first, and the decision is always
traced (`RagTrace.Route` + a `route` step carrying classifier, route, delegate).

```mermaid
flowchart LR
    Q[query] --> C{classifier}
    C -- NoRetrieval --> D[direct LLM answer<br/>no citations]
    C -- SingleShot --> B[balanced pipeline]
    C -- Iterative --> QU[quality pipeline<br/>documented fallback]
```

Classifiers (`Orkeon:Rag:QueryRouting:Classifier`): `heuristic` (default —
deterministic rules, zero LLM call) or `llm` (one constrained lightweight chat
call; when selected without an `IChatClient` the heuristic is the documented
fallback, with a warning).

**Current limitation**: `Iterative` routes fall back to the widest linear preset
(`quality`) until the corrective engine ships — the trace says so explicitly
(`iterative routing falls back to the 'quality' profile until the corrective
engine ships (RAG-06)`). The `corrective` profile lifts this fallback (see
[Corrective RAG](#corrective-rag-crag)).

## Configuration reference (`Orkeon:Rag`)

Bound over the selected preset — every key is an individual override.

| Key | Default | Meaning |
|---|---|---|
| `Orkeon:Rag:Profile` | `fast` | Preset: `fast` / `balanced` / `quality` / `adaptive` (unknown fails loudly) |
| `Orkeon:Rag:Collection` | — | Default collection when the call site names none |
| `Orkeon:Rag:Provider` | ambient | Document-store provider alias (`inmemory`, `redis`, `sqlite`, `chromadb`, `pinecone`, `lancedb`…); unset = ambient `IMemoryProvider` |
| `Orkeon:Rag:ConnectionString` / `ProviderOptions:*` | — | Passed to the memory-provider factory when `Provider` is set |
| `Orkeon:Rag:Retrieval:TopK` | 5 | Chunks kept for context assembly (call-site `RagQuery.TopN` wins) |
| `Orkeon:Rag:Retrieval:CandidateK` | 50 | Wide stage of the cascade (always ≥ final TopN) |
| `Orkeon:Rag:Retrieval:MinScore` | none | Optional raw-score floor (the legacy global 0.7 floor is deliberately gone) |
| `Orkeon:Rag:Retrieval:Hybrid:Enabled` | false (`fast`) | Default hybrid mode; flat shorthand `Retrieval:Hybrid = true` accepted |
| `Orkeon:Rag:Retrieval:Hybrid:RrfK` | 60 | Reciprocal Rank Fusion constant |
| `Orkeon:Rag:Retrieval:Mmr:Enabled` | false | MMR diversification at the fuse stage |
| `Orkeon:Rag:Retrieval:Mmr:Lambda` | 0.7 | Relevance/diversity trade-off in [0, 1] |
| `Orkeon:Rag:QueryTransform:Mode` | `none` | `none` / `multi-query` / `rag-fusion` / `hyde` |
| `Orkeon:Rag:QueryTransform:VariantCount` | 3 | Variants requested from non-`none` transformers |
| `Orkeon:Rag:Rerank:Enabled` | false (`fast`) | Whether the rerank stage runs |
| `Orkeon:Rag:Rerank:Kind` | `none` | `none` / `llm` / `onnx` / any registered name |
| `Orkeon:Rag:Rerank:TopN` | 5 | Chunks kept after reranking |
| `Orkeon:Rag:Context:MaxTokens` | 2000 | Context budget (≈ 4 chars/token; excess chunks dropped, never overrun) |
| `Orkeon:Rag:Context:Ordering` | `edges` | `edges` (anti-Lost-in-the-Middle) or `linear` |
| `Orkeon:Rag:Groundedness:Enabled` | false | Post-generation verification (traced as skipped without a checker) |
| `Orkeon:Rag:Generation:SystemPrompt` | built-in | Grounded system prompt override |
| `Orkeon:Rag:Generation:Temperature` / `MaxOutputTokens` | — | Sampling passed to the chat client |
| `Orkeon:Rag:QueryRouting:Classifier` | `heuristic` | `heuristic` or `llm` (adaptive profile) |
| `Orkeon:Rag:Ingestion:DefaultChunkingStrategy` | `recursive` | Strategy when a request names none |
| `Orkeon:Rag:Ingestion:ManifestDirectory` | `/output/rag/manifests` | VFS directory of the per-collection manifests |

## Ingestion

`DefaultIngestionPipeline` runs: load (`DocumentLoaderFactory` selects by source
kind/extension — text, CSV, HTML, PDF, `WebPageLoader` for URLs) → **security
validation** → chunk (named strategy) → embed → upsert.

- **Validation** (ingestion-path security): `PromptInjectionDocumentValidator`
  and `ContentIntegrityValidator` run on every document; rejected content goes to
  the `IQuarantineStore` and provenance is recorded (`IProvenanceTracker`).
  Details and threat model in [security.md](./security.md).
- **Incremental manifest** (per collection, one JSON file written through the
  VFS at `{ManifestDirectory}/{collection}.json`): each source's content hash and
  the embedding profile (provider, model, dimensions) are recorded. Unchanged
  sources are skipped entirely — `chunksEmbedded = 0`; changed ones are purged
  and re-ingested. Failure posture: a missing/corrupt manifest degrades to full
  ingestion, an unwritable manifest directory degrades to a warning (ingestion
  works, just never incrementally) — never a crash.
- **`Reindex = true`** purges every recorded source and rebuilds the collection.
  It is also the only way to re-ingest after an embedding-profile change:
  a drift between the manifest's recorded profile and the active provider fails
  the run loudly rather than silently mixing incompatible vectors.
- Glob sources (`*`, `**`, `?`) are expanded through the VFS by
  `SourceGlobExpander` at the consuming surfaces (scripting `rag.ingest`,
  `rag_ingest` tool, eval harness corpus).

## Crew YAML integration

Two YAML blocks, consumed at different moments (see `examples/rag/crew-yaml`):

```yaml
rag:                        # crew-level: collections ingested at crew creation
  collections:
    product-kb:
      sources: [/kb/faq.md]
      chunking: { strategy: recursive, max_tokens: 256, overlap: 32 }

agents:
  support:
    role: Support agent
    goal: Answer from the knowledge base
    knowledge: [product-kb] # agent-level: retrieval attachment
```

- The `rag:` block is parsed into `RagCrewConfig`; `CrewFactory` hands it to the
  `IRagCollectionsBootstrapper` at `CreateFromConfigAsync` — kickoff-time
  ingestion through the incremental pipeline (a fresh manifest makes it a
  no-op). A crew declaring `rag:` on a host without `AddOrkeonRag` logs a
  warning instead of failing.
- `knowledge:` attachments travel on the `Agent` aggregate; at task-context
  assembly the execution orchestrator calls `IKnowledgeContextAugmenter`, which
  retrieves from the attached collections (retrieval only — no nested LLM call)
  and injects a bounded, numbered block with `[n]` citations into the agent's
  prompt.

## Scripting and CLI surfaces

- **Scripting DSL** (`.ork.ts`): first-class `rag.ingest({ collection, sources,
  chunkingStrategy?, reindex? })` and `rag.query(question, { collection,
  profile?, topN? })` globals — see `examples/scripting/08-rag.ork.ts`. The
  namespace is always registered; calls fail with an actionable message when the
  host did not wire the subsystem.
- **CLI** (`orkeon`): `orkeon rag ingest|search|eval` over the same pipelines.

## Evaluation (golden datasets, CI gate)

The offline harness (`IRagEvalHarness`, RAG-04/C1) turns retrieval quality into a
published number instead of a claim:

- **Dataset** (`examples/rag/eval/golden.yaml`): a corpus directory plus cases —
  `question`, `relevant` source refs (suffix-matched against ingested ids),
  `expected_substrings`, `reference_answer`, `tags`. The corpus is ingested
  (incrementally) before every run.
- **Metrics**: recall@k and MRR per case and aggregated; generation is judged by
  an LLM judge when available, with a deterministic heuristic fallback — the
  report always labels which judge ran.
- **Profiles compared** in one run: `orkeon rag eval --dataset … --compare
  fast,balanced,quality --offline` (`--offline` swaps generation for a
  deterministic extractive stub — zero network).
- **CI gate** (`.github/workflows/rag-eval.yml`): the comparison table is
  published to the step summary, and an anti-regression gate fails the build
  when the `balanced` profile's aggregate recall@5 or MRR drops below the
  floors. Cases tagged `correctif` are excluded from the gates: they are seeded
  to fail single-shot retrieval on purpose (see below).

## Corrective RAG (CRAG)

> **Livré avec RAG-06 (profil `corrective`) — section complétée à l'intégration.**
>
> Shipping scope (fiche RAG-06): a corrective retrieval graph built on the
> in-house `Graph` orchestration (`StateGraph<RagGraphState>`) — nodes
> `retrieve` → `evaluate` (`IRetrievalEvaluator`, verdict
> `Correct | Incorrect | Ambiguous`) → `refine` / `rewrite_query` (bounded
> loop) / `web_fallback` (opt-in, injection-validated) → `generate` →
> `check_groundedness` (`IGroundednessChecker`); `Corrective.MaxIterations`
> bounded by the Graph circuit breaker; same `IRagPipeline` façade (the
> profile selects the executor); the `adaptive` profile's `Iterative` route
> then targets `corrective` instead of the current `quality` fallback; the
> seeded `correctif` golden cases become the acceptance proof
> (`--compare fast,balanced,quality,corrective,adaptive`).
>
> Until that lot lands, the hooks already exist and are traced: the
> `groundedness` stage runs when a checker is registered, `RagTrace` carries
> `Verdicts` and `Iterations`, and `Iterative` routing falls back to
> `quality` with an explicit trace detail.

## V1 limitations

- The in-process BM25 index is per-process and unpersisted; native hybrid
  (LanceDB) is preferred at scale — automatic when the provider supports it.
- MMR uses lexical similarity between candidates (embeddings are not
  re-computed at the fuse stage).
- `balanced` / `quality` require the opt-in ONNX packages; without them use
  `Rerank:Kind = llm` or stay on `fast`.
- The corrective engine is not shipped yet (see the CRAG section above);
  `Iterative` routes degrade to `quality`, explicitly traced.
