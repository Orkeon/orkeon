# 54. Coach Sportif Adaptatif par Episodique

> La memoire episodique trace la progression de l'athlete seance par seance. Les plans s'adaptent automatiquement aux performances mesurees. L'EvaluationScore suit les progres.

## Quality

:dart: Simplicite -- Agents specialises, adaptation automatique par episodique, flow intuitif

## Architecture

- **Process**: `sequential`
- **Agents**: 4 -- Training Program Planner, Performance Analyst, Sports Nutritionist, Recovery Coach
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `SQLite` (episodic athlete history)
- **Key features**: AgentMemory.Episodic, AgentConfiguration adaptive, TaskContext typed, EvaluationScore (performance tracking)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/04-health-wellness/54-sports-coach/config.yaml
```

## What this example demonstrates

- Episodic memory tracking athlete progression session by session across training cycles
- Automatic training plan adaptation based on measured performance trends
- Integrated coaching covering training, nutrition, and recovery optimization
