# 57. Generation d'Examens Calibres par Benchmark

> Le BenchmarkRunner calibre la difficulte des questions. Le consensus garantit la couverture taxonomique Bloom. Le LlmJudgeEvaluator evalue la clarte de chaque question.

## Quality

💪 Robustesse — Consensus, calibration benchmark, couverture taxonomique garantie

## Architecture

- **Process**: `consensual`
- **Agents**: 4 — Question Designer, Bloom Taxonomist, Psychometrician, Clarity Reviewer
- **Tools**: `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: Consensus voting, BenchmarkRunner (calibration), LlmJudgeEvaluator (clarity), IKnowledgeSource (Bloom taxonomy), InMemoryDataset
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/05-education/57-exam-generation/config.yaml
```

## What this example demonstrates

- Consensual process ensuring multi-perspective validation of exam questions
- Bloom's taxonomy coverage verification and gap detection
- Difficulty calibration using benchmark data and psychometric models
