# 78. Donnees Synthetiques Privacy-Safe

> L'Anonymiseur verifie la non-reidentification. Le PromptSecurityTypes empeche l'injection de donnees reelles dans les prompts. Aucune donnee n'est persistee.

## Quality

🔒 Securite — Privacy by design, validation anti-reidentification, zero persistance

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Schema Modeler, Data Generator, Distribution Validator, Anti-Reidentification Validator
- **Tools**: `csv_reader`, `json_tool`, `file_write`
- **Memory**: `InMemory` (zero persistence by design)
- **Key features**: PromptSecurityTypes, output validation (distributions), EvaluationSuite (realism + privacy), no StoreAsync
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/07-creative-media/78-synthetic-data/config.yaml
```

## What this example demonstrates

- Privacy-by-design synthetic data generation with zero persistence
- Anti-reidentification validation (k-anonymity, l-diversity, t-closeness)
- Statistical fidelity verification between source model and synthetic output
