# 66. Pipeline CI/CD avec Rollback Automatique

> Le ICheckpointManager sauvegarde l'etat a chaque stage. En cas d'echec, le rollback est automatique. Les CrewHooks notifient en temps reel.

## Quality

✅ Fiabilite — Checkpoint par stage, rollback automatique, notifications temps reel

## Architecture

- **Process**: `sequential`
- **Agents**: 4 — PR Analyst, Test Selector, Security Reviewer, Deployer
- **Tools**: `github`, `file_read`, `http_api`, `json_tool`, `file_write`
- **Memory**: `Redis`
- **Key features**: ICheckpointManager + IResumeEngine (rollback), CrewHooks (OnStageCompleted, OnPipelineFailed), TaskCallbacks, ICodeSecurityAnalyzer
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/06-engineering-devops/66-cicd-pipeline/config.yaml
```

## What this example demonstrates

- Checkpoint-based pipeline with automatic rollback on failure
- Security review integrated into the CI/CD pipeline
- Real-time notifications via CrewHooks at each pipeline stage
