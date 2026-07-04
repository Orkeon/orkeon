# 27. Prediction de Tendances avec Auto-Calibration

> Parallel agents analyze weak signals (publications, patents, funding). Long-term memory compares past predictions with actual outcomes for automatic bias correction.

## Quality

✅ Fiabilite — Persistent memory, automatic bias correction, continuous backtesting

## Architecture

- **Process**: `Parallel`
- **Agents**: 4 — Analyseur Publications (Worker), Analyseur Brevets (Worker), Analyseur Financements (Worker), Futuriste-Synthetiseur (Worker)
- **Tools**: `http_api`, `web_scrape`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: `AgentMemory.LongTerm` (auto-calibration), `EvaluationScore` (accuracy tracking), `BenchmarkRunner` (backtesting)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/27-trend-prediction/config.yaml
```

## What this example demonstrates

- Multi-source weak signal analysis (publications, patents, funding) in parallel
- Long-term memory enabling auto-calibration of prediction confidence
- Backtesting predictions against actual outcomes for continuous improvement
