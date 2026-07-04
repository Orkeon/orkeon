# 23. Analyse Genomique avec Chunking

> Three bioinformatics agents process long genomic sequences. ITextChunker splits FASTA files to respect context window limits. All data is encrypted at rest.

## Quality

🔒 Securite — Encrypted data, intelligent chunking, managed context window

## Architecture

- **Process**: `Hierarchical`
- **Agents**: 4 — Bio-informaticien Lead (Manager), Sequenceur (Worker), Annotateur Variants (Worker), Interpreteur Clinique (Worker)
- **Tools**: `file_read`, `http_api`, `json_tool`, `csv_reader`, `file_write`
- **Memory**: `EncryptedSQLite`
- **Key features**: `EncryptedSqliteMemoryProvider`, `ITextChunker` (long sequences), `IDocumentLoader` (FASTA), `IContextWindowManager`
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/23-genomic-analysis/config.yaml
```

## What this example demonstrates

- Text chunking for long genomic sequences (FASTA) respecting LLM context windows
- Encrypted storage for sensitive genomic and clinical data
- ACMG-guided clinical variant interpretation pipeline
