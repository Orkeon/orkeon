# 85. Adaptation Cross-Media Pipeline Type

> Extraction des elements narratifs d'un media source puis transformation vers un media cible via le pipeline type ComponentBase<TReq, TRes>. Chaque transformation est contractualisee.

## Quality

🎯 Simplicite — Transformation structuree entre formats, pipeline type bout-en-bout

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Source Analyzer, Key Element Extractor, Target Media Adapter, Format Specialist
- **Tools**: `file_read`, `pdf_reader`, `json_tool`, `file_write`
- **Memory**: `InMemory`
- **Key features**: ComponentBase<TReq, TRes> (typed transformation), IDocumentLoader (multi-format), ITextChunker, output validation format cible
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/07-creative-media/85-cross-media-adaptation/config.yaml
```

## What this example demonstrates

- Typed transformation pipeline from source medium to target medium
- Structured element extraction separating universal from medium-specific content
- Professional format output compliant with target medium industry standards
