# Orkeon.Rag.Abstractions

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Rag.Abstractions** holds the contracts, DTOs and options of the Orkeon RAG subsystem: `IRagPipeline`, `IIngestionPipeline`, `IDocumentStore`, `IChunkingStrategy`, `RagAnswer` with citations and a full stage trace, plus the profile presets (`fast`/`balanced`/`quality`/`corrective`/`adaptive`). Depends only on `Orkeon.Domain`.

## Install

```
dotnet add package Orkeon.Rag.Abstractions
```

## Documentation

- [RAG pipeline](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/rag-pipeline.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
