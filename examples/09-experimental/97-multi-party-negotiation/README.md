# 97. Negociation Multi-Parties — Budget de Concessions

> Six agents with conflicting interests negotiate via consensus. Each agent has long-term memory of red lines and a concession budget that depletes progressively. FlowEngine manages negotiation rounds.

## Quality

💪 Robustesse — Multi-party consensus, depleting concession budgets, guaranteed convergence, human arbitration

## Architecture

- **Process**: `consensual` (with FlowEngine rounds)
- **Agents**: 7 — Price, Quality, Deadlines, Ethics, Environment, Innovation advocates + Human Arbitrator
- **Tools**: `json_tool`, `file_write`
- **Memory**: `SQLite` (red lines + concession history)
- **Key features**: Consensus voting configurable, IFlowEngine (rounds), FlowState (concession budgets), AgentMemory.LongTerm (red lines), iteration limit, HumanInputContext
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/09-experimental/97-multi-party-negotiation/config.yaml
```

## What this example demonstrates

- Multi-party consensus negotiation with structured rounds and depleting budgets
- Long-term memory for maintaining non-negotiable red lines across rounds
- Guaranteed convergence through budget depletion with human arbitration as last resort
