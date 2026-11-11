> 🇫🇷 [Version française](../fr/guides/quality-gate.md)

# SonarQube Quality Gate — transitional policy and hardening trajectory

> **"Loosen then harden"**: the Quality Gate is **blocking**, with transitional
> thresholds hardened as the debt is paid down. This document is the reference for
> the hardening trajectory.

## 1. Context

The gate is **blocking where the analysis runs**: analysis with
`sonar.qualitygate.wait=true`, verdict re-read via `/api/qualitygates/project_status`,
**non-zero exit code** when the gate is FAILED. The analysis is maintainer-run against
the self-hosted SonarQube (`scripts/sonar-analyze.{sh,ps1}`); no CI workflow runs
SonarQube. The thresholds are **transitional** and their hardening is planned below.

## 2. Project key

The SonarQube project key is **`Orkeon`**; override it with the `SONAR_PROJECT_KEY`
environment variable.

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
| `new_coverage` | ≥ | **70%** | 80% | Below the 80% target while the suites grow. |
| `new_reliability_rating` | ≤ | **B (2)** | A (1) | The project reports 0 bugs and new code is at A: hardening to A is unblocked (§5). |
| `new_security_rating` | ≤ | **A (1)** | A (1) | Already met — kept strict. |
| `new_maintainability_rating` | ≤ | **A (1)** | A (1) | Already met — kept strict. |
| `new_duplicated_lines_density` | ≤ | **3%** | 3% | Already met (0.56%) — kept. |
| `new_security_hotspots_reviewed` | ≥ | **0% (neutralized)** | 100% | Every hotspot is reviewed; the 0 threshold keeps the condition visible until it is raised to 100 (§5). |

## 4. Where the verdict is enforced

| Surface | Mechanism | Effect when the gate is FAILED |
|---|---|---|
| `scripts/sonar-analyze.sh` | `sonar.qualitygate.wait=true` + API re-read of the verdict (status + failed conditions logged) | **exit code ≠ 0** (the Markdown report is still generated) |
| `scripts/sonar-analyze.ps1` | same | **exit code ≠ 0** |

There is **no CI surface**: no GitHub workflow runs a SonarQube analysis (what
CI does gate on every PR is the `-warnaserror` build with the full analyzer
set, the API freeze, the test suites and the docs gates — see
[the CI workflow](https://github.com/Orkeon/orkeon/blob/main/.github/workflows/ci.yml)). The verdict (status + each
failed condition with actual value and threshold) is visible in the script
logs.

## 5. Hardening trajectory

Each condition is hardened **as soon as its passing criterion is met** —
no big-bang. Summary:

| Condition | T0 (in force) | Passing criterion | T1 | Passing criterion | T2 (target) |
|---|:--:|---|:--:|---|:--:|
| `new_coverage` | 70% | new code ≥ 75% over the measured runs | 75% | new code stable ≥ 80% over ~1 month of merges | **80%** |
| `new_reliability_rating` | B | met: 0 bugs, new code at A | A | — | **A** |
| `new_security_hotspots_reviewed` | 0% (neutralized) | met: every hotspot reviewed | 100% | — | **100%** |
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
2. Update this document (tables §3 and §5).
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
  The scripts therefore install both coverage tools when they are missing —
  `dotnet-coverage`, which measures (the test projects run on
  Microsoft.Testing.Platform, which rejects the VSTest `--collect` collector), and
  ReportGenerator, which converts its Cobertura output to the SonarQube generic
  format — and **abort the analysis** when the Cobertura report holds no class,
  rather than let the gate evaluate against nothing.
- On a headless machine, the scripts' Docker fallback can be disabled
  (`SONAR_NO_DOCKER=1`): an unreachable server then fails the run instead of
  booting an ephemeral instance without history (which would render the
  "new code" verdict meaningless).
- The scripts measure coverage on the **unit and fast suites only**, with the filter of
  `coverage.yml` (`--filter "Category!=Integration&Category!=Slow"`, §8). The Integration
  (Testcontainers: Docker and gigabytes of images) and Slow categories are not run, so the
  tests never need Docker and the local figure covers the same scope as the public one.
  Only Orkeon's own assemblies are measured (`dotnet-coverage.settings.xml`, shared with
  `coverage.yml`): `Orkeon.*`, `orkeon` and `orkeon-host`, without the test assemblies.
  Run those categories on their own:
  `dotnet test Orkeon.sln --no-build --filter "Category=Integration|Category=Slow" -- --ignore-exit-code 8`.

## 8. Where the public coverage number comes from

The SonarQube pass above is local: its report is written under `sonarqube/`, which the
repository does not track, so nothing it measures can be checked by a third party. The
coverage figure that *can* be checked comes from the
[Coverage workflow](https://github.com/Orkeon/orkeon/actions/workflows/coverage.yml)
(`.github/workflows/coverage.yml`): on every push to `main` and weekly, it runs the unit and
fast suites — the same `Category!=Integration&Category!=Slow` filter as `ci.yml` — under
`dotnet-coverage` (pinned) on Orkeon's own assemblies only (`dotnet-coverage.settings.xml`: packages
that ship their PDB, such as Aspire.Hosting or StackExchange.Redis, are not measured), refuses an
empty measurement, writes the ReportGenerator
summary to the run page and keeps `coverage.cobertura.xml` plus an HTML report as the
`coverage` artefact (90 days). It is *line coverage by the unit and fast suites*; the
Integration and Slow categories run in `integration.yml` without coverage. The README
links to the workflow and quotes no number: a run is a measurement, a typed number is a
claim.
