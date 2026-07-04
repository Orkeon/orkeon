# 13. Audit de Securite Informatique

> Hierarchical process with a virtual CISO coordinating 4 specialists. Suspect code is analyzed in a secure sandbox. All file paths are validated by IPathValidator and HTTP headers sanitized.

## Quality

🔒 Securite — Sandbox execution, path/URL validation, header sanitization, rate limiting

## Architecture

- **Process**: `Hierarchical`
- **Agents**: 5 — CISO Virtuel (Manager), Scanner Vulnerabilites (Worker), Analyste Configuration (Worker), Auditeur Compliance (Worker), Rapporteur Securite (Worker)
- **Tools**: `http_api`, `file_read`, `directory_read`, `json_tool`, `file_write`
- **Memory**: `EncryptedRedis`
- **Key features**: `ICodeSandbox`, `ICodeSecurityAnalyzer`, `IPathValidator`, `IUrlValidator`, `PromptSecurityTypes` (anti-injection), `LlmRateLimiter`
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/13-security-audit/config.yaml
```

## What this example demonstrates

- Hierarchical security audit with CISO-level coordination
- Secure sandbox execution for analyzing suspicious code
- Multi-layer security: path validation, URL validation, header sanitization, rate limiting
