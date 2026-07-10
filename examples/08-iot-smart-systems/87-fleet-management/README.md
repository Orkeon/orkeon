# 87. Fleet Management

> A dispatcher assigns delivery missions to vehicle agents, each with its own
> SQLite memory; a maintenance coordinator and a safety observer monitor the
> fleet, and a daily report consolidates operations.

## What it does

- **Process**: `Hierarchical` (Fleet Dispatcher delegates)
- **Agents**: 5 — Fleet Dispatcher (manager), Vehicle Alpha, Vehicle Bravo,
  Maintenance Coordinator, Safety Observer
- **Tools**: `http_api`, `json_tool`, `csv_reader`, `file_write`
- **Key features**: per-vehicle SQLite memory, hierarchical delegation, mission
  checkpointing, continuous safety monitoring
- **Runner**: `standard`

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the [profile matrix](../../appsettings/)). Runs offline
  against the bundled data.

## Required data

Ships a small synthetic dataset (regenerate with
`python3 scripts/generate-vitrine-data.py`). Addresses and vehicles are fictional.

| Virtual path | Mount flag | Purpose |
|---|---|---|
| `/data/missions.csv` | `--mount examples/08-iot-smart-systems/87-fleet-management/data:/data:ro` | 12 delivery missions: address, coordinates, time window, priority, cargo (read by `csv_reader`) |
| `/data/vehicle-diagnostics.csv` | (same mount) | Per-vehicle health: engine hours, tire pressure, brake wear, oil, last service |

## Run it

**From source:**

```bash
mkdir -p out
dotnet run --project examples/runners/standard -- \
  --config examples/08-iot-smart-systems/87-fleet-management/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount examples/08-iot-smart-systems/87-fleet-management/data:/data:ro ./out:/output:rw
```

Swap the `--settings` profile for any provider in
[`examples/appsettings/`](../../appsettings/). To only confirm the crew loads
and the data mount is accepted (no LLM), append `--validate`.

> Flag reference: [Run your first example](../../../docs/getting-started/run-your-first-example.md#every-flag-explained).

## Expected output

A daily fleet report written to `/output` (plus an `AUTO_SUMMARY.md`): the
mission assignment plan built from `missions.csv`, per-vehicle execution reports,
a maintenance status derived from `vehicle-diagnostics.csv`, a safety summary,
and consolidated fleet KPIs.

## Approx. duration & cost

- **Duration**: ~4–7 min (hierarchical delegation + per-vehicle memory)
- **Cost**: ~14–20 LLM calls; a few thousand tokens.
