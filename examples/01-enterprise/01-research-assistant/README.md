# 1. Multi-Source Research Assistant

> A crew of 3 agents collaborates sequentially to produce a structured synthesis
> report from heterogeneous sources (web, PDF, CSV) with proper citations.

## What it does

- **Process**: `Sequential`
- **Agents**: 3 — Web Researcher, Document Analyst, Synthesis Writer
- **Tools**: `web_scrape`, `pdf_reader`, `csv_reader`, `json_tool`, `file_write`
- **Key features**: task dependencies (each task builds on the previous), mixed
  document-format analysis (PDF + CSV) combined with web scraping, JSON-schema
  structured output
- **Runner**: `standard`

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the [profile matrix](../../appsettings/)). Web results
  improve markedly with network access for `web_scrape`; the bundled documents
  work offline.

## Required data

Ships a small synthetic dataset (regenerate with
`python3 scripts/generate-vitrine-data.py`).

| Virtual path | Mount flag | Purpose |
|---|---|---|
| `/data/ev-market-report.pdf` | `--mount examples/01-enterprise/01-research-assistant/data:/data:ro` | Synthetic 1-page EV market report (read by `pdf_reader`) |
| `/data/ev-sales-by-region.csv` | (same mount) | EV sales & market share by year/region (read by `csv_reader`) |

## Run it

**From source:**

```bash
mkdir -p out
dotnet run --project examples/runners/standard -- \
  --config examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --initial-context "Electric vehicle (EV) adoption trends 2019-2024" \
  --mount examples/01-enterprise/01-research-assistant/data:/data:ro ./out:/output:rw
```

Swap the `--settings` profile for any provider in
[`examples/appsettings/`](../../appsettings/). To only confirm the crew loads
and the data mount is accepted (no LLM), append `--validate`.

> Flag reference: [Run your first example](../../../docs/getting-started/run-your-first-example.md#every-flag-explained).

## Expected output

A Markdown synthesis report written to `/output` (plus an `AUTO_SUMMARY.md`),
with an executive summary, 3–5 thematic sections keyed to the shipped PDF/CSV
findings and any web sources, key takeaways, and a bibliography with citations.

## Approx. duration & cost

- **Duration**: ~2–4 min
- **Cost**: ~6–10 LLM calls; a few thousand tokens depending on web results.
