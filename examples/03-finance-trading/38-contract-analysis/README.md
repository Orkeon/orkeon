# 38. Analyse de Contrats avec Chunking

> Deux agents analysent un contrat en parallele (juridique et financier), puis un Negociateur propose des alternatives. Le ITextChunker decoupe les contrats volumineux pour respecter la context window.

## Quality

:dart: Simplicite -- 3 agents, analyse parallele, chunking intelligent automatique

## Architecture

- **Process**: `parallel`
- **Agents**: 3 -- Legal Analyst, Financial Analyst, Negotiation Strategist
- **Tools**: `pdf_reader`, `json_tool`, `csv_reader`, `file_write`
- **Memory**: `InMemory`
- **Key features**: ITextChunker (large contracts), IContextWindowManager, batch execution (parallel), output validation format clauses
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/38-contract-analysis/config.yaml
```

## What this example demonstrates

- Parallel legal and financial analysis of the same contract document
- Intelligent chunking for large documents exceeding context window limits
- Negotiation strategy synthesis from multi-perspective analysis
