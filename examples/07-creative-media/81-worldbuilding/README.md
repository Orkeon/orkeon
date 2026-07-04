# 81. Worldbuilding Coherent Consensus Croise

> Quatre agents construisent un monde fictif en parallele puis valident la coherence croisee par consensus. La memoire composite maintient la bible du monde.

## Quality

✅ Fiabilite — Memoire composite, coherence verifiable par consensus croise inter-dimensions

## Architecture

- **Process**: `consensual`
- **Agents**: 5 — Geographer, Historian, Sociologist, Linguist, Coherence Guardian
- **Tools**: `json_tool`, `file_write`, `file_read`
- **Memory**: `SQLite` (Composite planned: ShortTerm + LongTerm world bible)
- **Key features**: Composite memory (planned), IKnowledgeSource (world bible), cross-validation consensus, batch execution
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/07-creative-media/81-worldbuilding/config.yaml
```

## What this example demonstrates

- Multi-dimensional worldbuilding with cross-validation consensus
- Geography-to-language coherence chain verification
- World bible compilation maintaining canonical consistency
