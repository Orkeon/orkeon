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

# Multi-profile comparison table (one row per profile preset — see "Profiles"):
orkeon rag eval --dataset examples/rag/eval/golden.yaml --offline --compare fast,balanced,quality,corrective,adaptive

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

## Profiles (RAG-04/C4, `adaptive` since RAG-05, `corrective` since RAG-06)

`--profile` / `--compare` resolve the preset pipelines (`RagProfilePresets`):

| Profile | Retrieve | Rerank | Groundedness |
|---|---|---|---|
| `fast` | vector only, TopN direct | none | no |
| `balanced` | hybrid BM25 + RRF, CandidateK 50 | ONNX cross-encoder 50 → 5 | no |
| `quality` | hybrid BM25 + RRF, CandidateK 100 | ONNX cross-encoder 100 → 5 | enabled (checker shipped with RAG-06; offline, the LLM checker degrades to the safe `grounded` verdict) |
| `adaptive` | classifier-routed (RAG-05): `NoRetrieval` → direct LLM answer, `SingleShot` → `balanced`, `Iterative` → `corrective` (since RAG-06) | per delegate | per delegate |
| `corrective` | CRAG graph (RAG-06): hybrid BM25 + RRF feeding retrieve → evaluate → rewrite/refine loops, no linear rerank stage | none (the graph corrects by looping, not reranking) | native `check_groundedness` node (whenever a checker is registered) |

Measured table (2026-07-26, offline: local BGE embeddings, embedded
ms-marco-MiniLM-L-6-v2 int8 cross-encoder, extractive generation, heuristic
judge — published exactly as produced by
`--compare fast,balanced,quality,corrective,adaptive --reindex`):

| profile | recall@5 | MRR | groundedness | answer-relevance | judge | ms/case |
|---|---|---|---|---|---|---|
| fast | 0.89 | 0.89 | 0.89 | 0.89 | heuristic | 3 |
| balanced | 0.89 | 0.89 | 0.89 | 0.89 | heuristic | 131 |
| quality | 0.89 | 0.89 | 0.89 | 0.89 | heuristic | 95 |
| corrective | 0.78 | 0.64 | 0.78 | 0.78 | heuristic | 6 |
| adaptive | 0.89 | 0.89 | 0.89 | 0.89 | heuristic | 115 |

**Honest reading of the `corrective` row — offline, it is WORSE than `quality`,
and here is exactly why.** `--offline` replaces the LLM with the deterministic
extractive stub, and the corrective graph is the one profile whose inner nodes
*need* a real LLM: the retrieval grader's tolerant parser ends up fishing grade
words (`no`, `correct`, …) out of the stub's echoed passages — pseudo-random
verdicts — and every triggered `rewrite_query` degenerates to the stub's fixed
prefix line, i.e. ONE meaningless probe shared by all cases. Per-case fallout
(see `golden-corrective.md`/`.json`): five of the nine cases ended on that
degenerate probe and its single top-5 set; it happens to contain
`notes-power.md` (q-007 "recovered": recall 1.00, RR 0.50), `notes-firmware.md`
(q-004) and `notes-error-codes.md` (q-008), but not `notes-api-limits.md` nor
`faq-subscription.md` — so q-002 and q-006, saturated at 1.00 by every other
profile, drop to 0.00. The q-007 "flip" in this offline table is therefore an
**artifact, not a demonstration**: nothing here bridged the vocabulary gap.
The causal, end-to-end demonstration of the corrective mechanism (grader
verdict `Incorrect` → LLM rewrite "extend the runtime of an S-series node" →
re-retrieve → `notes-power.md` cited where `quality` misses it, same store and
embeddings) is `tests/e2e/Orkeon.E2E.Tests/CorrectiveRagMechanismSlowTests.cs`,
which scripts the LLM for exactly the two roles the CI environment cannot
provide — and is labelled as such. The CI gate stays on `balanced` and is
unaffected (`correctif` cases excluded; gated aggregates 1.00/1.00).

