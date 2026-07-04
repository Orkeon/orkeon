# 11. Traduction et Localisation par Consensus

> Consensual process where 3 language experts must agree before validation. Translator produces initial version, Native Reviewer corrects cultural nuances, Terminologist verifies technical vocabulary against a persistent glossary.

## Quality

💪 Robustesse — Triple consensus validation, guaranteed quality, persistent glossary

## Architecture

- **Process**: `Consensual`
- **Agents**: 3 — Traducteur (Worker), Reviseur Natif (Worker), Terminologue (Worker)
- **Tools**: `file_read`, `file_write`, `json_tool`
- **Memory**: `SQLite`
- **Key features**: Consensus voting (unanimity required), `IKnowledgeSource` for glossaries, `ITextChunker` for long documents
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/11-translation-consensus/config.yaml
```

## What this example demonstrates

- Consensual process requiring unanimity from all expert agents
- Persistent terminology glossary as a knowledge source across translations
- Multi-perspective quality assurance (accuracy, cultural fit, terminology)
