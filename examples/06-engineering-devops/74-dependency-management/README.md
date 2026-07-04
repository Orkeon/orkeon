# 74. Gestion des Dependances CVE

> Scanner, Analyse CVE, Test compatibilite, Upgrade. Les CVE critiques sont traitees en priorite via TaskPriority. Le ICodeSandbox teste la compatibilite avant application.

## Quality

✅ Fiabilite — Priorisation criticite, test avant upgrade, historique des changements

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Dependency Scanner, Security CVE Checker, Compatibility Tester, Upgrader
- **Tools**: `file_read`, `http_api`, `shell_command`, `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: TaskPriority (critical CVEs first), ICodeSandbox (compatibility testing), IUrlValidator, Task dependencies, LlmRateLimiter
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/06-engineering-devops/74-dependency-management/config.yaml
```

## What this example demonstrates

- CVE-prioritized dependency vulnerability management
- Sandbox-based compatibility testing before applying upgrades
- Tracked upgrade history with changelog generation
