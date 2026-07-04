# 32. Detection de Fraude en Temps Reel

> L'ObserverAgent surveille un flux continu de transactions. Le Pattern Agent detecte les anomalies par comparaison semantique vectorielle avec les patterns connus en memoire Redis chiffree.

## Quality

:lock: Securite -- Memoire chiffree, rate limiting, audit complet, detection temps reel

## Architecture

- **Process**: `parallel`
- **Agents**: 4 -- Surveillance Observer, Pattern Detector, Investigator, Alerter
- **Tools**: `http_api`, `json_tool`, `database_query`, `semantic_search`, `file_write`
- **Memory**: `EncryptedRedis`
- **Key features**: ObserverAgent, EncryptedRedisMemoryProvider, LlmRateLimiter, AuditEventTypes, vector search pattern matching, TaskCallbacks alerts
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/32-fraud-detection/config.yaml
```

## What this example demonstrates

- Real-time transaction monitoring with ObserverAgent continuous surveillance
- Semantic vector matching against known fraud patterns in encrypted memory
- Full audit trail generation for regulatory compliance (SAR-ready)
