# 33. Scoring de Credit Validation Humaine

> Pipeline sequentiel avec validation humaine declenchee conditionnellement: uniquement si le score depasse un seuil de risque. Le LlmJudgeEvaluator assure la coherence des evaluations.

## Quality

:lock: Securite -- Validation humaine conditionnelle, conformite NIST, chiffrement

## Architecture

- **Process**: `sequential`
- **Agents**: 5 -- Credit Data Collector, Credit Scorer, Risk Analyst, Recommender, Human Decider (conditional)
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `database_query`, `file_write`
- **Memory**: `EncryptedSQLite`
- **Key features**: HumanInputContext (conditional: if score > threshold), INistComplianceReporter, LlmJudgeEvaluator, AuditEventTypes
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/33-credit-scoring/config.yaml
```

## What this example demonstrates

- Sequential credit evaluation pipeline with conditional human-in-the-loop
- Multiple scoring models with confidence intervals and risk factor analysis
- NIST-compliant audit trail with encrypted data storage
