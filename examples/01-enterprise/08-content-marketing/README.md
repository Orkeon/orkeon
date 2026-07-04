# 8. Creation de Contenu Marketing

> Sequential pipeline reproducing a natural content team workflow. Each agent refines the previous agent's work. The Fact-Checker verifies claims via web scraping before publication.

## Quality

🎯 Simplicite — The natural creation flow codified into 4 clear, independent steps

## Architecture

- **Process**: `Sequential`
- **Agents**: 4 — Stratege d'Angle (Worker), Redacteur (Worker), Editeur SEO (Worker), Fact-Checker (Worker)
- **Tools**: `web_scrape`, `file_write`, `json_tool`
- **Memory**: `InMemory`
- **Key features**: Linear task dependencies, `TaskCallbacks` (OnStepCompleted), output validation for publication format
- **Runner**: `standard`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/standard -- --config examples/01-enterprise/08-content-marketing/config.yaml
```

## What this example demonstrates

- Sequential refinement pipeline where each agent improves the previous output
- Fact-checking as a mandatory final step before publication
- SEO optimization integrated into the content creation workflow
