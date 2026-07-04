# 31. Trading Algorithmique Multi-Strategies

> Showcase complete du framework: 8 agents, 40+ outils specialises, processus hierarchique complet. Le CIO coordonne analyse, risque, execution et compliance en temps reel.

## Quality

:muscle: Robustesse -- Showcase maximale: 40+ outils types, validation a chaque etape

## Architecture

- **Process**: `hierarchical`
- **Agents**: 8 -- CIO (manager), Data Orchestrator, Technical Analyst, Quant Forecaster, Risk Officer, Portfolio Manager, Trader, Compliance Officer
- **Tools**: `http_api`, `json_tool`, `csv_reader`, `code_interpreter`, `database_query`, `file_write`
- **Memory**: `Redis` (real-time) + `SQLite` (historical)
- **Key features**: Full delegation with DelegationEvents, CrewHooks, TaskCallbacks, LlmRateLimiter, EvaluationSuite, streaming
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/31-algo-trading/config.yaml
```

## What this example demonstrates

- Hierarchical process with a manager agent coordinating 7 specialized workers
- Complete trading pipeline from data collection through execution and compliance
- Risk management integration with position limits and VaR monitoring
