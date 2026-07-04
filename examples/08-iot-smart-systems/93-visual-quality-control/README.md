# 93. Controle Qualite Visuel — Auto-Calibration par Historique

> Long-term memory enables the system to auto-calibrate acceptance thresholds. Thresholds refine with experience. The BenchmarkRunner ensures inspection reproducibility.

## Quality

✅ Fiabilite — Auto-calibration from inspection history, reproducible benchmarks

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Image Capturer, Parts Inspector, Defect Classifier, SPC Statistician
- **Tools**: `http_api`, `json_tool`, `csv_reader`, `file_write`
- **Memory**: `SQLite` (inspection history + calibration data)
- **Key features**: AgentMemory.LongTerm (auto-calibration), EvaluationSuite, BenchmarkRunner (reproducibility), EvaluationScore
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/08-iot-smart-systems/93-visual-quality-control/config.yaml
```

## What this example demonstrates

- Auto-calibrating inspection thresholds from long-term inspection history
- Statistical Process Control (SPC) with control charts and capability indices
- Reproducible quality benchmarks with BenchmarkRunner for consistent inspections
