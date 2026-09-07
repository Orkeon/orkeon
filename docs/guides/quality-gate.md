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

- the gate is **blocking where the analysis runs**: analysis with
  `sonar.qualitygate.wait=true`, verdict re-read via the API
  (`/api/qualitygates/project_status`), **non-zero exit code** if the gate is
  FAILED. The analysis is **maintainer-run against the self-hosted SonarQube**
  (`scripts/sonar-analyze.{sh,ps1}`) — **no CI workflow runs SonarQube today**
  (the former Sonar workflows were removed; wiring the analysis into a
  scheduled CI lane remains an open follow-up);
- the thresholds are **transitional** (realistic given the state measured on
  2026-05-31) and their **hardening is planned** below.

**Where the project stands on 2026-09-05**
([full report](../../sonarqube/sonarqube-report-2026-09-05.md)): the gate is **OK** on every
condition it reports — `new_reliability_rating` **A (1)**, `new_security_rating` **A**,
`new_maintainability_rating` **A**, `new_coverage` **78.1 %**,
`new_duplicated_lines_density` **0.0 %** — over 162 k lines carrying **0 bugs,
0 vulnerabilities, 0 unreviewed hotspots** (17 hotspots, all REVIEWED) and 0 min of
technical debt. The two conditions this page described as red or neutralized in May are
therefore **met**. The thresholds below have not moved yet: raising them is the procedure
of §6, not a consequence of this measurement.

## 2. Project key

The SonarQube project key is **`Orkeon`** (renamed on 2026-08-17 as a PUB-01
audit follow-up — the old key was the last trace of the project's
pre-rename name). This supersedes the QCM decision of 2026-06-11 and
accepts the trade-off it wanted to avoid: the server-side analysis history
("new code" baseline, trends) restarts from the first analysis under the new
key; the old project remains browsable on the self-hosted instance. The key
can be overridden via the `SONAR_PROJECT_KEY` environment variable.

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
| `new_coverage` | ≥ | **70%** | 80% | New code at 71.3% on 2026-05-31; aligned with the 70% coverage target (R5.2 decision). Rises with R5.5/R5.6. |
| `new_reliability_rating` | ≤ | **B (2)** | A (1) | B tolerated *minor* bugs while the 3 known *major* bugs got fixed. On 2026-05-31 the new code was at **C** and the condition stayed red on purpose — targeted pressure of the blocking gate on the only real causes (cf. R5.4 work item). Those bugs are gone: on 2026-09-05 the new code is at **A (1)** and the project reports 0 bugs, so B is now slack rather than pressure. Hardening to A is unblocked (§5). |
| `new_security_rating` | ≤ | **A (1)** | A (1) | Already met — kept strict. |
| `new_maintainability_rating` | ≤ | **A (1)** | A (1) | Already met — kept strict. |
| `new_duplicated_lines_density` | ≤ | **3%** | 3% | Already met (0.56%) — kept. |
| `new_security_hotspots_reviewed` | ≥ | **0% (neutralized)** | 100% | Neutralized while 9 inherited hotspots awaited review (security remediation, sheet 07). All 17 hotspots are **REVIEWED** as of 2026-09-05 and none is left unreviewed, so the neutralization no longer masks anything; the 0 threshold keeps the condition **visible** in the gate and the reports until it is raised to 100 (§5). |

## 4. Where the verdict is enforced

| Surface | Mechanism | Effect when the gate is FAILED |
|---|---|---|
| `scripts/sonar-analyze.sh` | `sonar.qualitygate.wait=true` + API re-read of the verdict (status + failed conditions logged) | **exit code ≠ 0** (the Markdown report is still generated) |
| `scripts/sonar-analyze.ps1` | same | **exit code ≠ 0** |

There is **no CI surface**: no GitHub workflow runs a SonarQube analysis (what
CI does gate on every PR is the `-warnaserror` build with the full analyzer
set, the API freeze, the test suites and the docs gates — see
[Project status](../../README.md#project-status)). The verdict (status + each
failed condition with actual value and threshold) is visible in the script
logs.

## 5. Hardening trajectory

Each condition is hardened **as soon as its passing criterion is met** —
no big-bang. Summary:

| Condition | T0 (in force) | Passing criterion | T1 | Passing criterion | T2 (target) |
|---|:--:|---|:--:|---|:--:|
| `new_coverage` | 70% | R5.5 (`Tools.Analysis` contract tests) **and** R5.6 (de-flake) delivered; coverage target raised to 75% (R5.2). New code measured at 78.1% on 2026-09-05 | 75% | new code stable ≥ 80% over ~1 month of merges | **80%** |
| `new_reliability_rating` | B | the 3 known major bugs fixed (remediation campaign) — **met on 2026-09-05**: 0 bugs, new code at A | A | — | **A** |
| `new_security_hotspots_reviewed` | 0% (neutralized) | the 9 inherited hotspots reviewed on the server (security remediation, sheet 07) — **met on 2026-09-05**: 17/17 REVIEWED | 100% | — | **100%** |
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
  This is why the script installs ReportGenerator automatically.
- On a headless machine, the scripts' Docker fallback can be disabled
  (`SONAR_NO_DOCKER=1`): an unreachable server then fails the run instead of
  booting an ephemeral instance without history (which would render the
  "new code" verdict meaningless).

## 8. History

| Date | Event |
|---|---|
| 2026-06-11 | Creation of the "Orkeon Transitional" gate (T0), activation of local blocking (R5.4), alignment of the scripts' project key on the historical key |
| 2026-08-17 | Project key renamed to `Orkeon` (PUB-01 audit follow-up — historical pre-rename key retired; supersedes the 2026-06-11 QCM decision, analysis history restarts under the new key) |
| 2026-08-18 | Document realigned with reality (DOC-02): no CI Sonar workflow exists — enforcement is local to the analysis scripts; the former workflow references removed |
