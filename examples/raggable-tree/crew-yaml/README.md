# RaggableTree — Crew from YAML

> A 3-agent hierarchical crew (navigator, analyst, flow_tracer) wired to the
> RaggableTree analysis tools, declared entirely in `crew.yaml` and run by the
> stock `orkeon` CLI — no C# host needed. The runner registers the RaggableTree
> subsystem and its 15 agent tools by default.

## What it does

- `crew.yaml` declares the `codebase-explorer` crew: the navigator (manager)
  indexes the mounted tree on demand with `index_codebase`, then the crew
  answers architectural questions through the analysis tools (`codebase_map`,
  `symbol_detail`, `flow_trace`, `impact_analysis`, …).
- The target codebase is mounted read-only at `/src` with `--mount`; analysed
  languages are auto-detected per `index_codebase` call.

## Prerequisites

An LLM (any configured provider — see `examples/appsettings/`). Indexing itself
needs no API key; running the crew does.

## Run it

```bash
# Validate the crew offline (no LLM call): 3 agents, 12 tools resolved
orkeon run examples/raggable-tree/crew-yaml/crew.yaml --mount .:/src:ro --validate

# Run it against the codebase of your choice
orkeon run examples/raggable-tree/crew-yaml/crew.yaml --mount /path/to/a/codebase:/src:ro
```

From a source checkout, replace `orkeon` with
`dotnet run --project src/scripting/Orkeon.Scripting.Cli --`.

## Tuning the index

The crew file carries only the crew; infrastructure knobs (embedding backend,
index mode, exclude globs) live in the optional `RaggableTree` section of
`--settings`, e.g.:

```json
{ "RaggableTree": { "IndexMode": "Frozen", "Exclude": ["node_modules", "bin", "obj"] } }
```

Opt out entirely with `"RaggableTree": { "Enabled": false }`.

## Approx. duration & cost

- **Duration**: seconds to index a small tree, then LLM latency per question.
- **Cost**: indexing is free (local fingerprints); the crew run uses your LLM.
