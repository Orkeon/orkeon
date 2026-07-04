# 101. Crew de Crews — L'Orchestre des Orchestres

> The ultimate case: a meta-crew where each "agent" is itself a complete crew. The Conductor routes tasks to sub-crews via semantic embedding selection. Sub-crews communicate via A2A and share Redis memory. This example demonstrates all four framework qualities simultaneously.

## Quality

🎯🔒💪✅ — **Simplicity** (each sub-crew is simple on its own), **Reliability** (multi-level checkpointing, distributed memory), **Robustness** (fractal composition, errors contained per level), **Security** (multi-level audit trail, NIST compliance, human validation at critical points)

## Architecture

- **Process**: `hierarchical` (meta) + variable internal process per sub-crew
- **Agents**: 5 leads — Conductor (manager), Research Lead (5 agents), Development Lead (4 agents), Test Lead (3 agents), Documentation Lead (3 agents)
- **Tools**: `web_scrape`, `http_api`, `json_tool`, `file_write`, `file_read`, `shell_command`, `directory_read`, `semantic_search`
- **Memory**: `Redis` (shared inter-crew) + `SQLite` (per sub-crew)
- **Key features**: Semantic embedding selection, A2A protocol (inter-crews), ICheckpointManager (per level), Feedback communication, CrewHooks, INistComplianceReporter, AuditEventTypes, IConfigurationVersioning (planned), DelegationPerformanceReport, EvaluationSuite
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key
3. Redis instance running for shared inter-crew memory

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/09-experimental/101-crew-of-crews/config.yaml
```

## What this example demonstrates

- Fractal crew composition where each agent encapsulates an entire crew
- Semantic embedding-based task routing for intelligent sub-crew selection
- Multi-level checkpointing, audit trail, and distributed memory across the crew hierarchy
