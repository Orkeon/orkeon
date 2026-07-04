# 26. Construction de Graphe de Connaissances

> Five agents build a knowledge graph iteratively via FlowEngine. Each cycle refines the graph with feedback. Iteration limits guarantee convergence.

## Quality

💪 Robustesse — Controlled loops via FlowEngine, guaranteed convergence through guard rails

## Architecture

- **Process**: Cyclic via `FlowEngine`
- **Agents**: 5 — Extracteur Entites (Worker), Detecteur Relations (Worker), Classificateur Taxonomique (Worker), Detecteur Contradictions (Worker), Visualisateur Graphe (Worker)
- **Tools**: `pdf_reader`, `json_tool`, `http_api`, `file_write`
- **Memory**: `Redis`
- **Key features**: `IFlowEngine` + `FlowState` (iterative cycles), `FlowStepBase<TInput, TOutput>`, iteration limit, `IKnowledgeSource` (feeding)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/26-knowledge-graph/config.yaml
```

## What this example demonstrates

- Iterative knowledge graph construction with FlowEngine cyclic processing
- Contradiction detection and resolution for graph consistency
- Multi-step entity extraction, relation detection, and taxonomic organization
