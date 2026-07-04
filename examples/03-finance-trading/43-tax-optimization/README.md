# 43. Optimisation Fiscale Multi-Juridictions

> Agents specialises par juridiction analysent en parallele les implications fiscales. Le IConfigurationDiffService permet de comparer visuellement les scenarios entre juridictions. (Features planifiees: IConfigurationVersioning, IConfigurationDiffService)

## Quality

:muscle: Robustesse -- Configuration versionnee, diff entre scenarios, parallelisme par juridiction

## Architecture

- **Process**: `parallel`
- **Agents**: 5 -- US Tax Specialist, EU Tax Specialist, APAC Tax Specialist, Strategy Consolidator, Senior Tax Advisor (human)
- **Tools**: `http_api`, `pdf_reader`, `csv_reader`, `json_tool`, `file_write`
- **Memory**: `SQLite` (versioned configurations)
- **Key features**: IConfigurationVersioning (planned), IConfigurationDiffService (planned), IYamlDiffService, HumanInputContext, batch execution
- **Runner**: `trading`

## Prerequisites

1. .NET 10 SDK
2. Configure `appsettings.json` with your LLM API key

## Run

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/43-tax-optimization/config.yaml
```

## What this example demonstrates

- Parallel jurisdiction-specific tax analysis (US, EU, APAC)
- Scenario comparison with configuration diff for visual strategy review
- Human-in-the-loop senior validation for compliance and reputational risk
