# 36. Prevision de Tresorerie Auto-Corrective

> Les modeles s'affinent via memoire long-terme qui compare predictions passees vs realisations. Le BenchmarkRunner mesure l'accuracy et le systeme corrige automatiquement ses biais.

## Quality

:white_check_mark: Fiabilite -- Memoire persistante, auto-correction des biais, amelioration continue mesurable

## Architecture

- **Process**: `sequential`
- **Agents**: 4 -- Pattern Historian, Invoice Collector, Cash Flow Modeler, Stress Scenario Planner
- **Tools**: `csv_reader`, `http_api`, `json_tool`, `database_query`, `file_write`
- **Memory**: `SQLite` (historical predictions vs actuals)
- **Key features**: AgentMemory.LongTerm (auto-calibration), EvaluationScore (accuracy), IResumeEngine, BenchmarkRunner (backtesting)
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/36-cash-flow-forecast/config.yaml
```

## What this example demonstrates

- Self-correcting forecast models that learn from past prediction errors
- Long-term memory storing prediction accuracy for continuous bias correction
- Stress testing with multiple severity scenarios and contingency planning
