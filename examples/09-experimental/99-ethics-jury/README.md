# 99. Jury Ethique Multi-Perspectives pour Decisions IA

> Five agents embodying ethical frameworks analyze AI decision impact in parallel. No autonomous decisions are made. The human decides, informed by five perspectives and historical decision outcomes.

## Quality

🔒 Securite — Zero autonomous decisions, five mandatory perspectives, audit trail, outcome history

## Architecture

- **Process**: `parallel` then `sequential` (synthesis) then human decision
- **Agents**: 7 — Utilitarian, Deontological, Care Ethics, Distributive Justice, Virtue Ethics analysts + Synthesizer + Human Decision-Maker
- **Tools**: `json_tool`, `file_write`, `web_scrape` (jurisprudence)
- **Memory**: `SQLite` (decision history + outcomes)
- **Key features**: Batch execution (5 parallel analyses), HumanInputContext (final decision), EvaluationSuite, AgentMemory.LongTerm (precedents), AuditEventTypes
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/09-experimental/99-ethics-jury/config.yaml
```

## What this example demonstrates

- Five parallel ethical analyses ensuring comprehensive moral evaluation
- Absolute human-in-the-loop requirement for all decisions (zero AI autonomy)
- Complete audit trail with decision precedents for accountability and learning
