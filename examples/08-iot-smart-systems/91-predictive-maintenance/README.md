# 91. Maintenance Predictive — ObserverAgent par Machine

> One ObserverAgent per critical machine. Episodic memory per machine enables failure prediction by pattern comparison. TaskPriority ensures critical machines are serviced first.

## Quality

✅ Fiabilite — Machine-specific failure prediction from historical patterns, urgency prioritization, checkpointing

## Architecture

- **Process**: `parallel` (monitoring) then `sequential` (intervention planning)
- **Agents**: 6 — Pump Observer, Compressor Observer, Conveyor Observer, Diagnostician, Maintenance Planner, Parts Inventory Manager
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `database_query`, `file_write`
- **Memory**: `SQLite` (episodic per machine)
- **Key features**: ObserverAgent, AgentMemory.Episodic per machine, ICheckpointManager, TaskCallbacks, TaskPriority
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/08-iot-smart-systems/91-predictive-maintenance/config.yaml
```

## What this example demonstrates

- Per-machine ObserverAgent pattern for continuous health monitoring
- Episodic memory for comparing current sensor patterns against past failure signatures
- Prioritized maintenance scheduling based on machine criticality and failure urgency
