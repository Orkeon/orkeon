# 69. Analyse de Performance 4 Couches

> Quatre analystes paralleles (backend, frontend, reseau, BDD) alimentes par metriques OpenTelemetry, puis synthese priorisee des bottlenecks.

## Quality

💪 Robustesse — Parallelisme 4 couches, metriques OpenTelemetry reelles, synthese priorisee

## Architecture

- **Process**: `parallel`
- **Agents**: 5 — Backend Profiler, Frontend Analyzer, Network Monitor, Database Analyst, Optimization Synthesizer
- **Tools**: `http_api`, `database_query`, `json_tool`, `file_write`
- **Memory**: `Redis`
- **Key features**: OpenTelemetry integration, LlmProviderHealthCheck, batch execution, EvaluationScore (bottleneck scoring), cost tracking
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/06-engineering-devops/69-performance-analysis/config.yaml
```

## What this example demonstrates

- Four-layer parallel performance analysis with independent profiling
- Cross-layer correlation identifying cascading performance issues
- Prioritized optimization roadmap based on user impact scoring
