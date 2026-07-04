# 79. Composition Musicale par Consensus

> Agents par dimension musicale qui doivent s'harmoniser via consensus unanime. Le protocole Feedback permet les ajustements iteratifs.

## Quality

💪 Robustesse — Consensus unanime, iterations feedback, convergence harmonique

## Architecture

- **Process**: `consensual`
- **Agents**: 5 — Melodist, Harmonist, Rhythmician, Arranger, Mixer
- **Tools**: `json_tool`, `file_write`
- **Memory**: `InMemory`
- **Key features**: Consensus voting (unanimity), Communication Feedback protocol, iteration limit, output validation (music theory)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/07-creative-media/79-music-composition/config.yaml
```

## What this example demonstrates

- Unanimous consensus across 5 musical dimension specialists
- Iterative feedback protocol for creative convergence
- JSON-based music notation for structured composition output
