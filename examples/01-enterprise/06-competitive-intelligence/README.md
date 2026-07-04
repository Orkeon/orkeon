# 6. Veille Concurrentielle Continue

> Five agents monitor different competitive dimensions in parallel. Redis memory persists observations between runs. An Observer Agent detects significant changes via semantic comparison with previous entries.

## Quality

✅ Fiabilite — Distributed memory, schedulable execution, zero context loss between runs

## Architecture

- **Process**: `Parallel`
- **Agents**: 5 — Scraper Web (Worker), Analyste Brevets (Worker), Moniteur Prix (Worker), Synthetiseur (Worker), Detecteur de Changements (Observer)
- **Tools**: `web_scrape`, `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `Redis`
- **Key features**: `ObserverAgent` with `AgentStatus` tracking, `MemoryEvents` for detecting new entries, long-term memory
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/06-competitive-intelligence/config.yaml
```

## What this example demonstrates

- Parallel competitive monitoring across web, patents, and pricing dimensions
- Observer Agent pattern for semantic change detection against historical data
- Persistent Redis memory enabling continuous intelligence across execution runs
