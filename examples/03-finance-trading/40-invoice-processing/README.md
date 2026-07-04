# 40. Traitement Automatise de Factures

> Extraction et structuration de factures en formats varies. Les tolerant JSON converters gerent les variations automatiquement. Le pipeline type ComponentBase garantit la coherence bout-en-bout.

## Quality

:white_check_mark: Fiabilite -- Converters tolerants multi-format, pipeline type, validation de schema

## Architecture

- **Process**: `sequential`
- **Agents**: 3 -- Invoice Extractor, Data Structurer, Accounting Reconciler
- **Tools**: `pdf_reader`, `csv_reader`, `json_tool`, `database_query`, `file_write`
- **Memory**: `SQLite` (invoice history)
- **Key features**: ComponentBase typed pipeline, tolerant converters (BoolTolerant, IntTolerant), YAML attributes (FieldSchema, ReturnSchema), output validation
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/40-invoice-processing/config.yaml
```

## What this example demonstrates

- Multi-format invoice processing with tolerant JSON converters handling format variations
- Typed pipeline ensuring data consistency from extraction through reconciliation
- Three-way matching between invoices, purchase orders, and receiving records
