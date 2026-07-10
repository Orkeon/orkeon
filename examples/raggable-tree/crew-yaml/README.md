# RaggableTree — Crew from YAML

> Builds a RaggableTree index and shows how a YAML-defined crew consumes it. The
> `crew.yaml` here declares a 3-agent hierarchical crew (navigator, analyst,
> flow_tracer) wired to the RaggableTree agent tools.

## What it does

- Loads `crew.yaml`, which describes the RaggableTree settings **and** a
  `codebase-explorer` crew (3 agents, `hierarchical` process, a tool allow-list
  and a budget guardrail).
- Mounts a target directory read-only at `/src` and builds the index with the
  TypeScript and C# adapters.
- Prints the resulting node/edge counts and explains how the crew's tools
  (`codebase_map`, `symbol_detail`, `flow_trace`, …) consume that index.

This program builds and explains the index; wiring the crew to an actual runner
to answer questions is left as the next step (the YAML is ready for it). See the
[RaggableTree guide](../../../docs/architecture/raggable-tree.md).

## Prerequisites

- .NET SDK ≥ 10.0.300
- The `crew.yaml` references `${OPENAI_API_KEY}` for its embedding provider. The
  indexing performed by this example does not call it, but wiring the crew to a
  runner would — export it before running the crew for real.

## Required data

None to provide. The example indexes the directory you pass (or the current
directory by default). `crew.yaml` is copied next to the built binary.

## Run it

```bash
# Uses ./crew.yaml and indexes the current directory
dotnet run --project examples/raggable-tree/crew-yaml

# Or pass a YAML path and a directory to index
dotnet run --project examples/raggable-tree/crew-yaml -- crew.yaml /path/to/a/codebase
```

## Expected output

```
Loading crew config from crew.yaml
Indexing /path/to/a/codebase (mounted at /src)
Indexed <n> nodes, <n> edges.
The YAML file declares a 3-agent crew (navigator, analyst, flow_tracer)
that consumes the produced index via the RaggableTree tools registered by
AddRaggableTreeTools. Wire the crew runner of your choice to execute it.
```

## Approx. duration & cost

- **Duration**: seconds for a small tree.
- **Cost**: none for indexing (the crew itself, if run, would use embeddings + an LLM).
</content>
