# 73. Refactoring Securise Sandbox

> Le code refactore est systematiquement execute dans un ICodeSandbox et analyse par ICodeSecurityAnalyzer avant validation. Aucun code ne touche la production sans passer les tests.

## Quality

🔒 Securite — Sandbox securise, analyse securite systematique, validation non-regression

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Code Smell Detector, Refactoring Architect, Sandbox Implementer, Non-Regression Tester
- **Tools**: `file_read`, `directory_read`, `file_write`, `shell_command`, `json_tool`
- **Memory**: `InMemory`
- **Key features**: ICodeSandbox (isolated execution), ICodeSecurityAnalyzer, IPathValidator, output validation (tests pass)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/06-engineering-devops/73-secure-refactoring/config.yaml
```

## What this example demonstrates

- Sandbox-isolated code execution for safe refactoring validation
- Security analysis as a mandatory gate before production changes
- Full non-regression verification with automated test execution
