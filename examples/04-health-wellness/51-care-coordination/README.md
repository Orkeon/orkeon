# 51. Coordination de Soins Broadcast

> Un ManagerAgent coordonne les intervenants multi-disciplinaires. Le protocole Broadcast garantit que toute mise a jour du plan de soins est communiquee a tous les agents sans exception.

## Quality

:muscle: Robustesse -- Broadcast garanti, zero perte d'information, coordination centralisee

## Architecture

- **Process**: `hierarchical`
- **Agents**: 5 -- Care Coordinator (manager), Treating Physician, Specialist, Physiotherapist, Nurse
- **Tools**: `json_tool`, `http_api`, `file_write`
- **Memory**: `EncryptedRedis` (shared care plan)
- **Key features**: Communication Broadcast, DelegationEvents, EncryptedRedisMemoryProvider, CrewHooks (OnPlanUpdated), AgentStatus
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/04-health-wellness/51-care-coordination/config.yaml
```

## What this example demonstrates

- Guaranteed Broadcast protocol ensuring all care team members receive every update
- Hierarchical coordination with centralized care plan management
- Multi-disciplinary team collaboration with encrypted shared memory
