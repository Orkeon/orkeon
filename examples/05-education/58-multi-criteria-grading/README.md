# 58. Correction Multi-Criteres Reproductible

> Trois correcteurs independants en parallele, plus un LlmJudgeEvaluator pour coherence inter-correcteurs. L'EvaluationSuite garantit reproductibilite et equite.

## Quality

✅ Fiabilite — Evaluation reproductible, coherence verifiable, scoring normalise

## Architecture

- **Process**: `parallel`
- **Agents**: 4 — Content Grader, Form Grader, Methodology Grader, Grade Consolidator
- **Tools**: `file_read`, `json_tool`, `file_write`
- **Memory**: `InMemory`
- **Key features**: EvaluationSuite multi-criteria, LlmJudgeEvaluator (inter-grader consistency), EvaluationScore normalized, InMemoryDataset (calibration copies)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/05-education/58-multi-criteria-grading/config.yaml
```

## What this example demonstrates

- Parallel independent grading eliminating single-grader bias
- Inter-grader consistency verification and score normalization
- Multi-criteria evaluation with structured feedback generation
