# 77. Production de Podcast Pipeline Type

> Pipeline sequentiel ou chaque etape produit un livrable avec contrat type ToolBase<TReq, TRes>. Le contrat d'interface entre etapes est garanti par le framework.

## Quality

🎯 Simplicite — Pipeline parfaitement lineaire, contrats types, chaque etape = un livrable

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Source Researcher, Scriptwriter, Sound Design Brief Writer, Final Editor
- **Tools**: `web_scrape`, `http_api`, `json_tool`, `file_read`, `file_write`
- **Memory**: `InMemory`
- **Key features**: ToolBase<TReq, TRes> (typed contracts), Task dependencies, output validation per stage, TaskCallbacks
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/07-creative-media/77-podcast-production/config.yaml
```

## What this example demonstrates

- Typed pipeline where each stage produces a contractually defined deliverable
- Sequential production workflow mirroring real podcast creation
- Output validation ensuring each stage meets its contract before handoff
