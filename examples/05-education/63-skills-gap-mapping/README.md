# 63. Cartographie Lacunes Competences

> Un agent par membre de l'equipe en parallele, puis consolidation en carte de competences collective. Le matching semantique identifie les formations prioritaires par gap analysis.

## Quality

💪 Robustesse — Matching semantique competences/formations, parallelisme par individu

## Architecture

- **Process**: `parallel`
- **Agents**: 3 — Skills Assessor, Team Map Consolidator, Training Recommender
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `Redis`
- **Key features**: Vector search (skill-to-training matching), AgentSkill (A2A) for competency modeling, batch execution, semantic selection
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/05-education/63-skills-gap-mapping/config.yaml
```

## What this example demonstrates

- Parallel per-member competency assessment with team-level consolidation
- Semantic matching between competency gaps and training catalog
- Gap prioritization by criticality, team impact, and training ROI
