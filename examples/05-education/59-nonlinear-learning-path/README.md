# 59. Parcours de Formation Non-Lineaire

> Le FlowEngine gere un parcours a branchements : si l'apprenant echoue, le flow bifurque vers du renforcement. Si il reussit, il avance. Le LlmFlowStep prend les decisions de branchement.

## Quality

🎯 Simplicite — Flow non-lineaire adaptatif, branchement LLM-driven, zero configuration manuelle

## Architecture

- **Process**: `sequential` (FlowEngine planned)
- **Agents**: 4 — Gap Diagnostician, Pedagogical Architect, Resource Curator, Calendar Planner
- **Tools**: `http_api`, `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: IFlowEngine + FlowState (conditional branching), LlmFlowStep (decision), AgentMemory.Episodic, IKnowledgeSource
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/05-education/59-nonlinear-learning-path/config.yaml
```

## What this example demonstrates

- Non-linear learning paths with conditional branching based on learner performance
- LLM-driven branching decisions for remediation vs advancement
- Episodic memory tracking achievements and failures across sessions