`adaptive` equals `balanced` on THIS dataset **by construction**: the heuristic
classifier routes all 9 golden questions to `SingleShot` → `balanced` (each has
exactly one interrogative word, a single `?`, and fewer than 25 words — no
`NoRetrieval` or `Iterative` trigger fires; since RAG-06 an `Iterative` route
would delegate to `corrective` instead of `quality`). The ms/case deltas among
the ONNX-reranking rows are warm-session artifacts of the run order, not
quality gains. What offline mode can and cannot measure here: `--offline` swaps
in a deterministic extractive chat client, so the LLM-backed query transformers
(`multi-query` / `rag-fusion` / `hyde`), the `llm` classifier and the corrective
graph's LLM nodes have no real LLM to call — the measured path is
`QueryTransform.Mode=none` + heuristic routing (+ the corrective degradation
described above). The transformer and MMR levers are wired and unit-tested;
their quality delta gets measured the day a real `IChatClient` drives the run
(or the corpus grows past vector-only saturation).

Honest reading — the fast/balanced/quality rows are identical on THIS dataset, by construction:

- the six regular cases are already saturated by plain vector retrieval
  (recall@5 = RR = 1.00 each, even for `fast`) — a 12-document corpus leaves the
  hybrid and rerank stages no headroom to show a gain;
- the only unsaturated case is the seeded `correctif` one (q-007, below), and it
  defeats the cross-encoder too: the measured cross-encoder score of the truly
  relevant `notes-power.md` is **0.0000** (dead last) while the decoy
  `faq-battery.md` — which literally contains "make the battery last longer" —
  scores **0.9997**. A lexically aligned reformulation ("extend the runtime of an
  S-series node") scores `notes-power.md` at 0.9995, proving the reranker works
  and the gap is pure vocabulary bridging: exactly the query-transform (RAG-05)
  and corrective (RAG-06) levers, as plan §9.1 predicted (`correctif` = "doit
  échouer en Quality, réussir en Corrective").

The 0.89 aggregate = 8/9 (q-007 at 0 by design; q-008/q-009 are exact-identifier lookups — see below). The gated aggregates
(`correctif` excluded) are 1.00 / 1.00 for the four linear/routing profiles;
offline `corrective` sits at 0.75 / 0.66 gated because of the q-002/q-006
degradation explained above — it is not the gated profile, and no threshold was
touched. The harness and CI publication are in place precisely so that spread
gets **measured, not proclaimed**.

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

**Measured RAG-06 outcome (2026-07-26)**: the flip is demonstrated end-to-end
by `CorrectiveRagMechanismSlowTests` (real BGE + real hybrid store + real ONNX
baseline; the LLM is scripted for the grader and the rewrite because CI has
none — measured citations flip from five decoys under `quality` to
`notes-power.md` first under the corrective graph). The offline eval table's
`corrective` row does show q-007 at recall 1.00, but through a degenerate probe
artifact, not through vocabulary bridging — see the honest reading above; the
inverted `RagEvalGoldenDatasetSlowTests` assertion (default profile) still
holds and stays.

If a future embedding/hybrid stage starts recovering it, do not delete the
assertion — strengthen the decoys or seed a harder case, so RAG-06 keeps a
measurable target.

### `q-008` / `q-009` — exact-identifier lookups (tag `lexical`)

Added at RAG-04 integration to give hybrid BM25+RRF its structural terrain:
terse reference tables (`notes-error-codes.md`, `notes-spare-parts.md`) with
almost no prose, queried by exact tokens (`E-7734`, `BRK-115`) wrapped in
decoy-flavoured phrasing. **Measured outcome (2026-07-26)**: at this corpus
scale (12 documents, top-5) plain vector retrieval with local BGE also ranks
the tables first — subword tokenization makes rare identifiers strong vector
signals in a small corpus — so `fast` and `balanced` tie at 0.89. The cases
stay: they are legitimate coverage, they gate in CI, and they become the
hybrid discriminator the day the corpus grows past what top-5 vector recall
can saturate.
