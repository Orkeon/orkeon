# 95. Gestion de Crise — Broadcast + Priorite Critique

> Multi-agency emergency coordination. Broadcast protocol ensures universal alert diffusion. TaskPriority.Critical guarantees immediate handling of life-threatening tasks.

## Quality

🔒 Securite — Audit trail, human validation for critical decisions, reliable broadcast, critical priority handling

## Architecture

- **Process**: `hierarchical`
- **Agents**: 6 — Crisis Coordinator (manager), Event Detector, Impact Assessor, Resource Allocator, Public Communicator, Decision Maker (Human)
- **Tools**: `http_api`, `web_scrape`, `json_tool`, `file_write`
- **Memory**: `Redis` (real-time crisis state)
- **Key features**: Broadcast communication, HumanInputContext (critical decisions), AuditEventTypes, CrewHooks, AgentStatus, TaskPriority.Critical
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key
3. Redis instance running for real-time crisis state sharing

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/08-iot-smart-systems/95-crisis-management/config.yaml
```

## What this example demonstrates

- Broadcast communication ensuring all agents receive critical alerts simultaneously
- Human-in-the-loop for irreversible large-scale decisions (evacuation, emergency declarations)
- Complete audit trail of all crisis response actions for post-incident review
