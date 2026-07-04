# 25. Assistant de Laboratoire avec Reprise d'Experiences

> Agents connected to IoT instruments. ICheckpointManager saves experiment state at each step. On interruption (power failure, sensor timeout), IResumeEngine resumes exactly where the experiment stopped.

## Quality

✅ Fiabilite — Granular checkpointing, exact resumption, complete lab history

## Architecture

- **Process**: `Sequential`
- **Agents**: 4 — Planificateur Experiences (Worker), Moniteur IoT (Worker), Enregistreur Resultats (Worker), Comparateur Litterature (Worker)
- **Tools**: `http_api`, `csv_reader`, `web_scrape`, `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: `ICheckpointManager` + `IResumeEngine`, `AgentMemory.Episodic`, `TaskCallbacks` (instrument alerts)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/25-lab-assistant-resume/config.yaml
```

## What this example demonstrates

- Checkpoint-based experiment recovery enabling exact resumption after interruptions
- IoT instrument monitoring with real-time anomaly detection
- Literature comparison for automatic contextualization of experimental results
