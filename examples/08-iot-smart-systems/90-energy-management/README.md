# 90. Gestion Energetique — Cycle Continu FlowEngine

> The FlowEngine manages a continuous cycle of monitoring, prediction, optimization, and reporting. Long-term memory enables consumption prediction based on historical patterns.

## Quality

✅ Fiabilite — Continuous FlowEngine cycle, history-based prediction, measurable optimization

## Architecture

- **Process**: `sequential` (cyclic via FlowEngine)
- **Agents**: 4 — Consumption Monitor, Peak Forecaster, Parameter Optimizer, Efficiency Reporter
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `SQLite` (consumption history)
- **Key features**: IFlowEngine (continuous cycle), FlowState, AgentMemory.LongTerm, EvaluationScore (energy efficiency)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/08-iot-smart-systems/90-energy-management/config.yaml
```

## What this example demonstrates

- Continuous monitoring-prediction-optimization cycle managed by FlowEngine
- Long-term memory for historical consumption pattern analysis and prediction
- Measurable energy efficiency improvements tracked across cycles
