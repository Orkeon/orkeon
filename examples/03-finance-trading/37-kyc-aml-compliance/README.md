# 37. Conformite KYC/AML Triple Securite

> Chaque etape est auditee NIST, chaque donnee chiffree, chaque prompt protege contre l'injection. Le process hierarchique strict est pilote par un Compliance Manager.

## Quality

:lock: Securite -- Triple securite: chiffrement + audit NIST + anti-injection de prompts

## Architecture

- **Process**: `hierarchical`
- **Agents**: 4 -- Compliance Manager (manager), Identity Verifier, Sanctions Screener, Country Risk Analyst
- **Tools**: `http_api`, `pdf_reader`, `json_tool`
- **Memory**: `EncryptedRedis`
- **Key features**: EncryptedRedisMemoryProvider, INistComplianceReporter, AuditEventTypes, LlmCallAudit, PromptSecurityTypes (anti-injection)
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/37-kyc-aml-compliance/config.yaml
```

## What this example demonstrates

- Hierarchical compliance workflow with strict manager oversight
- Triple security layer: encrypted memory, NIST audit trail, and prompt injection protection
- Sanctions screening against multiple global watchlists (OFAC, EU, UN, PEP)
