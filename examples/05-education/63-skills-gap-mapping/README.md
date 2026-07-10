# 63. Team Skills-Gap Mapping

> Assess each team member's competencies against role requirements, consolidate
> them into a team competency map, and recommend priority training matched to
> the gaps.

## What it does

- **Process**: `Parallel` per-member assessment, then sequential consolidation
  and recommendation
- **Agents**: 3 — Skills Assessor, Team Map Consolidator, Training Recommender
- **Tools**: `http_api`, `csv_reader`, `json_tool`, `file_write`
- **Key features**: gap analysis against role targets, semantic matching of gaps
  to a training catalog, prioritization by criticality / impact / ROI
- **Runner**: `standard`

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the [profile matrix](../../appsettings/)). Runs offline
  against the bundled data.

## Required data

Ships a small synthetic dataset (regenerate with
`python3 scripts/generate-vitrine-data.py`). All members and skills are fictional.

| Virtual path | Mount flag | Purpose |
|---|---|---|
| `/data/team-skills.csv` | `--mount examples/05-education/63-skills-gap-mapping/data:/data:ro` | 8 members × 7 skills: proficiency, certification, experience (read by `csv_reader`) |
| `/data/role-requirements.csv` | (same mount) | Required proficiency per role and skill |
| `/data/training-catalog.csv` | (same mount) | Training programs mapped to skills (read by `csv_reader`) |

## Run it

**From source:**

```bash
mkdir -p out
dotnet run --project examples/runners/standard -- \
  --config examples/05-education/63-skills-gap-mapping/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount examples/05-education/63-skills-gap-mapping/data:/data:ro ./out:/output:rw
```

Swap the `--settings` profile for any provider in
[`examples/appsettings/`](../../appsettings/). To only confirm the crew loads
and the data mount is accepted (no LLM), append `--validate`.

> Flag reference: [Run your first example](../../../docs/getting-started/run-your-first-example.md#every-flag-explained).

## Expected output

A training action plan written to `/output` (plus an `AUTO_SUMMARY.md`): a
team competency map with per-skill coverage and critical gaps, and prioritized
training recommendations matched from the shipped catalog with timelines and ROI.

## Approx. duration & cost

- **Duration**: ~2–4 min
- **Cost**: ~8–12 LLM calls; a few thousand tokens.
