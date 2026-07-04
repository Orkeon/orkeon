> 🇫🇷 [Version française](../fr/guides/quality-gate.md)

# SonarQube Quality Gate — transitional policy and hardening trajectory

> Work item **R5.4** (finding TST-005) · Maintainer decision (QCM 2026-06-11):
> **"Loosen then harden"** — the Quality Gate becomes **blocking immediately**,
> with realistic transitional thresholds, then is hardened as the debt is
> paid down. This document is the reference for the hardening trajectory.

## 1. Context

Before R5.4, the SonarQube Quality Gate was in **ERROR on 3 conditions**
(`new_coverage 71.3 < 80`, `new_reliability_rating 3 > 1`,
`new_security_hotspots_reviewed 0.0 < 100` — report of 2026-05-31) but
**no pipeline consumed this verdict**: the analysis completed without
`sonar.qualitygate.wait=true` or any status read, and the CI stayed green.
The "80% on new code" policy was documented but never enforced.

Since R5.4:

- the gate is **blocking**: analysis with `sonar.qualitygate.wait=true`, verdict
  re-read via the API (`/api/qualitygates/project_status`), **non-zero exit code**
  if the gate is FAILED — locally (scripts) as well as in CI (workflow);
- the thresholds are **transitional** (realistic given the state measured on
  2026-05-31) and their **hardening is planned** below.

## 2. Project key

The SonarQube project key remains **`CrewAI.NET`** (QCM decision 2026-06-11):
renaming it on the server side would reset the analysis history ("new code"
baseline, trends) to zero. The key can be overridden via the
`SONAR_PROJECT_KEY` environment variable.

## 3. The "Orkeon Transitional" gate

The gate is **provisioned automatically and idempotently** by the analysis
scripts, then **associated with the project**:

- `scripts/sonar-analyze.sh` — `QUALITY_GATE_CONDITIONS` table;
- `scripts/sonar-analyze.ps1` — `$QualityGateConditions` table.

These **two tables are the source of truth for the thresholds** and must remain
identical to each other (and in sync with this document). On each run,
the script creates the gate if it does not exist, creates or updates each
condition whose threshold differs, and (re)associates the gate with the project.

### Conditions (all on **new code**)

| Condition | Direction | Transitional threshold (T0) | Final target | Rationale for the transitional threshold |
|---|---|:--:|:--:|---|
| `new_coverage` | ≥ | **70%** | 80% | New code at 71.3% on 2026-05-31; aligned with the CI coverage gate at 70% (R5.2 decision). Rises with R5.5/R5.6. |
| `new_reliability_rating` | ≤ | **B (2)** | A (1) | B tolerates *minor* bugs while the 3 known *major* bugs get fixed. On 2026-05-31 the new code was at **C**: the condition stays red until those 3 bugs are fixed — **this is intentional** (targeted pressure of the blocking gate on the only real causes, cf. R5.4 work item). |
| `new_security_rating` | ≤ | **A (1)** | A (1) | Already met — kept strict. |
| `new_maintainability_rating` | ≤ | **A (1)** | A (1) | Already met — kept strict. |
| `new_duplicated_lines_density` | ≤ | **3%** | 3% | Already met (0.56%) — kept. |
| `new_security_hotspots_reviewed` | ≥ | **0% (neutralized)** | 100% | 9 inherited hotspots not yet reviewed (in progress via the security remediation, sheet 07). The 0 threshold makes the condition always OK **while keeping it visible** in the gate and the reports. |

## 4. Where the verdict is enforced

| Surface | Mechanism | Effect when the gate is FAILED |
|---|---|---|
| `scripts/sonar-analyze.sh` | `sonar.qualitygate.wait=true` + API re-read of the verdict (status + failed conditions logged) | **exit code ≠ 0** (the Markdown report is still generated) |
| `scripts/sonar-analyze.ps1` | same | **exit code ≠ 0** |
| `.github/workflows/sonar.yml` (`sonarqube` job) | delegates to `sonar-analyze.sh` (`SONAR_NO_DOCKER=1`) | **red job** on push to `main`/`develop` and PRs to `main` |

The verdict (status + each failed condition with actual value and threshold) is
visible in the script logs and the CI job logs.

## 5. Hardening trajectory

Each condition is hardened **as soon as its passing criterion is met** —
no big-bang. Summary:

| Condition | T0 (transitional, today) | Passing criterion | T1 | Passing criterion | T2 (target) |
|---|:--:|---|:--:|---|:--:|
| `new_coverage` | 70% | R5.5 (`Tools.Analysis` contract tests) **and** R5.6 (de-flake) delivered; CI gate raised to 75% (R5.2) | 75% | new code stable ≥ 80% over ~1 month of merges | **80%** |
| `new_reliability_rating` | B | the 3 known major bugs fixed (remediation campaign) | A | — | **A** |
| `new_security_hotspots_reviewed` | 0% (neutralized) | the 9 inherited hotspots reviewed on the server (security remediation, sheet 07) | 100% | — | **100%** |
| `new_security_rating` | A | already at the target level | A | — | **A** |
| `new_maintainability_rating` | A | already at the target level | A | — | **A** |
| `new_duplicated_lines_density` | 3% | already at the target level | 3% | — | **3%** |

**Final state (T2)**: the conditions join those of the built-in
"Sonar way" gate. Two options at that point: switch the project to "Sonar way"
and delete "Orkeon Transitional", or keep the named gate with the final
values (prefer the former to reduce the custom configuration
surface).

## 6. Procedure for changing the thresholds

1. Modify the `QUALITY_GATE_CONDITIONS` table in `scripts/sonar-analyze.sh`
   **and** `$QualityGateConditions` in `scripts/sonar-analyze.ps1` (both
   must remain identical).
2. Update this document (tables §3 and §5, history §8).
3. Re-run an analysis: the idempotent provisioning pushes the new
   thresholds to the server (`update_condition`).

Do not modify the thresholds directly in the SonarQube UI: they would be
overwritten on the next script run.

## 7. Known limits

- The provisioning requires a token with the **"Administer Quality
  Gates"** permission (and "Create Projects" for a fresh server). Failing that, the
  script reports it as WARN and continues: the gate **currently associated** with the
  project is then enforced (the blocking remains effective, but with the server's
  thresholds).
- `new_coverage` is **not evaluated** by SonarQube if no coverage is
  imported: a broken coverage import can wrongly let the gate pass.
  This is why the script installs ReportGenerator automatically and the
  CI workflow installs it explicitly.
- In CI, the scripts' Docker fallback is disabled (`SONAR_NO_DOCKER=1`):
  an unreachable server fails the job instead of booting an ephemeral
  instance without history (which would render the "new code" verdict
  meaningless).
- The old workflow `.github/workflows/sonarqube.yml` (non-blocking, key
  `Orkeon`) is made redundant by `sonar.yml` and is meant to be removed.

## 8. History

| Date | Event |
|---|---|
| 2026-06-11 | Creation of the "Orkeon Transitional" gate (T0), activation of local + CI blocking (R5.4), alignment of the scripts' project key on `CrewAI.NET` |
