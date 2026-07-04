# 41. Detection de Delit d'Initie

> ObserverAgent correle les transactions inhabituelles avec les evenements d'entreprise en temps reel. Le IKnowledgeSource alimente les bases reglementaires. Audit exhaustif.

## Quality

:lock: Securite -- Audit exhaustif, memoire chiffree, correlation multi-source

## Architecture

- **Process**: `parallel`
- **Agents**: 4 -- Transaction Observer, Event Correlator, Behavioral Analyst, Regulatory Reporter
- **Tools**: `http_api`, `database_query`, `json_tool`, `semantic_search`, `file_write`
- **Memory**: `EncryptedRedis`
- **Key features**: ObserverAgent, IKnowledgeSource (regulations), AuditEventTypes, EncryptedRedisMemoryProvider, CrewHooks (OnAnomalyDetected)
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/41-insider-trading-detection/config.yaml
```

## What this example demonstrates

- Real-time transaction surveillance with ObserverAgent continuous monitoring
- Correlation of trading anomalies with corporate events and insider access
- Behavioral profiling with semantic vector comparison in encrypted memory
