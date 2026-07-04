# 75. Chaos Engineering Humain Approuve

> Le HumanAgent valide chaque injection de perturbation. L'ObserverAgent mesure l'impact en temps reel. Le ICheckpointManager permet le rollback si necessaire.

## Quality

🔒 Securite — Validation humaine par injection, observation temps reel, rollback possible

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Hypothesis Formulator, Fault Injector, Impact Measurer, Results Analyst
- **Tools**: `http_api`, `json_tool`, `file_write`
- **Memory**: `Redis`
- **Key features**: HumanInputContext (approval before each injection), ObserverAgent, TaskCallbacks (threshold alerts), ICheckpointManager (rollback)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/06-engineering-devops/75-chaos-engineering/config.yaml
```

## What this example demonstrates

- Mandatory human approval before each fault injection
- Real-time impact monitoring during chaos experiments
- Hypothesis-driven experimentation with structured results analysis
