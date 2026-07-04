# 84. Critique Multi-Perspectives

> Trois agents incarnant des ecoles de pensee analysent independamment la meme oeuvre en parallele. L'independance est garantie par le batch execution (aucun agent ne voit le travail des autres).

## Quality

💪 Robustesse — Independance des analyses par parallelisme, synthese equilibree

## Architecture

- **Process**: `parallel`
- **Agents**: 4 — Structuralist Critic, Feminist Critic, Post-Colonial Critic, Comparative Synthesizer
- **Tools**: `file_read`, `web_scrape`, `json_tool`, `file_write`
- **Memory**: `InMemory`
- **Key features**: Batch execution (guaranteed independence), output validation (structured critique format), EvaluationInput
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/07-creative-media/84-multi-perspective-critique/config.yaml
```

## What this example demonstrates

- Guaranteed independence through parallel batch execution (no cross-contamination)
- Three distinct theoretical frameworks applied to the same work
- Balanced comparative synthesis identifying convergences and productive tensions
