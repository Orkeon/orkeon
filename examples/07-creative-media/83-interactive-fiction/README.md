# 83. Fiction Interactive FlowEngine

> Le FlowEngine gere un arbre de choix narratifs. Le lecteur (HumanAgent) fait des choix qui influencent la suite. Un ObserverAgent verifie la coherence narrative en temps reel.

## Quality

💪 Robustesse — FlowEngine arborescent, choix du lecteur, experience unique, coherence verifiee

## Architecture

- **Process**: `sequential` (FlowEngine tree planned)
- **Agents**: 3 — Narrator, Ambiance Writer, Consistency Checker
- **Tools**: `json_tool`, `file_read`, `file_write`
- **Memory**: `SQLite`
- **Key features**: IFlowEngine + FlowState (choice tree), HumanInputContext (MultipleChoice), ObserverAgent (coherence), AgentMemory.Episodic
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/07-creative-media/83-interactive-fiction/config.yaml
```

## What this example demonstrates

- Branching narrative tree with reader-driven choices
- Real-time narrative coherence verification across branches
- Episodic memory tracking reader choice history for unique experiences
