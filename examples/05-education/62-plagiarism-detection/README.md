# 62. Detection de Plagiat Multi-Couches

> Quatre angles d'analyse en parallele (style, sources, structure, semantique). L'analyse multi-couches elimine les faux positifs qu'un seul angle aurait produits. Scoring de confiance.

## Quality

✅ Fiabilite — Multi-couches anti-faux-positifs, scoring de confiance calibre

## Architecture

- **Process**: `parallel`
- **Agents**: 5 — Style Analyzer, Source Comparator, Structure Analyzer, Semantic Analyzer, Verdict Reporter
- **Tools**: `file_read`, `web_scrape`, `http_api`, `json_tool`, `file_write`
- **Memory**: `InMemory`
- **Key features**: Batch execution parallel, EvaluationSuite (multi-criteria scoring), EvaluationScore (confidence), output validation
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/05-education/62-plagiarism-detection/config.yaml
```

## What this example demonstrates

- Four independent parallel analysis layers for robust plagiarism detection
- Cross-referencing findings to eliminate false positives
- Calibrated confidence scoring requiring multi-layer corroboration
