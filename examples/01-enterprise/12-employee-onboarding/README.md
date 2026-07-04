# 12. Onboarding Automatise des Employes

> Fully YAML-configurable onboarding crew. Each onboarding step is a task with its own validation. Mentor selection uses semantic embedding-based skill matching.

## Quality

🎯 Simplicite — 100% declarative YAML configuration, zero custom code, automatic execution

## Architecture

- **Process**: `Sequential`
- **Agents**: 4 — Documentaliste (Worker), Formateur (Worker), Matcher Mentor (Worker), Planificateur (Worker)
- **Tools**: `file_write`, `http_api`, `json_tool`
- **Memory**: `InMemory`
- **Key features**: `CrewConfiguration` full YAML, `TaskCallbacks` for step tracking, semantic selection by embeddings for mentor matching, `AgentConfiguration`
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/12-employee-onboarding/config.yaml
```

## What this example demonstrates

- Fully declarative YAML-driven onboarding pipeline with zero custom code
- Semantic embedding-based mentor matching using skill profiles
- Step-by-step onboarding with TaskCallbacks for progress tracking
