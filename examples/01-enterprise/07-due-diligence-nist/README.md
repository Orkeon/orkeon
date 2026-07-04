# 7. Due Diligence avec Audit NIST

> Hierarchical process led by a Chief Analyst coordinating 4 specialists (legal, financial, reputation, compliance). Each finding is severity-classified with full NIST audit trail for regulatory traceability.

## Quality

🔒 Securite — Complete audit trail, NIST compliance, encrypted data at rest

## Architecture

- **Process**: `Hierarchical`
- **Agents**: 5 — Chief Analyst (Manager), Analyste Juridique (Worker), Analyste Financier (Worker), Analyste Reputation (Worker), Analyste Conformite (Worker)
- **Tools**: `pdf_reader`, `csv_reader`, `web_scrape`, `http_api`, `json_tool`
- **Memory**: `EncryptedSQLite`
- **Key features**: `INistComplianceReporter`, `AuditEventTypes` + builders, `LlmCallAudit`, `EvaluationScore` for scoring findings
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/07-due-diligence-nist/config.yaml
```

## What this example demonstrates

- Hierarchical due diligence process with specialist delegation
- NIST-compliant audit trail ensuring full traceability of every finding
- Encrypted data storage for confidential M&A information
