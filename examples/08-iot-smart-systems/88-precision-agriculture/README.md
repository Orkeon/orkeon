# 88. Agriculture de Precision — Humain Valide les Actions Physiques

> Precision agriculture where agents connected to IoT sensors analyze field data. The HumanAgent (farmer) validates every physical action (irrigation, chemical treatment). Rate limiting protects sensor APIs.

## Quality

🔒 Securite — Human validation required for all physical actions, rate limiting on sensor APIs, field history tracking

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Sensor Data Collector, Agronomist Analyst, Action Planner, Farmer (Human Validator)
- **Tools**: `http_api`, `json_tool`, `csv_reader`, `file_write`
- **Memory**: `SQLite` (parcel history)
- **Key features**: HumanInputContext (physical action validation), LlmRateLimiter, TaskCallbacks (threshold alerts), AgentMemory.Episodic
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/08-iot-smart-systems/88-precision-agriculture/config.yaml
```

## What this example demonstrates

- Human-in-the-loop validation for all physical interventions on crops
- IoT sensor data collection with rate limiting to protect infrastructure
- Episodic memory for comparing current readings with historical parcel data
