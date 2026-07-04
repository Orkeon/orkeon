# 92. Logistique Warehouse — Batch Haute Performance

> High-performance parallel execution for real-time warehouse order management. Batch execution and rate limiting maximize throughput while protecting backend systems.

## Quality

💪 Robustesse — High-performance batch execution, rate limiting, order prioritization

## Architecture

- **Process**: `parallel`
- **Agents**: 4 — Order Orchestrator, AGV Route Controller, Real-Time Inventory Tracker, Quality Agent
- **Tools**: `http_api`, `database_query`, `json_tool`, `file_write`
- **Memory**: `Redis` (real-time state)
- **Key features**: Batch execution, LlmRateLimiter, TaskPriority (urgent orders), AgentStatus, CrewHooks
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key
3. Redis instance running for real-time warehouse state

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/08-iot-smart-systems/92-warehouse-logistics/config.yaml
```

## What this example demonstrates

- Batch execution for high-throughput order processing in parallel
- Rate limiting to protect WMS and backend systems from overload
- Priority-based order handling ensuring urgent deliveries are processed first
