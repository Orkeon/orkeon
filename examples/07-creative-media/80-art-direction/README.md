# 80. Direction Artistique Delegation Report

> Processus hierarchique creatif avec un DA Manager. Les DelegationPerformanceReport mesurent l'efficacite de chaque creatif. Les DelegationEvents trackent chaque delegation.

## Quality

💪 Robustesse — Hierarchie creative, mesure performance par agent, arbitrage DA

## Architecture

- **Process**: `hierarchical`
- **Agents**: 5 — Art Director (manager), Brief Strategist, Visual Designer, Copywriter, Media Planner
- **Tools**: `web_scrape`, `http_api`, `json_tool`, `file_write`
- **Memory**: `InMemory`
- **Key features**: DelegationParameters, DelegationPerformanceReport, DelegationEvents, ManagerAgent delegation capabilities
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/07-creative-media/80-art-direction/config.yaml
```

## What this example demonstrates

- Hierarchical creative process with art director managing the team
- Delegation performance tracking and reporting per creative agent
- Full campaign workflow from brief to media planning
