# RAG — Basic ingestion

> The smallest end-to-end demo of the RAG subsystem (`src/rag/`): ingest a tiny
> FAQ corpus with the on-device BGE embeddings, prove the second ingestion is
> incremental (zero re-embedding), then ask two questions and get scored,
> cited passages back — fully offline, no API key.

## What it does

- Mounts `data/` read-only at `/data` and a per-run temp directory read-write
  at `/output` (the ingestion manifests land in `/output/rag/manifests`).
- Wires the whole opt-in chain: `AddOrkeonLocalEmbeddings()` (BGE-micro-v2,
  384 dims, CPU) → `AddOrkeonInfrastructure()` → `AddOrkeonRag(configuration)`
  → `AddOrkeonRagTools()` (`rag_search` becomes agent-discoverable).
- Ingests four Markdown FAQ pages about a fictional smart herb garden
  ("VerdaPod") into the `demo-faq` collection via `IIngestionPipeline` and
  prints the report (documents, chunks, embedded/skipped, added/unchanged).
- Runs the **same ingestion a second time**: the per-collection manifest
  (RAG-03/C1) short-circuits every unchanged source — `chunks embedded: 0`.
- Asks two questions through `IRagPipeline.QueryAsync`:
  1. a lexical one (*"How long does the battery last on a full charge?"*), and
  2. a **pure paraphrase** (*"How long do buyers have to get their money
     back?"* — "buyers" and "money back" never appear in the corpus) that only
     a semantic model can route to the warranty-and-returns page.

## Offline generation

`StagedRagPipeline` requires an `IChatClient` for the generation step, and the
default of `AddOrkeonInfrastructure()` wires a real LLM provider (API key
required at call time). This example registers a deterministic offline
`IChatClient` stub **before** the infrastructure defaults, so the answer text
is a fixed notice and the retrieved passages (with scores and sources) are
printed instead — retrieval, citations, and the trace are fully real. To get
generated grounded answers, replace that registration with a real chat client
or configure an LLM provider.

## Prerequisites

- .NET SDK ≥ 10.0.300

No API key or `appsettings` needed — embeddings are computed on-device.

**First build only**: the `SmartComponents.LocalEmbeddings` package fetches the
BGE-micro-v2 ONNX file once (~17 MB) during `dotnet build`; subsequent builds
and every run are network-free.

## Run it

```bash
# From the repository root
bash examples/run-example.sh rag/basic-ingestion

# Or directly
dotnet run --project examples/rag/basic-ingestion

# Or against your own corpus directory (*.md files)
dotnet run --project examples/rag/basic-ingestion -- /path/to/your/docs
```

## Expected output (truncated)

```
RAG basic-ingestion — offline demo (local BGE embeddings, zero API key)

[1/3] Initial ingestion into collection 'demo-faq'...
  documents loaded  : 4
  chunks created    : 4
  chunks embedded   : 4
  sources           : 4 added, 0 unchanged, 0 re-ingested

[2/3] Second ingestion of the unchanged corpus (incremental)...
  chunks embedded   : 0
  sources           : 0 added, 4 unchanged, 0 re-ingested
  => unchanged corpus: 0 embeddings computed on the second run.

[3/3] Questions (semantic retrieval + citations)...

Q: "How long does the battery last on a full charge?"
A: (offline mode — no LLM configured; showing the retrieved passages instead of a generated answer)
   [1] 0.71xx  /data/power-and-battery.md  "# VerdaPod — Power and battery ..."
   ...

Q: "How long do buyers have to get their money back?"
A: (offline mode — no LLM configured; showing the retrieved passages instead of a generated answer)
   [1] 0.58xx  /data/warranty-and-returns.md  "# VerdaPod — Warranty and returns ..."
   ...
```

Scores depend on the model output and may shift slightly across CPU vendors;
the ranking (battery page first for Q1, warranty page first for Q2) is the
signal.

## Approx. duration & cost

- **Duration**: a few seconds (ONNX session boot dominates).
- **Cost**: none — no LLM calls, embeddings on-device.
