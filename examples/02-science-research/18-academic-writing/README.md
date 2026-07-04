# 18. Assistant de Redaction Academique

> 4-stage pipeline reproducing the academic writing workflow. The Citation Verifier automatically checks references via bibliographic APIs (CrossRef, Semantic Scholar). Each agent produces a typed deliverable feeding the next.

## Quality

🎯 Simplicite — Single responsibility per agent, perfectly linear pipeline

## Architecture

- **Process**: `Sequential`
- **Agents**: 4 — Planificateur Structure (Worker), Redacteur Academique (Worker), Verificateur de References (Worker), Editeur de Style (Worker)
- **Tools**: `http_api`, `file_read`, `file_write`, `json_tool`
- **Memory**: `InMemory`
- **Key features**: Strict task dependencies, `ComponentBase<TReq, TRes>` for typed steps, output validation academic format
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/18-academic-writing/config.yaml
```

## What this example demonstrates

- Typed sequential pipeline where each agent produces a deliverable for the next
- Automatic citation verification against bibliographic APIs (CrossRef, Semantic Scholar)
- Academic style enforcement with structure planning and style editing stages
