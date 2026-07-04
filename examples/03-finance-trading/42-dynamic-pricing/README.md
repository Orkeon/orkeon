# 42. Pricing Dynamique Arbitrage Hierarchique

> Un Manager Agent arbitre entre trois perspectives conflictuelles sur le prix optimal. Les contraintes min/max empechent les prix aberrants. Le DelegationParameters encadre chaque delegation.

## Quality

:muscle: Robustesse -- Arbitrage hierarchique structure, garde-fous sur les prix, rate limiting

## Architecture

- **Process**: `hierarchical`
- **Agents**: 4 -- Pricing Arbitrator (manager), Demand Analyst, Competition Monitor, Margin Optimizer
- **Tools**: `web_scrape`, `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `Redis` (real-time pricing)
- **Key features**: Delegation with constraints, DelegationParameters, output validation (min/max price), LlmRateLimiter
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/42-dynamic-pricing/config.yaml
```

## What this example demonstrates

- Hierarchical arbitration between conflicting pricing perspectives
- Real-time competitive monitoring with web scraping and rate limiting
- Margin optimization with price guardrails preventing aberrant pricing
