# 89. Monitoring Environnemental — ObserverAgent + Broadcast Alertes

> Four parallel agents monitor environmental dimensions (air, water, biodiversity, soil). An ObserverAgent triggers alerts via TaskCallbacks. Broadcast ensures universal alert diffusion.

## Quality

✅ Fiabilite — Real-time parallel monitoring, broadcast alerts, composite environmental score

## Architecture

- **Process**: `parallel`
- **Agents**: 6 — Air Monitor, Water Monitor, Biodiversity Monitor, Soil Monitor, Score Integrator, Alert Observer
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `Redis` (real-time state)
- **Key features**: Batch execution, ObserverAgent, TaskCallbacks (alerts), Broadcast communication, MemoryEvents, EvaluationScore
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key
3. Redis instance running for real-time shared state

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/08-iot-smart-systems/89-environmental-monitoring/config.yaml
```

## What this example demonstrates

- Parallel environmental monitoring across four independent dimensions
- ObserverAgent pattern for continuous threshold surveillance and alert triggering
- Broadcast communication to ensure all agents receive critical alerts simultaneously
