# 61. Plateforme Learning Gamifie avec Hooks

> Le FlowEngine gere une progression par niveaux avec deverrouillage. Les CrewHooks declenchent les recompenses (XP, badges). La memoire episodique trace les accomplissements.

## Quality

💪 Robustesse — Progression non-lineaire, hooks evenementiels, gamification via CrewHooks

## Architecture

- **Process**: `sequential` (FlowEngine planned for progression tree)
- **Agents**: 3 — Game Master, Challenge Designer, Evaluator
- **Tools**: `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: IFlowEngine (skill tree progression), CrewHooks (OnLevelCompleted, OnAchievementUnlocked), AgentMemory.Episodic, FlowState (player state)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/05-education/61-gamified-learning/config.yaml
```

## What this example demonstrates

- Gamified learning with XP rewards and badge unlocking
- CrewHooks triggering game events (level-up, achievements)
- Episodic memory tracking player progression across sessions
