# 71. Audit Cloud Multi-Piliers NIST

> Agents par pilier cloud en parallele avec sanitization des headers HTTP et conformite NIST. Le rapport couvre compute, network, storage, IAM et couts.

## Quality

🔒 Securite — Conformite NIST, sanitization headers, rate limiting APIs cloud

## Architecture

- **Process**: `parallel`
- **Agents**: 6 — Compute Auditor, Network Auditor, Storage Auditor, IAM Auditor, Cost Auditor, Audit Reporter
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `EncryptedRedis`
- **Key features**: INistComplianceReporter, header sanitization (HttpApiTool), IUrlValidator, AuditEventTypes, batch execution, LlmRateLimiter
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/06-engineering-devops/71-cloud-audit-nist/config.yaml
```

## What this example demonstrates

- Five-pillar parallel cloud audit covering all infrastructure aspects
- NIST control mapping and compliance reporting
- HTTP header sanitization and rate limiting for cloud API calls
