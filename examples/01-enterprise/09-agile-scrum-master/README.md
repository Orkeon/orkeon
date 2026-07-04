# 9. Gestion de Projet Agile — Scrum Master IA

> A Manager Agent acts as Scrum Master, decomposing epics into tasks and assigning them to the most qualified agents via semantic embedding-based skill matching. DelegationPerformanceReport measures assignment effectiveness.

## Quality

💪 Robustesse — Semantic agent/task matching, delegation performance measurement

## Architecture

- **Process**: `Hierarchical`
- **Agents**: 4 — Scrum Master IA (Manager), Developpeur Backend (Worker), Developpeur Frontend (Worker), Ingenieur DevOps (Worker)
- **Tools**: `http_api`, `json_tool`, `file_write`
- **Memory**: `Redis`
- **Key features**: Semantic selection by embeddings, `AgentSkill` (A2A) for skill descriptions, `DelegationParameters`, `DelegationPerformanceReport`
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/09-agile-scrum-master/config.yaml
```

## What this example demonstrates

- Semantic embedding-based skill matching for intelligent task assignment
- Hierarchical delegation from Scrum Master to specialized developers
- Performance tracking of delegation effectiveness across assignments
