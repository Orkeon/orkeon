# 35. Reconciliation Comptable Multi-Sources

> Un agent par source de donnees en parallele (banque, ERP, CRM), puis convergence vers un Reconciliateur. Le checkpointing permet de reprendre apres un timeout API sans recommencer.

## Quality

:white_check_mark: Fiabilite -- Batch parallele, checkpointing, converters tolerants pour formats heterogenes

## Architecture

- **Process**: `parallel`
- **Agents**: 5 -- Bank Extractor, ERP Extractor, CRM Extractor, Reconciliation Analyst, Discrepancy Corrector
- **Tools**: `http_api`, `csv_reader`, `database_query`, `json_tool`, `file_write`
- **Memory**: `Redis` (shared intermediate state)
- **Key features**: Batch tool execution, ICheckpointManager, tolerant JSON converters, output validation JSON schema
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/35-accounting-reconciliation/config.yaml
```

## What this example demonstrates

- Parallel extraction from three independent data sources (bank, ERP, CRM)
- Three-way matching reconciliation with fuzzy matching on amounts and dates
- Checkpoint-based recovery allowing resume after API timeouts
