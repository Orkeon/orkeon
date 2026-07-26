# RAG golden dataset (`golden.yaml`)

Versioned evaluation dataset of the RAG subsystem (RAG-04/C1, plan §9): 7
questions over a small fictional corpus (`corpus/`, 10 markdown documents —
product FAQ + tech notes for the imaginary "Nimbus Hub / Nimbus Sense" devices).
The harness is **100 % CI-runnable offline**: local BGE embeddings, deterministic
extractive generation (`--offline`), deterministic heuristic judge.

## Running it

```bash
# From the repository root — ingests corpus/ (incremental), evaluates, writes
# reports to ./.orkeon/rag/eval/ (markdown + JSON):
orkeon rag eval --dataset examples/rag/eval/golden.yaml --offline

# Multi-profile comparison table (one row per profile). Until the RAG-04/C4
# profile presets land, every profile name resolves to the same pipeline
# (documented default of IRagProfileResolver), so the rows are identical:
orkeon rag eval --dataset examples/rag/eval/golden.yaml --offline --compare fast,balanced,quality

# CI anti-regression gate (exit 1 below the floors; `correctif` cases excluded):
orkeon rag eval --dataset examples/rag/eval/golden.yaml --offline --min-recall 0.80 --min-mrr 0.70
```

The dedicated CI workflow is `.github/workflows/rag-eval.yml` (floors in its
`env:` block — keep them in sync with `RagEvalGoldenDatasetSlowTests`).

The agent-facing surface is the `rag_eval` tool (`Orkeon.Tools.Rag`), and the
programmatic one is `IRagEvaluator` / `IRagEvalHarness` (`Orkeon.Rag.Evaluation`).

> Note (default in-memory store): the ingestion manifest under `./.orkeon`
> persists across CLI runs while the in-memory document store does not. If a
> second run reports `0 added, N unchanged` and zero recall, re-run with
> `--reindex` (or delete `./.orkeon/rag/manifests`).

## Schema

Mapping form (used here) — snake_case keys:

```yaml
name: golden              # dataset name (reports, file names)
corpus: ./corpus          # dir ingested before evaluation (relative to this file, or /virtual/path)
collection: rag-eval-golden  # evaluated collection (default: rag-eval-{name})
cases:
  - id: q-001                       # unique, required
    question: "…"                   # required
    relevant: ["corpus/faq.md"]     # refs of the sources/chunks a correct retrieval must surface:
                                    # chunk id, document id, source id, or source-path suffix;
                                    # an optional #fragment is ignored by suffix matching
    expected_substrings: ["30 days"] # deterministic answer-relevance (heuristic judge)
    reference_answer: "…"           # optional, handed to the LLM judge
    tags: [facile, factuel]         # free-form; `correctif` has gate semantics (below)
```

A bare YAML list of cases (no `name:`/`corpus:` wrapper) is also accepted; the
dataset name then falls back to the file name and no corpus is ingested.

## Metrics and judge labelling

- Retrieval (deterministic): recall@k, precision@k (denominator k), MRR —
  computed from `relevant` vs the ranked citations.
- Generation: groundedness + answer-relevance. LLM-judge (`--llm-judge`, via the
  configured `IChatClient`) **or** deterministic heuristic fallback
  (`expected_substrings` presence + citation coverage of `relevant`). The mode
  actually used is **always labelled** in the report (`judge: llm` /
  `judge: heuristic`, per case and per run — LLM parse failures fall back per
  case and are counted).

## The seeded `correctif` case (q-007)

`q-007` ("How can I make the battery of my Nimbus Sense last longer?") is a
**deliberately hard retrieval case**, built to defeat plain vector similarity:

- the true answer lives in `corpus/notes-power.md`, which *avoids the
  question's vocabulary entirely* — it says "S-series node", "energy",
  "cell runtime", "eco mode", never "battery", "last longer" or "Nimbus Sense";
- five decoy documents (`faq-battery.md`, `faq-warranty.md`, `faq-returns.md`,
  `faq-shipping.md`, `notes-telemetry.md`) repeat that vocabulary heavily in
  unrelated contexts — `faq-battery.md` even contains the literal phrase
  "make the battery last longer".

With local BGE embeddings the top-5 is monopolized by the decoys and
`q-007` scores recall@5 = 0. **This failure is intentional** (verified by an
inverted assertion in `RagEvalGoldenDatasetSlowTests`): it is the benchmark the
corrective retrieval engine (RAG-06) must flip. That is why every gate
(`--min-recall`, `--min-mrr`, CI floors) excludes cases tagged `correctif`.

If a future embedding/hybrid stage starts recovering it, do not delete the
assertion — strengthen the decoys or seed a harder case, so RAG-06 keeps a
measurable target.
