# 68. Migration de Base de Donnees — Reprise Exacte

> Chaque etape est checkpointee. En cas d'echec a l'etape 3 sur 4, le IResumeEngine reprend exactement a l'etape 3. Le DatabaseQueryTool valide l'integrite referentielle.

## Quality

💪 Robustesse — Reprise exacte au point d'arret, validation integrite, zero perte de donnees

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — Schema Cartographer, Correspondence Mapper, Script Transformer, Integrity Validator
- **Tools**: `database_query`, `file_read`, `file_write`, `json_tool`
- **Memory**: `SQLite`
- **Key features**: ICheckpointManager + IResumeEngine + IStateStore, TaskCallbacks, output validation (referential integrity)
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/06-engineering-devops/68-database-migration/config.yaml
```

## What this example demonstrates

- Checkpoint-based migration with exact resume on failure
- Referential integrity validation after migration
- Idempotent migration scripts with rollback support
