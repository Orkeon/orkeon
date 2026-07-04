# 67. Incident Response — Humain Valide en Prod

> Detection, Diagnostic, Remediation, Post-mortem. Le HumanAgent valide toute action touchant la production. Le streaming permet le suivi temps reel de la resolution.

## Quality

🔒 Securite — Validation humaine pour prod, streaming temps reel, tracabilite complete

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Incident Detector, Root Cause Diagnostician, Remediator, Post-Mortem Communicator
- **Tools**: `http_api`, `database_query`, `json_tool`, `file_write`
- **Memory**: `Redis`
- **Key features**: HumanInputContext (validation actions prod), streaming, TaskCallbacks (alerts), CrewHooks
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/06-engineering-devops/67-incident-response/config.yaml
```

## What this example demonstrates

- Mandatory human approval for all production-touching actions
- Systematic root cause analysis with evidence chain
- Blameless post-mortem generation with preventive action items
