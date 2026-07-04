# 21. Peer Review Simule avec Calibration

> Before submission, 3 independent reviewer-agents evaluate the article. BenchmarkRunner calibrates severity. The consensual process identifies unanimous critical points.

## Quality

💪 Robustesse — Consensus voting, benchmark calibration, multi-criteria feedback

## Architecture

- **Process**: `Consensual`
- **Agents**: 4 — Reviewer Methodologie (Worker), Reviewer Statistiques (Worker), Reviewer Pertinence (Worker), Editeur-Synthetiseur (Worker)
- **Tools**: `pdf_reader`, `file_read`, `json_tool`, `file_write`
- **Memory**: `InMemory`
- **Key features**: `EvaluationSuite` + `LlmJudgeEvaluator` (structured scoring), `BenchmarkRunner` (severity calibration), consensus voting, `InMemoryDataset` (reference articles)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/21-peer-review-calibration/config.yaml
```

## What this example demonstrates

- Simulated peer review with independent multi-criteria evaluation
- Benchmark-based calibration ensuring consistent reviewer severity
- Consensus-driven editorial decision with structured revision requests
