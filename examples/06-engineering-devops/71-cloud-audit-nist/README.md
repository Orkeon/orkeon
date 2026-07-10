# 71. Multi-Pillar Cloud Audit (NIST)

> Six agents audit a cloud estate across five pillars (compute, network, storage,
> IAM, cost) in parallel, then consolidate findings into a NIST-mapped report.

## What it does

- **Process**: `Parallel` pillar audits, then a sequential consolidation
- **Agents**: 6 — Compute, Network, Storage, IAM, and Cost auditors + Audit
  Reporter
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Key features**: five-pillar parallel fan-out, HTTP header sanitization and
  rate limiting on cloud API calls, NIST control mapping in the final report
- **Runner**: `standard`

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the [profile matrix](../../appsettings/)). Most pillar
  audits call `http_api` (live cloud APIs) and work best with network access;
  the cost pillar reads the bundled billing export offline.

## Required data

Ships a small synthetic dataset (regenerate with
`python3 scripts/generate-vitrine-data.py`). The compute/network/storage/IAM
pillars query live APIs via `http_api`; only the cost pillar reads a local file.

| Virtual path | Mount flag | Purpose |
|---|---|---|
| `/data/cloud-billing.csv` | `--mount examples/06-engineering-devops/71-cloud-audit-nist/data:/data:ro` | 40-row synthetic billing export: service, region, monthly cost, utilization, tagging (read by `csv_reader`) |

## Run it

**From source:**

```bash
mkdir -p out
dotnet run --project examples/runners/standard -- \
  --config examples/06-engineering-devops/71-cloud-audit-nist/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount examples/06-engineering-devops/71-cloud-audit-nist/data:/data:ro ./out:/output:rw
```

Swap the `--settings` profile for any provider in
[`examples/appsettings/`](../../appsettings/). To only confirm the crew loads
and the data mount is accepted (no LLM), append `--validate`.

> Flag reference: [Run your first example](../../../docs/getting-started/run-your-first-example.md#every-flag-explained).

## Expected output

A NIST-compliant audit report written to `/output` (plus an `AUTO_SUMMARY.md`):
per-pillar findings with severity ratings, a cost section derived from the
shipped billing export (idle resources, rightsizing, tagging gaps), NIST control
mappings, and a prioritized remediation plan.

## Approx. duration & cost

- **Duration**: ~3–6 min
- **Cost**: ~12–18 LLM calls; a few thousand tokens.
