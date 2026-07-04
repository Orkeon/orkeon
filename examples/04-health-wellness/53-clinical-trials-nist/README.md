# 53. Essais Cliniques Checkpoint + Audit NIST

> Architecture hierarchique stricte avec le PI comme Manager. Donnees chiffrees, actions auditees, checkpointing pour reprise. Le Data Safety Board intervient via HumanAgent.

## Quality

:lock: Securite -- Audit NIST, checkpointing, chiffrement, validation humaine safety

## Architecture

- **Process**: `hierarchical`
- **Agents**: 6 -- Principal Investigator (manager), Patient Recruiter, Protocol Manager, Data Monitor, Safety Monitor, Data Safety Board (human)
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `EncryptedSQLite`
- **Key features**: INistComplianceReporter, ICheckpointManager, AuditEventTypes, EncryptedSqliteMemoryProvider, HumanInputContext (safety decisions)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/04-health-wellness/53-clinical-trials-nist/config.yaml
```

## What this example demonstrates

- Strict hierarchical trial management with PI as coordinating manager
- NIST-compliant audit trail with encrypted patient data and checkpoint recovery
- Human Data Safety Monitoring Board for critical safety decisions
