# 17. Simulation de Debat Scientifique

> Three agents embody opposing scientific positions, supervised by a Moderator Manager. A structured communication protocol prevents loops and forces convergence toward a balanced synthesis with citations.

## Quality

💪 Robustesse — Structured anti-loop protocol, guaranteed convergence via iteration limit

## Architecture

- **Process**: `Consensual`
- **Agents**: 4 — Moderateur (Manager), Defenseur Position A (Worker), Defenseur Position B (Worker), Voix Moderee (Worker)
- **Tools**: `web_scrape`, `http_api`, `json_tool`
- **Memory**: `InMemory`
- **Key features**: Communication `Consensus` protocol, configurable iteration limit, `IContextWindowManager`
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/17-scientific-debate/config.yaml
```

## What this example demonstrates

- Consensual process with manager-moderated scientific debate
- Anti-loop protocol ensuring debates converge to actionable synthesis
- Multi-perspective evidence evaluation with balanced synthesis output
