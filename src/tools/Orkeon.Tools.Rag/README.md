# Orkeon.Tools.Rag

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Tools.Rag** exposes the RAG subsystem (`Orkeon.Rag`) to agents as tools, in the `Tools.*` family (`Orkeon.Tools.Rag`, **not** `Orkeon.Rag.Tools` — see ADR-004/ADR-006).

## Tools

| Tool | Class | Description |
|------|-------|-------------|
| `rag_search` | `RagSearchTool` | Grounded retrieval over `IRagPipeline` (question / `top_k` / `collection`); `collection = "raggable-tree"` routes to the semantic code index (`IRaggableStore`). Output: answer text + `Sources:` block with scores. |
| `rag_ingest` | `RagIngestTool` | Ingests documents into a RAG collection through `IIngestionPipeline` (path or URL, chunking strategy, collection). |
| `rag_eval` | `RagEvalTool` | Runs the offline RAG evaluation harness (`IRagEvalHarness`) on a golden dataset and reports recall@k / MRR. |

## Install

```
dotnet add package Orkeon.Tools --prerelease
```

> This assembly ships inside the [`Orkeon.Tools`](https://www.nuget.org/packages/Orkeon.Tools) package — the seven built-in tool families in one install; it is no longer a standalone NuGet package. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Registration

```csharp
services.AddOrkeonRag(configuration);   // Orkeon.Rag.DependencyInjection — the subsystem
services.AddOrkeonRagTools();           // Orkeon.Tools.Rag.DependencyInjection — the agent tools
```

## Documentation

- [RAG pipeline architecture](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/rag-pipeline.md)
- [ADR-006 — RAG subsystem](https://github.com/Orkeon/orkeon/blob/main/docs/adr/ADR-006-rag-subsystem.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
