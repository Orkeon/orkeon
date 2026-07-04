# 52. Pharmacovigilance Deduplication Semantique

> Monitoring parallele de 3 sources (rapports officiels, litterature, reseaux sociaux) avec deduplication semantique vectorielle. Le rate limiting protege les APIs sensibles.

## Quality

:white_check_mark: Fiabilite -- Deduplication semantique, rate limiting, monitoring multi-source continu

## Architecture

- **Process**: `parallel`
- **Agents**: 5 -- Official Report Monitor, Literature Monitor, Social Monitor, Signal Detector, Risk Evaluator
- **Tools**: `http_api`, `web_scrape`, `pdf_reader`, `json_tool`, `semantic_search`, `file_write`
- **Memory**: `Redis` (semantic vector deduplication)
- **Key features**: Batch execution, LlmRateLimiter, vector search (deduplication), TaskCallbacks (alerts), MemoryEvents
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/04-health-wellness/52-pharmacovigilance/config.yaml
```

## What this example demonstrates

- Parallel monitoring of three independent adverse event data sources
- Semantic vector deduplication eliminating duplicate reports across sources
- Statistical signal detection with disproportionality analysis (PRR, ROR)
