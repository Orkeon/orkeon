# 20. Veille Brevets avec Deduplication Semantique

> Continuous patent monitoring with Redis vector memory. Each new patent is compared semantically against existing entries via cosine similarity before addition. Zero duplicates guaranteed.

## Quality

✅ Fiabilite — Semantic vector deduplication, distributed memory, zero duplicates

## Architecture

- **Process**: `Parallel`
- **Agents**: 4 — Crawler EPO/USPTO (Worker), Classificateur Domaine (Worker), Analyste Paysage (Worker), Alerteur (Worker)
- **Tools**: `http_api`, `pdf_reader`, `json_tool`, `file_write`
- **Memory**: `Redis`
- **Key features**: Vector search cosine similarity, `MemoryEvents` for alert triggering, relevance scoring (0-1)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/20-patent-monitoring/config.yaml
```

## What this example demonstrates

- Semantic deduplication using Redis vector search with cosine similarity
- Continuous patent monitoring with memory persistence across runs
- Relevance-based alerting that filters noise from genuinely new findings
