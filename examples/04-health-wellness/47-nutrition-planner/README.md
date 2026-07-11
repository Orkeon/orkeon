# 47. Nutrition Planner

> A dietitian crew builds a patient dietary profile, designs a week of meals,
> verifies nutritional adequacy against a food composition table, and produces
> daily coaching guidance.

## What it does

- **Process**: `Sequential`
- **Agents**: 4 — Dietary Profile Analyst, Recipe Designer, Nutritional
  Verifier, Daily Coach
- **Tools**: `http_api`, `json_tool`, `csv_reader`, `file_write`
- **Key features**: config-driven dietary constraints, episodic memory across
  sessions, nutrient verification against a shipped composition table
- **Runner**: `standard`

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the [profile matrix](../../appsettings/)). Runs offline
  against the bundled data.

## Required data

Ships a small synthetic dataset (regenerate with
`python3 scripts/generate-vitrine-data.py`). The patient is entirely fictional.

| Virtual path | Mount flag | Purpose |
|---|---|---|
| `/data/patient-history.csv` | `--mount examples/04-health-wellness/47-nutrition-planner/data:/data:ro` | Synthetic patient profile: allergies, intolerances, condition, calorie target (read by `csv_reader`) |
| `/data/food-nutrition.csv` | (same mount) | Food composition per 100 g: calories, macros, iron, calcium, B12, folate |

## Run it

With the installed `orkeon` CLI (release archive or `dotnet tool install`) — or,
from a source checkout, `dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run …`:

```bash
mkdir -p out
orkeon run examples/04-health-wellness/47-nutrition-planner/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount examples/04-health-wellness/47-nutrition-planner/data:/data:ro ./out:/output:rw
```

Swap the `--settings` profile for any provider in
[`examples/appsettings/`](../../appsettings/). To only confirm the crew loads
and the data mount is accepted (no LLM), append `--validate`.

> Flag reference: [Run your first example](../../../docs/getting-started/run-your-first-example.md#every-flag-explained).

## Expected output

A personalized nutrition plan written to `/output` (plus an `AUTO_SUMMARY.md`): a
dietary profile honoring the shipped constraints, a 7-day meal plan, a nutrient
adequacy analysis computed from the food composition table, and a daily coaching
message.

## Approx. duration & cost

- **Duration**: ~3–5 min
- **Cost**: ~10–14 LLM calls; a few thousand tokens.
