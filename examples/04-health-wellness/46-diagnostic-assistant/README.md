# 46. Aide au Diagnostic -- Humain Systematique

> Validation humaine a chaque etape. Aucun diagnostic n'est jamais pose de maniere autonome. Donnees chiffrees de bout en bout. Anti-injection de prompts pour empecher les manipulations.

## Quality

:lock: Securite -- Humain dans la boucle systematique, zero autonomie diagnostique, chiffrement total

## Architecture

- **Process**: `sequential`
- **Agents**: 4 -- Symptom Analyst, Differential Diagnosis Generator, Drug Interaction Checker, Attending Physician (human at every step)
- **Tools**: `http_api`, `json_tool`, `pdf_reader`
- **Memory**: `EncryptedSQLite`
- **Key features**: HumanInputContext at every Task, EncryptedSqliteMemoryProvider, AuditEventTypes, PromptSecurityTypes, delegation disabled
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/04-health-wellness/46-diagnostic-assistant/config.yaml
```

## What this example demonstrates

- Mandatory human validation at every single processing step
- Zero autonomous diagnostic capability -- all outputs are advisory only
- Encrypted data storage with prompt injection protection for medical data
