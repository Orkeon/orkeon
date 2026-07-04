# 10. Analyse de Sentiment Client Multi-Plateforme

> Three agents collect and analyze reviews in parallel from different platforms (Amazon, Google, Social). An Aggregator merges into thematic scores. Composite memory (SQLite + Redis) enables historical trending and real-time analysis.

## Quality

✅ Fiabilite — Dual SQLite/Redis memory, historical trending, robust aggregation

## Architecture

- **Process**: `Parallel`
- **Agents**: 4 — Collecteur Amazon (Worker), Collecteur Google (Worker), Collecteur Reseaux Sociaux (Worker), Agregateur de Sentiments (Worker)
- **Tools**: `web_scrape`, `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `SQLite` + `Redis` (Composite 🔮)
- **Key features**: Batch tool execution, Composite memory 🔮 (short-term + long-term), `EvaluationScore` for sentiment scoring
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/10-sentiment-analysis/config.yaml
```

## What this example demonstrates

- Parallel multi-platform sentiment collection with batch tool execution
- Composite memory pattern combining SQLite (historical) and Redis (real-time) -- planned feature
- Cross-platform sentiment aggregation with unified thematic scoring
