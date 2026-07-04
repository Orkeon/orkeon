# 5. Support Client Multi-Niveaux avec Escalade

> 3-tier support system with automatic escalation. L1 triage answers simple queries via knowledge base, complex cases go to L2 specialist, then to L3 human manager. Episodic memory preserves full client history.

## Quality

💪 Robustesse — Automatic delegation with fallback, client context preserved across sessions

## Architecture

- **Process**: `Hierarchical`
- **Agents**: 4 — Superviseur Support (Manager), Agent Triage L1 (Worker), Specialiste L2 (Worker), Manager Escalade L3 (Human)
- **Tools**: `web_scrape`, `http_api`, `json_tool`
- **Memory**: `Redis`
- **Key features**: `IKnowledgeSource` for FAQ, delegation with fallback, `AgentMemory` (short-term + episodic), `IContextWindowManager`
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/05-customer-support/config.yaml
```

## What this example demonstrates

- Hierarchical escalation pattern (L1 -> L2 -> L3) with automatic delegation
- Episodic memory preserving client history across support sessions
- Knowledge base integration for fast resolution of common queries
