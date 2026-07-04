# 98. Archeologie Numerique de Codebase Legacy

> Five agents explore an unknown codebase and produce complete documentation. Composite memory progressively builds a mental model. FlowEngine manages iterative exploration.

## Quality

🎯 Simplicite — Point to a repository, get complete documentation with evolving composite memory

## Architecture

- **Process**: `sequential` (with FlowEngine iterative loops)
- **Agents**: 5 — Structure Cartographer, Git Archaeologist, Pattern Decoder, Documenter, Modernization Planner
- **Tools**: `directory_read`, `file_read`, `github`, `json_tool`, `file_write`
- **Memory**: `SQLite` (composite: ShortTerm session + LongTerm codebase mental model, planned)
- **Key features**: Composite memory (planned), IFlowEngine, IDocumentLoader, ITextChunker, IKnowledgeSource, IContextWindowManager
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/09-experimental/98-legacy-code-archaeology/config.yaml
```

## What this example demonstrates

- Progressive codebase understanding through specialized exploration agents
- Composite memory that builds a mental model enriched by each exploration phase
- End-to-end pipeline from raw codebase to documentation and modernization plan
