# 16. Meta-Analyse Scientifique PRISMA

> Systematic literature review with full traceability of inclusion/exclusion decisions following the PRISMA protocol. Episodic memory journals every decision with its reasoning.

## Quality

✅ Fiabilite — Complete traceability, every inclusion/exclusion justified and replayable

## Architecture

- **Process**: `Sequential`
- **Agents**: 4 — Bibliographe (Worker), Statisticien (Worker), Methodologiste (Worker), Synthetiseur PRISMA (Worker)
- **Tools**: `http_api`, `pdf_reader`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: `AgentMemory.Episodic` for traceability, `EvaluationSuite` for study quality scoring, output validation JSON (PRISMA format)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/16-prisma-meta-analysis/config.yaml
```

## What this example demonstrates

- PRISMA-compliant systematic review with structured inclusion/exclusion tracking
- Episodic memory journaling every screening decision for full auditability
- Statistical meta-analysis with heterogeneity assessment and publication bias detection
