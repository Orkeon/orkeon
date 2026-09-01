# Orkeon.Rag

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Rag** implements the RAG subsystem: document loaders, 4 chunking strategies, ingestion-path validation (prompt-injection screening included), the staged retrieval pipeline (transform → retrieve → fuse → rerank → assemble → cited generation → groundedness), hybrid BM25+RRF, the corrective CRAG graph, and the offline evaluation harness. Opt-in: `AddOrkeonRag(configuration)`.

## Install

```
dotnet add package Orkeon --prerelease
```

> This assembly ships inside the [`Orkeon`](https://www.nuget.org/packages/Orkeon) umbrella package — the whole framework in one install; it is no longer a standalone NuGet package. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [RAG pipeline](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/rag-pipeline.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
