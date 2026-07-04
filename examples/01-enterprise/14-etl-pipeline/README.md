# 14. Pipeline ETL Intelligent

> Intelligent extraction-transformation-loading pipeline processing 7 different formats with typed tools. Each step is idempotent with checkpointing. Tolerant JSON converters handle format variations automatically.

## Quality

✅ Fiabilite — 7 typed tools, idempotent steps, failure recovery, tolerant converters

## Architecture

- **Process**: `Sequential`
- **Agents**: 4 — Parseur Multi-Format (Worker), Nettoyeur de Donnees (Worker), Enrichisseur API (Worker), Chargeur Base de Donnees (Worker)
- **Tools**: `pdf_reader`, `csv_reader`, `xml_parser`, `json_tool`, `http_api`, `database_query`, `file_write`
- **Memory**: `Redis`
- **Key features**: `ICheckpointManager` + `IResumeEngine`, `ComponentBase<TReq, TRes>` typed pipeline, tolerant JSON converters (BoolTolerant, IntTolerant, etc.)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/14-etl-pipeline/config.yaml
```

## What this example demonstrates

- Multi-format data extraction (PDF, CSV, XML, JSON) into unified schema
- Idempotent ETL steps with checkpoint-based recovery on failure
- Tolerant JSON converters handling format variations (booleans, numbers, locales)
