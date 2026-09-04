# 40. Automated Invoice Processing

> Extract invoices from mixed formats (PDF + CSV), normalize them to a standard
> schema, then three-way match them against purchase orders and receiving records.

## What it does

- **Process**: `Sequential`
- **Agents**: 3 — Invoice Extractor, Data Structurer, Accounting Reconciler
- **Tools**: `pdf_reader`, `csv_reader`, `json_tool`, `file_write`,
  `relational_database_query`, `audit_trail`, `dashboard_metrics`
- **Key features**: multi-format extraction, `ComponentBase` typed pipeline with
  tolerant JSON converters, three-way matching (invoice ↔ PO ↔ goods receipt),
  exception reporting
- **Runner**: `orkeon` CLI (TypeScript crew, tools from [`../_tools/`](../_tools/)) (provides `audit_trail` and `dashboard_metrics`)

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the [profile matrix](../../appsettings/)); validated
  end-to-end against GLM 5.2 (Z.AI). Runs fully offline against the bundled data.

## Required data

Ships a small synthetic dataset (regenerate with
`python3 scripts/generate-vitrine-data.py`). All invoices are read via file
tools; the reconciliation records are CSV (the `relational_database_query` tool
is available but the shipped data is file-based).

| Virtual path | Mount flag | Purpose |
|---|---|---|
| `/data/invoices/INV-2024-0001.pdf` | `--mount examples/03-finance-trading/40-invoice-processing/data:/data:ro` | Synthetic vendor invoice (read by `pdf_reader`) |
| `/data/invoices/INV-2024-0002.pdf` | (same mount) | Second vendor invoice, different format (`pdf_reader`) |
| `/data/invoices/invoices-batch.csv` | (same mount) | Batch of electronic invoices (`csv_reader`) |
| `/data/purchase-orders.csv` | (same mount) | Purchase orders for three-way matching |
| `/data/goods-receipts.csv` | (same mount) | Goods-receipt records for three-way matching |

## Run it

With the installed `orkeon` CLI (the trading tools ship inside the crew's own
TypeScript module) — or, from a source checkout,
`dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run …`:

```bash
mkdir -p out
orkeon run examples/03-finance-trading/40-invoice-processing/main.ork.ts \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount examples/03-finance-trading/40-invoice-processing/data:/data:ro ./out:/output:rw
```

Swap the `--settings` profile for any provider in
[`examples/appsettings/`](../../appsettings/). To only confirm the crew loads
and the data mount is accepted (no LLM), append `--validate`.

> Flag reference: [Run your first example](../../../docs/getting-started/run-your-first-example.md#every-flag-explained).

## Expected output

A reconciliation report written to `/output` (plus an `AUTO_SUMMARY.md`): the
structured invoices, the three-way match result per invoice, and an exception
list flagging mismatches (e.g. the `INV-2024-0005` invoice whose PO-9999 has no
matching purchase order).

## Approx. duration & cost

- **Duration**: ~3–5 min on GLM 5.2
- **Cost**: ~9–14 LLM calls; low four-figure token count.
