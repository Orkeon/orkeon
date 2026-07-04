# 15. Redaction de Propositions Commerciales

> Sequential proposal writing guided by YAML templates with human validation. Each section is produced by a specialized agent and validated by JSON schema before passing to the next. Human Agent approves the final version.

## Quality

🔒 Securite — Mandatory human approval, no automatic submission, auditable history

## Architecture

- **Process**: `Sequential`
- **Agents**: 5 — Analyste Besoins (Worker), Architecte Solution (Worker), Redacteur Commercial (Worker), Reviewer de Proposition (Worker), Approbateur Final (Human)
- **Tools**: `pdf_reader`, `http_api`, `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: `HumanInputContext` (type: confirmation), YAML templates, output validation JSON schema per section, `TaskPriority`
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/15-commercial-proposals/config.yaml
```

## What this example demonstrates

- Human-in-the-loop approval workflow preventing unauthorized proposal submissions
- Section-by-section JSON schema validation ensuring proposal consistency
- CRM integration for client context enrichment in proposal writing
