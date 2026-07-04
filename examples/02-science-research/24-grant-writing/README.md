# 24. Redaction de Demandes de Subventions

> 100% YAML-configurable pipeline. Each section of the grant application is a Task with its own validation. The Human Agent (PI) validates each section before progressing to the next.

## Quality

🎯 Simplicite — 100% declarative configuration, human validation per section, zero code

## Architecture

- **Process**: `Sequential`
- **Agents**: 5 — Stratege Appels (Worker), Redacteur Scientifique (Worker), Gestionnaire Budget (Worker), Reviewer Global (Worker), PI Validateur (Human)
- **Tools**: `http_api`, `file_read`, `file_write`, `csv_reader`, `json_tool`
- **Memory**: `InMemory`
- **Key features**: `CrewConfiguration` YAML, `HumanInputContext` (type: text + confirmation), output validation per section, `TaskContext` typed
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/24-grant-writing/config.yaml
```

## What this example demonstrates

- Fully declarative YAML-driven grant writing pipeline
- Human-in-the-loop PI validation at each critical section
- Budget preparation with automated compliance checking against funder guidelines
