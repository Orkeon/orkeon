# 1. Assistant de Recherche Multi-Sources

> A crew of 3 agents collaborates sequentially to produce a structured synthesis report from heterogeneous sources (web, PDF, CSV, JSON) with proper citations.

## Quality

🎯 Simplicite — 3 agents, a linear pipeline, a structured result

## Architecture

- **Process**: `Sequential`
- **Agents**: 3 — Chercheur Web (Worker), Analyste Documentaire (Worker), Redacteur de Synthese (Worker)
- **Tools**: `web_scrape`, `pdf_reader`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `InMemory`
- **Key features**: Task dependencies (each task depends on the previous), output validation JSON schema
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/01-research-assistant/config.yaml
```

## What this example demonstrates

- Sequential task pipeline where each agent builds on the previous agent's output
- Multi-format document analysis (PDF, CSV, JSON) combined with web scraping
- Structured synthesis report generation with citations and bibliography
