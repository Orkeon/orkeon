# 72. Test de Charge Trending Historique

> Chaque run est compare aux precedents via memoire long-terme. Le BenchmarkRunner assure la reproductibilite. Les regressions de performance sont detectees automatiquement.

## Quality

✅ Fiabilite — Benchmarks reproductibles, trending historique, detection automatique regressions

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Scenario Designer, Test Executor, Results Analyzer, Optimization Recommender
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: BenchmarkRunner (reproducibility), AgentMemory.LongTerm (trending), EvaluationScore (regression detection), InMemoryDataset (baseline)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/06-engineering-devops/72-load-testing/config.yaml
```

## What this example demonstrates

- Reproducible load testing with historical baseline comparison
- Automatic performance regression detection across runs
- Long-term memory for trending performance metrics over time
