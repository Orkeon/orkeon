> 🇫🇷 [Version française](../fr/reference/examples-catalog.md)

> **See also**: [Back to index](../INDEX.md)

# Examples catalog

Everything under `examples/` runs on the same engine; this page is the editorial map.
The **authoritative inventory** — every numbered example with its process type,
agent/task counts and referenced tools — is the generated
[`examples/INDEX.md`](https://github.com/Orkeon/orkeon/blob/main/examples/INDEX.md): it is produced by
`scripts/generate_examples_index.py` and CI fails when it drifts from the folders on
disk, so no count is maintained by hand here.

## The nine business categories

The numbered YAML crews live in nine thematic folders:

| Category | Folder | Focus |
|----------|--------|-------|
| **01 — Enterprise** | `01-enterprise/` | CRM, due diligence, compliance, HR, supply chain |
| **02 — Science & Research** | `02-science-research/` | Literature analysis, computational biology, open science |
| **03 — Finance & Trading** | `03-finance-trading/` | Algorithmic trading, fraud detection, portfolio consensus |
| **04 — Health & Wellness** | `04-health-wellness/` | Diagnostic support, clinical trials, personalized plans |
| **05 — Education** | `05-education/` | Course design, assessment, tutoring, adaptive learning |
| **06 — Engineering & DevOps** | `06-engineering-devops/` | CI/CD automation, infrastructure, code review, testing |
| **07 — Creative & Media** | `07-creative-media/` | Content generation, scripting, design workflows |
| **08 — IoT & Smart Systems** | `08-iot-smart-systems/` | Monitoring, predictive maintenance, anomaly detection |
| **09 — Experimental** | `09-experimental/` | Prototypes, advanced orchestration research |

## Beyond the numbered crews

| Folder | What it shows |
|--------|---------------|
| `rag/` | The RAG subsystem: `basic-ingestion/`, `hybrid-retrieval/`, `custom-reranker/`, `crew-yaml/`, plus the offline evaluation dataset under `eval/` |
| `raggable-tree/` | Semantic codebase analysis: `basic-indexing/`, `crew-yaml/`, `custom-adapter/` |
| `scripting/` | The TypeScript DSL (`.ork.ts`): hello world → dynamic spawn, FSM/graph literals, custom tools, RAG (`08-rag.ork.ts`) |
| `cli-ts-commands/` | Interactive REPL commands in `*.cmd.ts`, loaded without .NET recompilation |
| `local-embeddings/` | On-device embeddings (no API key) wired into memory and agent selection |
| `crew-multifile/` | A crew described as a directory (`orkeon run <dir>`) |
| `forge/promote-demo/` | The Atelier's offline half: a ready `orkeon forge` session bundled as a workspace — `forge list`, then `forge promote` into an ordinary folder (no LLM needed) |
| `runners/` | The runner projects that execute the numbered examples, incl. two interactive dotnet tools |

## Notable examples

- **Due diligence with NIST audit** — `01-enterprise/07-due-diligence-nist/`:
  hierarchical process, compliance tooling, long-term memory.
- **Algorithmic trading, multi-strategy** — `03-finance-trading/31-algo-trading/`:
  parallel specialists with consensus aggregation.
- **RAG ingestion to cited answers** — `rag/basic-ingestion/`: ingest, retrieve,
  generate with citations — then graduate to `hybrid-retrieval/` and the
  `corrective` profile.
- **Index a codebase, then ask it questions** — `raggable-tree/basic-indexing/`:
  Tree-sitter indexing plus the 15 analysis tools.

## Running an example

```bash
orkeon run examples/crew-multifile/          # a crew directory
orkeon run examples/scripting/01-hello-world.ork.ts
./examples/run-example.sh 7                  # numbered example, from source
docker run -it --rm -e ORKEON_RUNNER=shell ghcr.io/orkeon/orkeon-runners
# then inside the container: orkeon-example list && orkeon-example run 7
```

From C# (via DI):

```csharp
// crewFactory : ICrewFactory, orchestrator : ICrewOrchestrationService
var crew = await crewFactory.CreateFromDirectoryAsync(
    "examples/01-enterprise/07-due-diligence-nist/", ct);
var result = await orchestrator.KickoffAsync(crew.Id, CrewInput.Empty(), ct);
```

Examples ship configuration, not datasets — see the
[example data policy](./example-data-policy.md) for how to mount your own input.
