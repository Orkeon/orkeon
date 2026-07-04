# 50. Imagerie Medicale -- Radiologue Dernier Mot

> Le rapport est genere automatiquement mais jamais finalise sans validation du Radiologue humain. La memoire long-terme compare avec l'historique patient pour detecter les evolutions.

## Quality

:lock: Securite -- HumanAgent obligatoire en fin de chaine, jamais de rapport auto-valide

## Architecture

- **Process**: `sequential`
- **Agents**: 5 -- Image Preprocessor, ROI Analyst, Historical Comparator, Report Drafter, Supervising Radiologist (human)
- **Tools**: `file_read`, `json_tool`, `http_api`, `file_write`
- **Memory**: `EncryptedSQLite` (patient imaging history)
- **Key features**: HumanInputContext (mandatory final validation), AgentMemory.LongTerm (imaging history), EncryptedSqliteMemoryProvider
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/04-health-wellness/50-radiology-assistant/config.yaml
```

## What this example demonstrates

- Mandatory human radiologist sign-off preventing any auto-validated reports
- Longitudinal comparison with encrypted patient imaging history
- Structured reporting following ACR standards with preliminary/final workflow
