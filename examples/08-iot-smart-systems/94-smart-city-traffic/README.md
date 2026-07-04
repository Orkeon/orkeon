# 94. Smart City Trafic — A2A Inter-Zones

> Per-zone traffic agents communicating via A2A protocol with adjacent zones. The A2ATaskRouter routes requests. Decentralized coordination enables horizontal scaling.

## Quality

💪 Robustesse — A2A inter-zone communication, decentralized coordination, horizontal scalability

## Architecture

- **Process**: `parallel` (per zone) + A2A (inter-zone)
- **Agents**: 6 — Zone North, Zone South, Zone East, Zone West, Global Modeler, Display Informer
- **Tools**: `http_api`, `json_tool`, `file_write`
- **Memory**: `Redis` (global traffic state)
- **Key features**: A2A protocol (inter-zones), AgentCard + AgentSkill per zone, Direct communication (zone to zone), A2ATaskRouter, agent discovery
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key
3. Redis instance running for shared traffic state

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/08-iot-smart-systems/94-smart-city-traffic/config.yaml
```

## What this example demonstrates

- A2A protocol for decentralized inter-zone traffic coordination
- Per-zone autonomous agents that scale horizontally as the city grows
- Global traffic model synthesis from distributed zone-level data
