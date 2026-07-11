# 19. Experimental Data Analysis

> Four agents analyze an experimental dataset: parallel statistical analysis and
> visualization design, then sequential interpretation and report writing.

## What it does

- **Process**: `Parallel` (stats + visualization run concurrently), then a
  sequential interpretation and reporting join
- **Agents**: 4 — Statistician, Visualizer, Interpreter, Report Writer
- **Tools**: `csv_reader`, `json_tool`, `file_write`
- **Key features**: async task fan-out with a dependency join, structured JSON
  hand-off between tasks, scientific report assembly
- **Runner**: `standard`

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the [profile matrix](../../appsettings/)); validated
  end-to-end against GLM 5.2 (Z.AI). Runs fully offline against the bundled data.

## Required data

Ships a small synthetic dataset (regenerate with
`python3 scripts/generate-vitrine-data.py`).

| Virtual path | Mount flag | Purpose |
|---|---|---|
| `/data/experiment-measurements.csv` | `--mount examples/02-science-research/19-experimental-data/data:/data:ro` | 120-subject control/treatment dose-response dataset (read by `csv_reader`) |
| `/data/experiment-metadata.json` | (same mount) | Study protocol metadata: design, endpoint, hypothesis |

## Run it

With the installed `orkeon` CLI (release archive or `dotnet tool install`) — or,
from a source checkout, `dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run …`:

```bash
mkdir -p out
orkeon run examples/02-science-research/19-experimental-data/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount examples/02-science-research/19-experimental-data/data:/data:ro ./out:/output:rw
```

Swap the `--settings` profile for any provider in
[`examples/appsettings/`](../../appsettings/). To only confirm the crew loads
and the data mount is accepted (no LLM), append `--validate`.

> Flag reference: [Run your first example](../../../docs/getting-started/run-your-first-example.md#every-flag-explained).

## Expected output

A Markdown analysis report written to `/output` (plus an `AUTO_SUMMARY.md`) with
an abstract, methods, results (descriptive stats, a control-vs-treatment
hypothesis test, a dose-response regression with effect size — all computed from
the shipped CSV), visualization specifications, discussion, and conclusions.

## Approx. duration & cost

- **Duration**: ~2–4 min on GLM 5.2
- **Cost**: ~8–12 LLM calls; low four-figure token count.
