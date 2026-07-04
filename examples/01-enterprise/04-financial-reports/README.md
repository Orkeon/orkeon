# 4. Generation de Rapports Financiers avec Reprise

> Four agents work in parallel (API collection, quantitative analysis, compliance verification) then converge to a final writer. ICheckpointManager enables recovery after crashes or API timeouts.

## Quality

✅ Fiabilite — Checkpointing at each step, lossless recovery, parallel batch execution

## Architecture

- **Process**: `Parallel`
- **Agents**: 4 — Collecteur API (Worker), Analyste Quantitatif (Worker), Compliance Officer (Worker), Redacteur Final (Worker)
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `Redis`
- **Key features**: `ICheckpointManager` + `IResumeEngine` for failure recovery, batch tool execution, `CrewHooks` (OnTaskCompleted)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/04-financial-reports/config.yaml
```

## What this example demonstrates

- Parallel-then-sequential convergence pattern for financial data processing
- Checkpoint-based recovery enabling resumption after API timeouts or crashes
- Multi-agent parallel execution with shared Redis memory for intermediate state
