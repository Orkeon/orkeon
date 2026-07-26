# RAG — Custom reranker (host-provided `IReranker` via DI)

> Implements a deliberately trivial `IReranker` — a lexical bonus for exact
> identifiers (`R-102`) — and plugs it into the RAG pipeline through DI, the
> same extension point the opt-in ONNX cross-encoder package uses. Shows the
> CandidateK → TopN cascade and the `rerank` trace step. Fully offline, no
> API key.

## What it does

- Ingests four Markdown pages about a fictional solar inverter ("SunCore")
  into the `suncore-docs` collection (local BGE embeddings).
- Contributes `LexicalBonusReranker` to the reranker factory through
  **`IRerankerRegistrar`** — one `services.AddSingleton<IRerankerRegistrar,
  LexicalBonusRegistrar>()` line, applied when the singleton
  `RerankerFactory` is built, registration order relative to `AddOrkeonRag`
  irrelevant. This is exactly how `AddOrkeonOnnxReranker()` contributes the
  ONNX cross-encoder. (The factory itself is `TryAdd`-registered, so a host
  that registers its **own** `RerankerFactory` first wins outright.)
- Selects the reranker **by configuration**, not by code:

  ```
  Orkeon:Rag:Retrieval:CandidateK = 4      (wide stage of the cascade)
  Orkeon:Rag:Rerank:Enabled      = true
  Orkeon:Rag:Rerank:Kind         = lexical-bonus
  ```

  The `IRagPipeline` resolved from DI is composed from the `fast` preset plus
  these overrides — profile = preset, configuration = override.
- Asks one question with `TopN = 2` and prints the citations plus the full
  stage trace: the `rerank` step shows `reranker=lexical-bonus,
  candidates=4, kept=2` — the CandidateK (4) → TopN (2) cascade.
- Runs a control pipeline with reranking disabled to make the rank flip
  visible.

## The reranker

`LexicalBonusReranker` scores each candidate as `retrieval score + 1.0 per
identifier-like query token found verbatim in the chunk` (an identifier =
a token carrying a digit: a log code like `R-102`, a part number, an RFC
number). Chunks containing the exact identifier always outrank chunks that
merely *sound* related. Swap the body for a cross-encoder call or a domain
policy (prefer curated FAQ pages, penalise stale documents…) — the wiring
stays identical.

## What you should see

The question *"The inverter maintenance log shows code R-102, what does it
mean?"* **sounds** like the generic fault guide, and cosine similarity ranks
that page first. Only the relay self-test reference actually explains
`R-102` — the lexical bonus pins it at rank `[1]`:

```
Reranked result (lexical-bonus):
  [1] 1.68xx  /data/relay-selftest.md      <- +1.0 identifier bonus
  [2] 0.75xx  /data/fault-guide.md

Trace:
  - transform (none — passthrough) mode=none
  - retrieve  candidates=4, hybrid=false, mode=vector, top_k=4, variants=0
  - fuse      in=4, method=dedup, out=4
  - rerank    candidates=4, kept=2, reranker=lexical-bonus
  - assemble  chunks=2, dropped_by_budget=0, ordering=edges
  - generate

Control (no reranker — cosine order):
  [1] 0.75xx  /data/fault-guide.md         <- plausible but useless
  [2] 0.68xx  /data/relay-selftest.md
```

Cosine scores may shift slightly across CPU vendors; the rank flip is the
signal.

## Prerequisites

- .NET SDK ≥ 10.0.300

No API key or `appsettings` needed — embeddings are computed on-device,
generation is a deterministic offline stub (this demo is about the rerank
cascade).

**First build only**: the `SmartComponents.LocalEmbeddings` package fetches the
BGE-micro-v2 ONNX file once (~17 MB) during `dotnet build`.

## Run it

```bash
# From the repository root
bash examples/run-example.sh rag/custom-reranker

# Or directly
dotnet run --project examples/rag/custom-reranker
```

## Approx. duration & cost

- **Duration**: a few seconds (ONNX session boot dominates).
- **Cost**: none — no LLM calls, embeddings on-device.
