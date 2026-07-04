# 45. Stress Testing Reglementaire Rejouable

> Scenarios de stress versionnes et rejouables. Le IConfigurationVersioning permet de rejouer n'importe quel scenario passe a l'identique. Conformite NIST complete. (Features planifiees: IConfigurationVersioning, IConfigurationRollbackService)

## Quality

:lock: Securite -- Scenarios versionnes, conformite NIST, audit trail, chiffrement

## Architecture

- **Process**: `sequential`
- **Agents**: 4 -- Scenario Designer, Stress Test Modeler, Regulatory Reporter, Compliance Agent
- **Tools**: `csv_reader`, `http_api`, `json_tool`, `database_query`, `file_write`
- **Memory**: `EncryptedSQLite`
- **Key features**: INistComplianceReporter, IConfigurationVersioning (planned), IConfigurationRollbackService (planned), AuditEventTypes, output validation
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/45-stress-testing/config.yaml
```

## What this example demonstrates

- Versioned and replayable regulatory stress test scenarios
- Sequential pipeline from scenario design through modeling, reporting, and NIST audit
- Encrypted data storage with complete audit trail for regulatory compliance
