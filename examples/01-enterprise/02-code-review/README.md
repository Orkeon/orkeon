# 2. Revue de Code Automatisee

> A Manager Agent distributes code to 4 specialized reviewers (security, performance, tests, style), aggregates findings into a consolidated report, and uses LlmJudgeEvaluator for calibration.

## Quality

💪 Robustesse — Strict scope per agent, schema-validated results, calibration scoring

## Architecture

- **Process**: `Hierarchical`
- **Agents**: 5 — Review Lead (Manager), Revieweur Securite (Worker), Revieweur Performance (Worker), Revieweur Tests (Worker), Revieweur Style (Worker)
- **Tools**: `file_read`, `directory_read`
- **Memory**: `InMemory`
- **Key features**: Delegation with `DelegationEvents`, `EvaluationSuite` + `LlmJudgeEvaluator` for review scoring, output validation JSON schema
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/02-code-review/config.yaml
```

## What this example demonstrates

- Hierarchical process with a Manager Agent coordinating specialized Worker Agents
- Parallel independent reviews consolidated into a single report
- Multi-dimensional code analysis (security, performance, tests, style)
