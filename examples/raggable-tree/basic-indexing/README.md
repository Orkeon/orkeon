# RaggableTree — Basic indexing

> The smallest end-to-end RaggableTree example: point it at a directory and it
> builds the semantic code graph, then prints a summary of what it found.

## What it does

- Mounts a target directory read-only into the VFS at `/src`.
- Runs `RaggableTreeBuilder` with the five built-in language adapters
  (TypeScript, C#, Python, Go, Rust).
- Prints the index id and the node/edge/module/symbol counts.

This is a plain console program (no LLM, no crew runner) — it exercises the
indexing pipeline directly. See the [RaggableTree guide](../../../docs/architecture/raggable-tree.md)
for the full architecture.

## Prerequisites

- .NET SDK ≥ 10.0.300

No API key or `appsettings` needed — indexing is fully local.

## Required data

None to provide: the example indexes whatever directory you pass (or the current
directory by default). It skips `node_modules`, `dist`, `.git`, `bin`, `obj`.

## Run it

```bash
# Index the current directory
dotnet run --project examples/raggable-tree/basic-indexing

# Or index a specific directory
dotnet run --project examples/raggable-tree/basic-indexing -- /path/to/a/codebase
```

## Expected output

```
Indexing /path/to/a/codebase (mounted at /src)...
  IndexId : <guid>
  Files   : <n>
  Nodes   : <n>
  Edges   : <n>
  Modules : <n>
  Symbols : <n>
```

## Approx. duration & cost

- **Duration**: seconds for a small tree; scales with file count.
- **Cost**: none — no LLM calls.
</content>
