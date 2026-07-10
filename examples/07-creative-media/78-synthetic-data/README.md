# 78. Privacy-Safe Synthetic Data

> Model a source dataset's schema and distributions, generate synthetic records,
> validate statistical fidelity, and certify anti-reidentification — with zero
> persistence of the source.

## What it does

- **Process**: `Sequential`
- **Agents**: 4 — Schema Modeler, Data Generator, Distribution Validator,
  Anti-Reidentification Validator
- **Tools**: `csv_reader`, `json_tool`, `file_write`
- **Key features**: privacy-by-design (never stores source records), statistical
  fidelity checks (KS / chi-squared / correlation), k-anonymity / l-diversity /
  t-closeness certification
- **Runner**: `standard`

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the [profile matrix](../../appsettings/)). Runs offline
  against the bundled data.

## Required data

Ships a small synthetic dataset (regenerate with
`python3 scripts/generate-vitrine-data.py`). The "source" is itself synthetic —
the crew models its schema without copying records into its output.

| Virtual path | Mount flag | Purpose |
|---|---|---|
| `/data/source-customers.csv` | `--mount examples/07-creative-media/78-synthetic-data/data:/data:ro` | 150-row synthetic customer table: age, city, plan, tenure, charge, churn (read by `csv_reader`) |

## Run it

**From source:**

```bash
mkdir -p out
dotnet run --project examples/runners/standard -- \
  --config examples/07-creative-media/78-synthetic-data/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount examples/07-creative-media/78-synthetic-data/data:/data:ro ./out:/output:rw
```

Swap the `--settings` profile for any provider in
[`examples/appsettings/`](../../appsettings/). To only confirm the crew loads
and the data mount is accepted (no LLM), append `--validate`.

> Flag reference: [Run your first example](../../../docs/getting-started/run-your-first-example.md#every-flag-explained).

## Expected output

Written to `/output` (plus an `AUTO_SUMMARY.md`): a schema model of the source
distributions, a generated synthetic dataset, a fidelity validation report, and
a privacy certification with k-anonymity / l-diversity / t-closeness scores.

## Approx. duration & cost

- **Duration**: ~3–5 min
- **Cost**: ~10–14 LLM calls; a few thousand tokens.
