# 65. Mentorat IA pour Developpeurs

> Quatre perspectives complementaires sur un meme code. Le ICodeSandbox permet de demontrer les alternatives proposees. Le ICodeSecurityAnalyzer verifie la securite du code soumis.

## Quality

🎯 Simplicite — 4 perspectives + sandbox demonstration + suivi progression

## Architecture

- **Process**: `parallel`
- **Agents**: 4 — Code Reviewer, Concept Pedagogue, Patterns Architect, Career Coach
- **Tools**: `file_read`, `directory_read`, `json_tool`, `file_write`
- **Memory**: `SQLite`
- **Key features**: ICodeSandbox (live demonstration), ICodeSecurityAnalyzer, AgentMemory.LongTerm (developer progression), batch execution
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/05-education/65-developer-mentoring/config.yaml
```

## What this example demonstrates

- Four complementary mentoring perspectives analyzed in parallel
- Long-term memory tracking developer progression across sessions
- Synthesis of code review, concepts, patterns, and career coaching into growth plan
