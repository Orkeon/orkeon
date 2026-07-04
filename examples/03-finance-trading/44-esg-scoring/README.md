# 44. Analyse ESG Scoring Reproductible

> Chaque dimension ESG est evaluee independamment par un agent specialise, puis consolidee via EvaluationSuite. Le scoring normalise est reproductible et benchmarkable.

## Quality

:muscle: Robustesse -- Evaluation reproductible et benchmarkable, scoring normalise

## Architecture

- **Process**: `parallel`
- **Agents**: 5 -- ESG Data Collector, Environmental Analyst, Social Analyst, Governance Analyst, Score Integrator
- **Tools**: `http_api`, `pdf_reader`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `SQLite` (historical scores)
- **Key features**: EvaluationSuite multi-criteria, EvaluationScore normalized, EvaluationInput structured, InMemoryDataset (benchmarks)
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/44-esg-scoring/config.yaml
```

## What this example demonstrates

- Independent parallel evaluation of Environmental, Social, and Governance dimensions
- Reproducible scoring methodology with sector-specific materiality weights
- Normalized composite ratings enabling cross-company and cross-sector comparison
