# 76. Studio Narratif FlowEngine Cyclique

> Le FlowEngine gere les cycles creation-revision. La Composite memory maintient la coherence sur 3 couches : session courante, univers narratif persistant, et chapitres episodiques.

## Quality

💪 Robustesse — FlowEngine cyclique, memoire composite 3 couches, coherence narrative

## Architecture

- **Process**: `sequential` (FlowEngine planned)
- **Agents**: 4 — Plot Writer, Dialogue Writer, Artistic Director, Coherence Editor
- **Tools**: `file_read`, `file_write`, `json_tool`
- **Memory**: `InMemory` (Composite planned: ShortTerm + LongTerm + Episodic)
- **Key features**: IFlowEngine + FlowState (planned), Composite memory 3 layers (planned), iteration limit, IKnowledgeSource (project bible)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/07-creative-media/76-narrative-studio/config.yaml
```

## What this example demonstrates

- Cyclic creation-revision workflow for narrative content
- Three-layer memory maintaining session, universe, and episodic coherence
- Continuity verification across all narrative elements
