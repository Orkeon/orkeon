# 97. Multi-Party Negotiation

> Six advocates with conflicting priorities negotiate a procurement package over
> structured rounds, each spending a depleting concession budget; a human
> arbitrator breaks any remaining deadlock.

## What it does

- **Process**: `Consensual` (structured negotiation rounds)
- **Agents**: 7 — Price, Quality, Deadline, Ethics, Environment, and Innovation
  advocates + Human Arbitrator
- **Tools**: `json_tool`, `file_write`
- **Key features**: consensual voting with depleting concession budgets,
  long-term memory of each party's red lines, human-in-the-loop arbitration
- **Runner**: `standard`

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the [profile matrix](../../appsettings/)).
- The final `arbitrate_if_needed` task is `humanInput: true`; the runner prompts
  on stdin (or auto-approves under a non-interactive provider).

## Required data

**No mounted input data.** This crew uses only `json_tool` (inline JSON) and
`file_write`, so per the [example data policy](../../../docs/reference/example-data-policy.md)
it ships no `data/` folder to mount. The negotiation scenario is supplied as text
via `--initial-context`. A fuller, human-readable brief lives at
[`data/procurement-brief.md`](data/procurement-brief.md) for reference only — it
is not read by any tool.

## Run it

**From source:**

```bash
dotnet run --project examples/runners/standard -- \
  --config examples/09-experimental/97-multi-party-negotiation/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --initial-context "24-month electronics supply contract. Target unit price \$42 (seller asks \$49.50, buyer max \$46). Required: ISO 9001 + IPC-A-610 Class 3, first shipment in 8-10 weeks, conflict-free materials with annual audit, >=30% recycled content, firmware-upgrade path. Each party has a 100-point concession budget." \
  --mount ./out:/output:rw
```

(Run `mkdir -p out` first.) Swap the `--settings` profile for any provider in
[`examples/appsettings/`](../../appsettings/). To only confirm the crew loads,
append `--validate` (no data mount is needed).

> Flag reference: [Run your first example](../../../docs/getting-started/run-your-first-example.md#every-flag-explained).

## Expected output

A final consensus document written to `/output` (plus an `AUTO_SUMMARY.md`): each
party's initial positions and red lines, round-by-round concession trades with
remaining budgets, a convergence/impasse determination, and — if consensus was
not fully reached — the arbitrator's binding ruling on contested items.

## Approx. duration & cost

- **Duration**: ~4–8 min (multi-round, 7 agents)
- **Cost**: ~15–25 LLM calls; a mid four-figure token count.
