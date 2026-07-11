# 34. Portfolio Optimization by Consensus

> Three analysts with different investment philosophies must agree on an
> allocation; a risk manager validates the consensus against constraints.

## What it does

- **Process**: `Consensual` (weighted voting)
- **Agents**: 4 — Fundamental Analyst, Technical Analyst, Macro Strategist,
  Risk Manager Validator
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`,
  `technical_indicators`, `correlation_analysis`, `mean_variance_optimization`,
  `risk_parity`, `hierarchical_risk_parity`, `black_litterman`
- **Key features**: consensual process with weighted voting, long-term memory of
  analyst track records, quantitative optimizers as agent tools, risk validation
  as the final gate
- **Runner**: `trading` (adds the quantitative finance tools)

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the [profile matrix](../../appsettings/)). Runs offline
  against the bundled price/fundamentals data; `http_api` calls are optional.

## Required data

Ships a small synthetic dataset (regenerate with
`python3 scripts/generate-vitrine-data.py`).

| Virtual path | Mount flag | Purpose |
|---|---|---|
| `/data/price-history.csv` | `--mount examples/03-finance-trading/34-portfolio-consensus/data:/data:ro` | 60 trading days of daily close prices for 6 tickers (read by `csv_reader`, fed to `technical_indicators`) |
| `/data/fundamentals.csv` | (same mount) | Per-ticker fundamentals: P/E, P/B, ROE, debt/equity, market cap, yield |

## Run it

With the **`orkeon-trading`** runner (it adds 44 specialized trading tools on top
of the standard toolset) — or, from a source checkout,
`dotnet run --project examples/runners/trading -- --config …`:

```bash
mkdir -p out
orkeon-trading --config examples/03-finance-trading/34-portfolio-consensus/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount examples/03-finance-trading/34-portfolio-consensus/data:/data:ro ./out:/output:rw
```

Swap the `--settings` profile for any provider in
[`examples/appsettings/`](../../appsettings/). To only confirm the crew loads
and the data mount is accepted (no LLM), append `--validate`.

> Flag reference: [Run your first example](../../../docs/getting-started/run-your-first-example.md#every-flag-explained).

## Expected output

A validated portfolio allocation written to `/output` (plus an `AUTO_SUMMARY.md`):
per-ticker weights reconciled from the three analysts' views, the risk metrics
and any adjustments the validator applied, and a compliance confirmation.

## Approx. duration & cost

- **Duration**: ~3–6 min
- **Cost**: ~10–16 LLM calls; a few thousand tokens.
