# 70. Documentation Technique Versionnee

> Les agents extraient et documentent la doc technique. Le IConfigurationVersioning versionne chaque revision. Le IConfigurationDiffService montre les changements entre versions.

## Quality

🎯 Simplicite — Doc versionnee comme du code, diff entre versions, extraction automatique

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Code Extractor, API Documenter, Architecture Diagrammer, Coherence Editor
- **Tools**: `file_read`, `directory_read`, `github`, `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: IConfigurationVersioning (planned), IConfigurationDiffService (planned), IDocumentLoader, ITextChunker
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/06-engineering-devops/70-versioned-documentation/config.yaml
```

## What this example demonstrates

- Automatic code-to-documentation extraction pipeline
- Documentation versioning with diff between revisions (planned feature)
- Coherence verification across all documentation sections
