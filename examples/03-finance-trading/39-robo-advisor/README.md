# 39. Robo-Advisor Profilage Interactif

> Le HumanAgent collecte le profil de risque via questions interactives typees (choix multiples, numeriques, confirmation). La memoire episodique chiffree conserve l'historique complet de chaque client.

## Quality

:lock: Securite -- Interaction multi-modale, memoire chiffree par client, historique complet

## Architecture

- **Process**: `sequential`
- **Agents**: 4 -- Risk Profile Assessor (human input), Strategy Allocator, Portfolio Monitor, Rebalancer
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `ask_question`, `file_write`
- **Memory**: `EncryptedSQLite` (episodic per client)
- **Key features**: HumanInputContext (types: MultipleChoice, Numeric, Text), EncryptedSqliteMemoryProvider, AgentMemory.Episodic
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/39-robo-advisor/config.yaml
```

## What this example demonstrates

- Interactive multi-modal risk profiling with typed human input questions
- Encrypted episodic memory maintaining complete client history across sessions
- Full robo-advisory pipeline from profiling through strategy design and rebalancing
