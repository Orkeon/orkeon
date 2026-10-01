> 🇫🇷 [Version française](../fr/reference/examples-catalog.md)

> **See also**: [Back to index](../INDEX.md)

# Examples catalog

Everything under `examples/` runs on the same engine; this page is the editorial map.
The **authoritative inventory** — every numbered example with its process type,
agent/task counts and referenced tools — is the generated
[`examples/INDEX.md`](https://github.com/Orkeon/orkeon/blob/main/examples/INDEX.md): it is produced by
`scripts/generate_examples_index.py` and CI fails when it drifts from the folders on
disk, so no count is maintained by hand here.

The same generator writes its machine-readable twin,
[`examples/usecases.json`](https://github.com/Orkeon/orkeon/blob/main/examples/usecases.json):
one entry per numbered example, joining what its crew declares (process, tools, whether
they reach the internet or need a third-party key) to its hand-written `usecase.yaml`
sheet (title and problem statement in five languages, tags, mounts, whether it can be
imported as a team). CI fails when it drifts too; the sheet format is in
[`examples/README.md`](https://github.com/Orkeon/orkeon/blob/main/examples/README.md#use-case-sheet-usecaseyaml).

## The nine business categories

The numbered crews live in nine thematic folders. Most are YAML crews (`config.yaml`);
the fifteen of `03-finance-trading/` are TypeScript crews (`main.ork.ts`, run by the same
`orkeon run`) sharing that category's `_tools/` module. The numbering is historical and
not contiguous.

| Category | Folder | Focus |
|----------|--------|-------|
| **01 — Enterprise** | `01-enterprise/` | Research, code review, e-mail, reports, support, due diligence, onboarding, interactive Q&A |
| **02 — Science & Research** | `02-science-research/` | Meta-analysis, scientific debate, genomics, grant writing, knowledge graphs |
| **03 — Finance & Trading** | `03-finance-trading/` | Algorithmic trading, fraud detection, KYC/AML, portfolio consensus, ESG, stress testing |
| **04 — Health & Wellness** | `04-health-wellness/` | Diagnostic support, nutrition, clinical trials, pharmacovigilance, telemedicine |
| **05 — Education** | `05-education/` | Adaptive tutoring, exam generation, grading, gamification, mentoring |
| **06 — Engineering & DevOps** | `06-engineering-devops/` | CI/CD, incident response, database migration, chaos engineering, TypeScript codebase crews |
| **07 — Creative & Media** | `07-creative-media/` | Narrative, podcast, music, art direction, worldbuilding, newsletters |
| **08 — IoT & Smart Systems** | `08-iot-smart-systems/` | Smart home, fleet, precision agriculture, energy, predictive maintenance |
| **09 — Experimental** | `09-experimental/` | Self-adaptive crews, negotiation, ethics jury, crew of crews, graph orchestration |

## Beyond the numbered crews

| Folder | What it shows |
|--------|---------------|
| `rag/` | The RAG subsystem: `basic-ingestion/`, `hybrid-retrieval/`, `custom-reranker/`, `crew-yaml/`, plus the offline evaluation dataset under `eval/` |
| `raggable-tree/` | Semantic codebase analysis: `basic-indexing/`, `crew-yaml/`, `custom-adapter/` |
| `quickstart/` | The README's two-minute crew: one agent, one `file_write`, a local Ollama model, one writable mount |
| `scripting/` | The TypeScript DSL (`.ork.ts`), `01-hello-world` → `13-email-triage`: dynamic spawn, FSM/graph literals, custom tools and hooks, RAG (`08-rag.ork.ts`), inputs and memory, events; `crew-review-desk/` is a whole crew written in TypeScript with its own tools module |
| `cli-ts-commands/` | Interactive REPL commands in `*.cmd.ts`, loaded without .NET recompilation |
| `local-embeddings/` | On-device BGE-micro-v2 embeddings (no network, no API key): three snippets ranked against a query by cosine similarity |
| `crew-multifile/` | A crew described as a directory (`orkeon run <dir>`) |
| `forge/promote-demo/` | The Atelier's offline half: a ready `orkeon forge` session bundled as a workspace — `forge list`, then `forge promote` into an ordinary folder (no LLM needed) |
| `service-host/` | The `orkeon-host` daemon's example material: a README and `appsettings.host.json` |
| `run-events/` | The `orkeon run --events jsonl` protocol: README, `sample-stream.jsonl`, `watch-run.py` |
| `aspire/AppHost/` | A .NET Aspire AppHost (`Orkeon.Hosting.Aspire`) running the quickstart crew as a resource, its spans, metrics and logs in the dashboard |
| `interop/agent-framework/` | `Orkeon.Interop.AgentFramework` both ways: a crew wrapped as a Microsoft Agent Framework `AIAgent`, and a MAF agent handed to an Orkeon agent as a tool |
| `09-experimental/llm-response-format/`, `09-experimental/streaming-demo/` | Two unnumbered demos inside the experimental category: structured output (`response_format`) and real-time streaming of an agent's execution |
| `appsettings/` | The shared settings profile matrix: one `appsettings.json` plus a `*.local.json.example` per provider |
| `others/` | The README of a benchmark corpus of twenty TypeScript codebases for the `102` codebase crew (the codebases themselves are not committed) |

**Looking for e-mail?** Two examples, of two different kinds:

- [`scripting/13-email-triage.ork.ts`](https://github.com/orkeon/orkeon/blob/main/examples/scripting/13-email-triage.ork.ts) works on a **real mailbox**: a Gmail account reached with an app password (declared in `13-email-triage.appsettings.json`), it files the unread mail by kind and leaves each reply as a draft — nothing is sent. Walk-through: [Give your agents a mailbox](../getting-started/give-your-agents-a-mailbox.md).
- [`01-enterprise/03-email-pipeline`](https://github.com/orkeon/orkeon/blob/main/examples/01-enterprise/03-email-pipeline) connects to **no mailbox**: its agents read e-mail files with `email_parser` and write their triage and draft replies to files.

## Notable examples

- **Due diligence with NIST audit** — `01-enterprise/07-due-diligence-nist/`:
  hierarchical process — a chief analyst coordinates four specialists (legal,
  financial, reputation, compliance) — crew memory on, severity-classified findings.
- **Algorithmic trading, multi-strategy** — `03-finance-trading/31-algo-trading/`:
  a TypeScript crew, hierarchical: the CIO coordinates eight agents across analysis,
  risk, execution and compliance, with the category's shared `_tools/` module.
- **RAG ingestion to cited answers** — `rag/basic-ingestion/`: ingest, retrieve,
  generate with citations — then graduate to `hybrid-retrieval/` and the
  `corrective` profile.
- **Index a codebase, then ask it questions** — `raggable-tree/basic-indexing/`:
  Tree-sitter indexing from a plain console program (no LLM) — then
  `raggable-tree/crew-yaml/` gives a crew the analysis tools (`index_codebase`,
  `codebase_map`, `symbol_detail`, …).

## Running an example

```bash
orkeon run examples/crew-multifile/          # a crew directory
orkeon run examples/scripting/01-hello-world.ork.ts
./examples/run-example.sh 01-enterprise/01-research-assistant   # from source (config.yaml, main.ork.ts or a .csproj)
docker run -it --rm -e ORKEON_RUNNER=shell ghcr.io/orkeon/orkeon-runners
# then inside the container: orkeon-example list && orkeon-example run 7
```

From C# (via DI):

```csharp
// crewFactory : ICrewFactory, orchestrator : ICrewOrchestrationService.
// The factory reads through the VFS: the path is virtual, under a declared mount
// (Orkeon:FileSystem:Mounts, e.g. "examples/01-enterprise/07-due-diligence-nist:/crews:ro").
var crew = await crewFactory.CreateFromFileAsync("/crews/config.yaml", ct);
var result = await orchestrator.KickoffAsync(crew.Id, CrewInput.Empty(), ct);
```

`CreateFromDirectoryAsync` is for a crew split into files — `config.yaml` with `agents/`
and `tasks/` folders, like `crew-multifile/`, or the flat `crew.yaml` + `agents.yaml` +
`tasks.yaml` triplet — not for a folder holding a single `config.yaml`.

Examples ship configuration, not datasets — see the
[example data policy](./example-data-policy.md) for how to mount your own input.
