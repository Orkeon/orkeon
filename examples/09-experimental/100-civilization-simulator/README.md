# 100. Simulateur de Civilisation Emergente

> Ecosystem of 20+ agents representing factions. FlowEngine manages turns. A2A protocol enables inter-faction diplomacy. Episodic memory creates a unique emergent history.

## Quality

💪 Robustesse — A2A diplomatic protocol, FlowEngine turn-based progression, civilizational memory, emergent history

## Architecture

- **Process**: `sequential` (turns via FlowEngine + A2A inter-factions)
- **Agents**: 8 — Game Master (manager), Realm Leader + General + Scholar, Forest Chief + Shaman, Merchant Guild Master, Historian-Chronicler (observer)
- **Tools**: `json_tool`, `file_write`
- **Memory**: `SQLite` (composite: ShortTerm current turn + LongTerm civilization history + Episodic landmark events, planned)
- **Key features**: A2A protocol (diplomacy), IFlowEngine + FlowState (turns), Composite memory 3-layer (planned), ObserverAgent (chronicler), AgentCard + AgentSkill, CrewHooks
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/09-experimental/100-civilization-simulator/config.yaml
```

## What this example demonstrates

- Turn-based civilization simulation with FlowEngine managing game progression
- A2A protocol enabling emergent diplomacy (alliances, trade, betrayals) between factions
- Episodic memory creating a unique emergent history that shapes future faction behavior
