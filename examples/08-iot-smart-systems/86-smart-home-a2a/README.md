# 86. Maison Intelligente — Protocole A2A Natif

> Smart home where each subsystem (heating, lighting, security, energy) is an autonomous agent. The A2A protocol enables automatic discovery and communication between agents. Broadcast notifies all subsystems of state changes.

## Quality

💪 Robustesse — A2A native protocol, automatic agent discovery, reliable broadcast communication

## Architecture

- **Process**: `parallel`
- **Agents**: 5 — Home Supervisor (manager), Heating, Lighting, Security, Energy
- **Tools**: `http_api`, `json_tool`
- **Memory**: `Redis` (shared real-time state)
- **Key features**: A2A protocol (AgentCard, AgentSkill), Broadcast communication, agent discovery, conflict resolution
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key
3. Redis instance running for shared state

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/08-iot-smart-systems/86-smart-home-a2a/config.yaml
```

## What this example demonstrates

- A2A protocol for automatic agent discovery and inter-agent communication
- Parallel autonomous subsystems with broadcast state synchronization
- Conflict resolution between competing subsystem demands (e.g., comfort vs energy savings)
