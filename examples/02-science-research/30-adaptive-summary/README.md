# 30. Resume Adaptatif au Profil Lecteur

> A single agent whose behavior adapts to the reader's profile stored in long-term memory. Expert gets a concise technical summary. Novice gets a pedagogical summary. Memory learns preferences over interactions.

## Quality

🎯 Simplicite — Single agent, contextual intelligence, adaptation without explicit configuration

## Architecture

- **Process**: `Sequential`
- **Agents**: 1 — Resumeur Adaptatif (Worker)
- **Tools**: `pdf_reader`, `file_write`
- **Memory**: `SQLite`
- **Key features**: `AgentMemory.LongTerm` (reader profile), `ITextChunker` (long documents), `IContextWindowManager`, `AgentConfiguration` dynamic
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/30-adaptive-summary/config.yaml
```

## What this example demonstrates

- Single-agent adaptive behavior driven by reader profile in long-term memory
- Progressive preference learning from interaction history
- Dynamic summarization style switching (technical vs pedagogical) without reconfiguration
