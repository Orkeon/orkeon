# 64. Accessibilite Universelle des Contenus

> Quatre agents transforment en parallele un contenu en 4 formats accessibles simultanement. Le batch execution maximise la vitesse. Chaque format est valide independamment.

## Quality

💪 Robustesse — Parallelisme pur 4 formats simultanes, validation independante par format

## Architecture

- **Process**: `parallel`
- **Agents**: 4 — Text Transcriber, Language Simplifier, Audio Describer, Screen Reader Formatter
- **Tools**: `file_read`, `file_write`, `json_tool`, `xml_parser`
- **Memory**: `InMemory`
- **Key features**: Batch execution parallel, output validation per format, IDocumentLoader (varied formats), ITextChunker
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/05-education/64-accessibility/config.yaml
```

## What this example demonstrates

- Pure parallel processing of 4 accessibility formats simultaneously
- Independent validation of each accessible format output
- WCAG 2.1 AA compliance verification for screen reader output
