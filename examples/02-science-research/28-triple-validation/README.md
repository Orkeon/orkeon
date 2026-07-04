# 28. Validation Croisee par Triple Analyse Independante

> Three independent agents reproduce the same analysis with different methods (frequentist, Bayesian, ML). Consensus validates only if 2/3 converge. An Arbiter investigates divergences.

## Quality

💪 Robustesse — Triple independent validation, majority consensus, automatic arbitration

## Architecture

- **Process**: `Consensual`
- **Agents**: 4 — Analyste Methode A (Worker), Analyste Methode B (Worker), Analyste Methode C (Worker), Arbitre (Worker)
- **Tools**: `csv_reader`, `json_tool`, `file_write`
- **Memory**: `InMemory`
- **Key features**: Consensus voting (2/3 majority), `ICodeSandbox` (independent calculations), `EvaluationSuite` for comparison, `TaskPriority` (priority arbitration)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/28-triple-validation/config.yaml
```

## What this example demonstrates

- Triple independent analysis using different methodologies (frequentist, Bayesian, ML)
- Majority consensus validation requiring 2/3 method convergence
- Automated arbitration investigating root causes of methodological divergence
