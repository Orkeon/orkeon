# RAG — Crew from YAML (`rag:` block + `knowledge:` attachment)

> A crew declared entirely in YAML: the crew-level `rag:` block gets its
> collections ingested when the crew is created (kickoff), the agent-level
> `knowledge:` attachment binds the agent to a collection, and the
> knowledge-context augmenter — the exact component the execution path uses —
> produces the cited `[1]` block for a customer question. Fully offline, no
> API key.

## The YAML (see `crew.yaml`)

```yaml
rag:
  collections:
    product-kb:
      sources: [/kb/faq.md, /kb/battery-care.md, /kb/service-intervals.md]
      chunking: { strategy: recursive, max_tokens: 256, overlap: 32 }

agents:
  support:
    role: Support agent
    goal: Answer customer questions from the knowledge base, always citing sources
    knowledge: [product-kb]
```

## What it does

- Loads `crew.yaml` through `ICrewDefinitionLoader` and creates the crew with
  `ICrewFactory.CreateFromConfigAsync`. Crew creation runs the
  `RagCollectionsBootstrapper`: every collection declared by the `rag:` block
  is ingested through the **incremental** ingestion pipeline (per-collection
  manifest — an unchanged corpus re-ingests nothing on subsequent kickoffs).
- Proves the collection is searchable offline (a direct
  `IDocumentStore.SearchAsync` probe).
- Reads the agent back from the repository and shows its
  `KnowledgeAttachments` — the YAML `knowledge: [product-kb]` attachment.
- Calls `IKnowledgeContextAugmenter.BuildContextAsync` with the attachments
  and the question — the same call the execution orchestrator makes when it
  assembles a task context — and prints the resulting block: a grounding
  instruction followed by numbered `[n]` excerpts, plus the citations
  resolving each marker (source + score). The refund question lands on
  `faq.md` as `[1]`.

Retrieval-only augmentation: no LLM is involved in building the block. With
an LLM configured, running the crew grounds the agent's answer on this block
and keeps the `[n]` markers as citations (see
`tests/e2e/Orkeon.E2E.Tests/YamlKnowledgeCrewOfflineTests.cs` for the
end-to-end assertion of this exact YAML motif).

## Prerequisites

- .NET SDK ≥ 10.0.300

No API key or `appsettings` needed — embeddings are computed on-device
(BGE-micro-v2), and the demo registers a deterministic offline `IChatClient`
stub before the infrastructure defaults so no real LLM is ever wired.

**First build only**: the `SmartComponents.LocalEmbeddings` package fetches the
BGE-micro-v2 ONNX file once (~17 MB) during `dotnet build`.

## Run it

```bash
# From the repository root
bash examples/run-example.sh rag/crew-yaml

# Or directly
dotnet run --project examples/rag/crew-yaml
```

The same `crew.yaml` also runs as-is in the stock CLI (this host exists only to
prove the YAML `rag:`/`knowledge:` path offline, without an LLM):

```bash
orkeon run examples/rag/crew-yaml/crew.yaml --mount examples/rag/crew-yaml/data:/kb:ro --validate
# Drop --validate to run the crew for real (needs a configured LLM)
```

## Expected output (truncated)

```
[1/3] Crew 'support-crew' created — rag: collections ingested at kickoff.
[2/3] Collection 'product-kb' is searchable: 3 chunks retrieved.
[3/3] Agent 'Support agent' knowledge attachments: product-kb

Q: "How many days do customers have to request a refund?"

Knowledge context block injected into the agent prompt:
--------------------------------------------------------
Answer from the excerpts below and cite the passages you use with their [n] markers. ...

[1] (collection: product-kb, source: /kb/faq.md, score: 0.71)
# Volta One — Frequently asked questions
Refunds: customers may return the bike and request a full refund within 30
days of delivery, ...
--------------------------------------------------------
Citations:
  [1] 0.71xx  /kb/faq.md
  [2] 0.51xx  /kb/battery-care.md
  [3] 0.49xx  /kb/service-intervals.md
```

Scores may shift slightly across CPU vendors; `faq.md` at `[1]` is the signal.

## Approx. duration & cost

- **Duration**: a few seconds (ONNX session boot dominates).
- **Cost**: none — no LLM calls, embeddings on-device.
