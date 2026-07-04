# 34. Optimisation Portefeuille par Consensus

> Trois analystes avec des philosophies d'investissement differentes doivent s'accorder. La strategie de vote est ponderee par le track record historique de chaque analyste, stocke en memoire long-terme.

## Quality

:muscle: Robustesse -- Consensus pondere par performance passee, multi-perspectives, contraintes risk

## Architecture

- **Process**: `consensual`
- **Agents**: 4 -- Fundamental Analyst, Technical Analyst, Macro Strategist, Risk Manager Validator
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `SQLite` (track records for vote weighting)
- **Key features**: Weighted consensus voting, AgentMemory.LongTerm (historical performance), EvaluationScore
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/34-portfolio-consensus/config.yaml
```

## What this example demonstrates

- Consensual process with weighted voting based on historical analyst track records
- Multiple investment philosophies (fundamental, technical, macro) reaching consensus
- Risk validation as final gate ensuring allocation respects constraints
