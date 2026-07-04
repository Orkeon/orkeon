# 22. Monitoring d'Essais Cliniques Chiffre

> Continuous monitoring of sensitive patient data. Observer Agent detects alarm signals in real time. All data is encrypted, all actions audited per NIST standards.

## Quality

🔒 Securite — NIST audit, encrypted memory, real-time Observer Agent

## Architecture

- **Process**: `Parallel`
- **Agents**: 4 — Observateur Monitoring Continu (Observer), Analyste Safety (Worker), Statisticien Essais (Worker), Compliance Officer (Worker)
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `EncryptedSQLite`
- **Key features**: `INistComplianceReporter`, `AuditEventTypes`, `LlmCallAudit`, `ObserverAgent` alerts via `TaskCallbacks`, `EncryptedSqliteMemoryProvider`
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/02-science-research/22-clinical-trials-encrypted/config.yaml
```

## What this example demonstrates

- Encrypted memory for sensitive patient data in clinical trials
- Real-time Observer Agent monitoring for safety signal detection
- NIST-compliant audit trail for full regulatory traceability
