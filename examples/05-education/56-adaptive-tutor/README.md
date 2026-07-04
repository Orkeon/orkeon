# 56. Tuteur Adaptatif — Modele Apprenant Persistant

> Le modele de l'apprenant est persiste en SQLite et s'enrichit a chaque session. Le tuteur adapte non seulement le niveau mais aussi le style pedagogique memorise.

## Quality

✅ Fiabilite — Modele persistant multi-session, adaptation multi-dimensionnelle

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Level Assessor, Adaptive Pedagogue, Motivation Coach, Examiner
- **Tools**: `json_tool`, `file_read`, `file_write`
- **Memory**: `SQLite`
- **Key features**: AgentMemory.LongTerm (learner profile), AgentMemory.Episodic (session history), EvaluationSuite for diagnostics, IContextWindowManager
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/05-education/56-adaptive-tutor/config.yaml
```

## What this example demonstrates

- Persistent learner model that enriches across sessions via long-term memory
- Multi-dimensional adaptation (level + pedagogical style)
- Sequential pipeline: diagnose, teach, motivate, assess
