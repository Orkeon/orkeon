# RAG — Hybrid retrieval (BM25 + RRF vs vector only)

> Same corpus, same questions, two pipelines: the `fast` preset (vector only)
> against the same preset with hybrid retrieval enabled
> (`Orkeon:Rag:Retrieval:Hybrid`). Shows the query an embedding model gets
> wrong — an exact error code — being rescued by the lexical BM25 side, and
> the stage traces (including `fuse`: method, in/out). Fully offline, no API
> key.

## What it does

- Ingests four Markdown pages about a fictional security camera ("AtlasCam")
  into the `atlascam-docs` collection. Ingestion feeds **both** retrieval
  sides at once: the inner store gets the embedded chunks, and the
  `HybridSearchDocumentStore` decorator (always wrapping the store, see
  `AddOrkeonHybridRetrieval`) indexes the same chunks into its in-process
  BM25 index.
- Builds two `StagedRagPipeline` instances over the **same** store and
  embeddings; only the options differ:
  - vector only — `RagProfilePresets.Create(RagProfile.Fast)`;
  - hybrid — same preset with `Retrieval.Hybrid.Enabled = true` (the
    configuration equivalent is `Orkeon:Rag:Retrieval:Hybrid:Enabled`),
    i.e. BM25 + Reciprocal Rank Fusion with the standard `RrfK = 60`.
- Runs two questions through both pipelines and prints the rankings plus the
  hybrid run's full stage trace (`transform → retrieve → fuse → rerank →
  assemble → generate`), with each stage's data points (`retrieve:
  hybrid=true`, `fuse: method/in/out`, …).
- Ends with a store-level view where the score provenance is visible:
  `ScoreOrigin = vector` (cosine similarities) vs `ScoreOrigin = rrf` (fused
  rank aggregates — RRF scores are **not** similarities, which is why the
  hybrid numbers look small).

## What you should see

1. **Exact error code** (*"The camera screen shows E-417 after a reboot, how
   do I fix it?"*): the embedding model ranks the generic troubleshooting
   page first — the question *sounds* like troubleshooting. The BM25 side
   pins the rare token `E-417` in the firmware release notes (the only page
   that actually explains it), and RRF pushes that page to rank `[1]`.
2. **Pure paraphrase** (*"How can I make the picture look sharper?"* — no
   keyword overlap with the corpus): the vector side carries the query, and
   hybrid fusion keeps the semantic winner on top. Hybrid rescues lexical
   queries without degrading semantic ones — that is the point.

## Prerequisites

- .NET SDK ≥ 10.0.300

No API key or `appsettings` needed — embeddings are computed on-device
(BGE-micro-v2 via `AddOrkeonLocalEmbeddings()`), generation is a deterministic
offline stub (this demo is about retrieval rankings and traces).

**First build only**: the `SmartComponents.LocalEmbeddings` package fetches the
BGE-micro-v2 ONNX file once (~17 MB) during `dotnet build`; subsequent builds
and every run are network-free.

## Run it

```bash
# From the repository root
bash examples/run-example.sh rag/hybrid-retrieval

# Or directly
dotnet run --project examples/rag/hybrid-retrieval
```

## Expected output (truncated)

```
=== Query 1 — exact error code (lexical evidence) ===
Q: "The camera screen shows E-417 after a reboot, how do I fix it?"
  vector only:
    [1] 0.72xx  /data/troubleshooting.md      <- plausible but useless
    [2] 0.68xx  /data/firmware-notes.md
    ...
  hybrid (BM25 + RRF):
    [1] 0.032x  /data/firmware-notes.md       <- the page that explains E-417
    [2] 0.032x  /data/troubleshooting.md
    ...
  hybrid trace:
    - transform (none — passthrough) mode=none
    - retrieve  candidates=4, hybrid=true, mode=vector, top_k=5, variants=0
    - fuse      in=4, method=dedup, out=4
    - rerank    (disabled — truncation to TopN) candidates=4, kept=3, reranker=none
    - assemble  chunks=3, dropped_by_budget=0, ordering=edges
    - generate
```

Cosine scores may shift slightly across CPU vendors; the **rank-1 flip** on
query 1 (troubleshooting → firmware-notes) is the signal.

Note on the trace: the pipeline's `fuse` step combines rankings across query
*variants* (method `rrf` when a `rag-fusion` transformer produced variants,
`union`/`dedup` otherwise — here `dedup`, single query). The BM25 + vector
fusion itself happens inside the hybrid store during `retrieve`
(`hybrid=true`), which is why the store-level view shows `origin=rrf`.

## Approx. duration & cost

- **Duration**: a few seconds (ONNX session boot dominates).
- **Cost**: none — no LLM calls, embeddings on-device.
