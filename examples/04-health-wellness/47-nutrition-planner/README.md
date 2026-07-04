# 47. Planification Nutritionnelle YAML-Driven

> Configuration des contraintes alimentaires (allergies, preferences, objectifs caloriques) entierement en YAML. Les agents s'adaptent automatiquement sans modifier le code. La memoire suit l'evolution du patient.

## Quality

:dart: Simplicite -- Configuration YAML pure, adaptation automatique, zero code

## Architecture

- **Process**: `sequential`
- **Agents**: 4 -- Dietary Profile Analyst, Recipe Designer, Nutritional Verifier, Daily Coach
- **Tools**: `http_api`, `json_tool`, `csv_reader`, `file_write`
- **Memory**: `SQLite` (episodic per patient)
- **Key features**: AgentConfiguration YAML (dietary constraints), AgentMemory.Episodic, TaskContext typed, IKnowledgeSource (nutritional tables)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/04-health-wellness/47-nutrition-planner/config.yaml
```

## What this example demonstrates

- Fully YAML-driven dietary constraint configuration with zero code changes
- Episodic memory tracking patient progress and preferences across sessions
- End-to-end nutrition pipeline from profiling to daily coaching
