# 19. Analyse de Donnees Experimentales

> Parallel-then-sequential pattern: two agents work in parallel (statistics + visualization) then an Interpreter formulates conclusions and a Writer produces the report. ICodeSandbox executes calculations in isolation.

## Quality

💪 Robustesse — Parallelism with synchronization, sandbox for statistical code

## Architecture

- **Process**: `Parallel`
- **Agents**: 4 — Statisticien (Worker), Visualisateur (Worker), Interpreteur (Worker), Redacteur de Rapport (Worker)
- **Tools**: `csv_reader`, `json_tool`, `file_write`
- **Memory**: `InMemory`
- **Key features**: Batch tool execution (parallel phase), task dependencies (sequential phase), `ICodeSandbox` for secure execution
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/19-experimental-data/config.yaml
```

## What this example demonstrates

- Parallel-then-sequential execution pattern for experimental data analysis
- Sandboxed code execution for statistical calculations (R/Python)
- Integrated statistical analysis with visualization and scientific interpretation
