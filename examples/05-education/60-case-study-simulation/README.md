# 60. Simulation de Cas Pratiques Immersive

> Des agents jouent des roles dans un scenario interactif configurable en YAML. L'apprenant interagit via HumanAgent. Un ObserverAgent evalue en temps reel les competences demontrees.

## Quality

💪 Robustesse — Scenarios YAML, interaction multi-modale, evaluation temps reel

## Architecture

- **Process**: `sequential` (FlowEngine cycles planned)
- **Agents**: 3 — Scenario Master, Client Persona, Competency Observer
- **Tools**: `json_tool`, `file_write`
- **Memory**: `InMemory`
- **Key features**: HumanInputContext (Text, MultipleChoice, Confirmation), IFlowEngine (cycles), ObserverAgent (evaluation), EvaluationScore, YAML scenarios
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/05-education/60-case-study-simulation/config.yaml
```

## What this example demonstrates

- Immersive role-playing simulation with AI-driven characters
- Real-time competency evaluation during interactive scenarios
- Human-in-the-loop interaction with multi-modal input types
