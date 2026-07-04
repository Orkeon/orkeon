# 3. Pipeline de Traitement d'Emails

> Sequential crew that triages, categorizes, and drafts responses to incoming emails. A Human Agent validates before any sending occurs.

## Quality

🔒 Securite — Mandatory human validation before sending, no autonomous action

## Architecture

- **Process**: `Sequential`
- **Agents**: 4 — Trieur d'Emails (Worker), Extracteur d'Actions (Worker), Redacteur de Reponses (Worker), Validateur Humain (Human)
- **Tools**: `email_parser`, `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: `HumanInputContext` (type: confirmation), `TaskCallbacks` for notification, `TaskPriority` for triage
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/03-email-pipeline/config.yaml
```

## What this example demonstrates

- Human-in-the-loop validation ensuring no emails are sent without approval
- Email parsing with urgency-based prioritization using TaskPriority
- Sequential pipeline with clear separation of triage, extraction, and drafting
