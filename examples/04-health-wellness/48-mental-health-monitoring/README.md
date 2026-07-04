# 48. Monitoring Bien-etre Mental

> Donnees ultra-sensibles protegees par trois couches: chiffrement au repos, validation humaine systematique, prevention d'injection de prompts. L'ObserverAgent declenche les alertes.

## Quality

:lock: Securite -- Triple securite: chiffrement + validation humaine + anti-injection

## Architecture

- **Process**: `sequential`
- **Agents**: 4 -- Well-being Observer, Trend Analyst, Exercise Coach, Supervising Psychologist (human at every step)
- **Tools**: `json_tool`, `file_write`
- **Memory**: `EncryptedSQLite`
- **Key features**: EncryptedSqliteMemoryProvider, HumanInputContext systematic, PromptSecurityTypes, TaskCallbacks (threshold alerts), ObserverAgent
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/04-health-wellness/48-mental-health-monitoring/config.yaml
```

## What this example demonstrates

- Triple security layer for ultra-sensitive mental health data
- Systematic human validation by licensed psychologist at every step
- Longitudinal trend analysis with early warning pattern detection
