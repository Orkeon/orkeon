# 87. Gestion de Flotte — Mémoire par Véhicule + Checkpoint Missions

> Fleet management with one agent per vehicle, each with its own SQLite memory. The ICheckpointManager saves mission state at each stop. An ObserverAgent monitors driving safety continuously.

## Quality

✅ Fiabilite — Per-vehicle SQLite memory, mission checkpointing, continuous safety monitoring

## Architecture

- **Process**: `hierarchical`
- **Agents**: 5 — Fleet Dispatcher (manager), Vehicle Alpha, Vehicle Bravo, Maintenance Coordinator, Safety Observer
- **Tools**: `http_api`, `json_tool`, `csv_reader`, `file_write`
- **Memory**: `SQLite` (one database per vehicle)
- **Key features**: ICheckpointManager (missions), ObserverAgent, per-vehicle AgentMemory, DelegationEvents, AgentStatus
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/08-iot-smart-systems/87-fleet-management/config.yaml
```

## What this example demonstrates

- Per-vehicle agent memory with dedicated SQLite databases
- Mission checkpointing for reliable delivery tracking and recovery
- Continuous safety monitoring via ObserverAgent with real-time alerts
