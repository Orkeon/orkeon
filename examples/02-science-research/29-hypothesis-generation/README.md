# 29. Generation et Test d'Hypotheses Cyclique

> Four agents in a cyclic flow: observation, hypothesis generation, critique, experimental planning. FlowEngine manages the cycle with typed states and an explicit exit condition.

## Quality

💪 Robustesse — FlowEngine with typed states, exit condition, no infinite loops

## Architecture

- **Process**: Cyclic via `FlowEngine`
- **Agents**: 4 — Observateur Patterns (Worker), Creatif Hypotheses (Worker), Critique (Worker), Planificateur Experimental (Worker)
- **Tools**: `csv_reader`, `http_api`, `json_tool`, `file_write`
- **Memory**: `Redis`
- **Key features**: `IFlowEngine` + `FlowState`, `FlowStepBase<TInput, TOutput>`, `LlmFlowStep` (branching decisions), exit condition, iteration limit
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/29-hypothesis-generation/config.yaml
```

## What this example demonstrates

- Cyclic scientific method workflow (observe -> hypothesize -> critique -> plan)
- FlowEngine with typed states and explicit exit conditions preventing infinite loops
- LLM-driven branching decisions for hypothesis selection and prioritization
