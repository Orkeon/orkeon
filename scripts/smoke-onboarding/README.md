# Onboarding smoke test (P3-4)

`run-smoke.sh` is the **executable, measurable** implementation of the onboarding
acceptance criterion from
`backstage/remediation-onboarding-examples-experiments-2026-07-10.md` §2:

> On a clean machine (Linux/macOS), a new user following only the READMEs reaches
> a successful crew run in ≤ 20 minutes of active time, **by the path of their
> choice**: sources, a Releases binary, or a container.

It walks the three documented onboarding paths, **times each**, and reports
`PASS` / `FAIL` / `SKIP` with durations.

## The three scenarios

| # | Path | What it exercises |
|---|------|-------------------|
| **A** | From sources | `docs/getting-started/run-your-first-example.md`: copy an `appsettings.*.local.json.example`, then run the `01-research-assistant` crew through the `orkeon` CLI (`orkeon run <crew.yaml>`). |
| **B** | Experiments submodule | `experiments/09-factures-extraction/run.sh`: (1) missing settings must print the actionable `cp …` message and exit 1; (2) after `cp` of the template, the runner (source mode) starts and reaches the LLM call. |
| **C** | No SDK | The same showcase crew from a **self-contained Releases binary** (`ORKEON_SMOKE_RELEASE_URL`) or the **container image** (`ORKEON_SMOKE_IMAGE`, or a locally-built `orkeon-runners` image) — nothing is compiled. |

## What "success" means

The smoke does **not** require a live LLM key. Its job is to prove the whole
onboarding chain is sound — build, strict config load, crew construction with
**zero "tool not found" warnings**, and the LLM pre-flight probe — so that the
*only* thing a real user still has to supply is a reachable endpoint and a key.

- **Without a key (default).** Each run points the LLM `BaseUrl` at a loopback
  port nobody listens on (`http://127.0.0.1:1`) via the documented
  `ORKEON_Llm__BaseUrl` override. The connection is refused instantly, so the
  runner's TCP pre-flight probe reports the endpoint unreachable and exits with
  **code 2** — deterministically and *fast*, whether or not the CI runner has
  network egress. The PASS signal is that exit code **2 together with the
  runner's actionable "endpoint unreachable" message** (exit 2 alone is not
  enough — the runner also uses it for e.g. a bad mount, so both are required).
  A filtered *remote* port is deliberately avoided: it makes the connect hang on
  a long timeout instead of failing fast.
- **With a key.** Export `ORKEON_SMOKE_SETTINGS=/abs/path/appsettings.json`
  pointing at a working profile. The no-key redirect is dropped and every
  scenario is asserted on a **full run (exit 0)**.

A run that fails to build, breaks strict config load, or emits a
`tool not found in registry` warning is a **FAIL** regardless of the key — that is
a genuine onboarding regression. An exit 0 *without* a key is also a FAIL (a
false/empty success).

## Usage

```bash
# Default: no key — asserts the chain up to the LLM probe (exit 2).
bash scripts/smoke-onboarding/run-smoke.sh

# With a real key — asserts full runs (exit 0) on every scenario.
export ORKEON_SMOKE_SETTINGS=/abs/path/to/appsettings.local.json
bash scripts/smoke-onboarding/run-smoke.sh

# Scenario C against a published container image or a self-contained binary.
export ORKEON_SMOKE_IMAGE=ghcr.io/orkeon/orkeon-runners:latest
export ORKEON_SMOKE_RELEASE_URL=https://github.com/Orkeon/orkeon/releases/download/vX/orkeon-linux-x64.tar.gz
bash scripts/smoke-onboarding/run-smoke.sh
```

### Environment variables

| Variable | Effect |
|----------|--------|
| `ORKEON_SMOKE_SETTINGS` | Absolute path to a **working** `appsettings.json`. Switches all scenarios to full-run assertions (exit 0). Unset ⇒ no-key mode. |
| `ORKEON_SMOKE_RELEASE_URL` | URL of a self-contained runner archive (`.tar.gz` / `.zip`) or bare binary for scenario C. Takes precedence over the image path. |
| `ORKEON_SMOKE_IMAGE` | Container image for scenario C (e.g. `ghcr.io/orkeon/orkeon-runners:latest`). When unset, a locally-built `orkeon-runners:test` / `:latest` image is used if present. |
| `ORKEON_SMOKE_TIMEOUT` | Per-run timeout in seconds (default `600`). The first source build plus a virtiofs/WSL2 runner start can take a couple of minutes; on a normal machine each run is well under 30 s. |

## When to run it

At **every release** — it is the gate for the "≤ 20 min onboarding" criterion.
It is written to run on a **clean checkout in CI** (Docker image): it depends on
no pre-existing local state and removes every file it copies (the appsettings
copies under `examples/` and `experiments/` in particular), so
`git status` — including `git -C experiments status` — stays clean outside
`output/`.

> Scenario C is **skippable by design**: until the self-contained binary and the
> `ghcr.io/orkeon/orkeon-runners` image are published, leave
> `ORKEON_SMOKE_RELEASE_URL` / `ORKEON_SMOKE_IMAGE` unset and the scenario reports
> `SKIP` (unless a local `orkeon-runners` image is present, in which case it runs
> for real). A `SKIP` does not fail the smoke.

## Interpreting the summary

```
    SC   RESULT   SECONDS   NOTE
    --   ------   -------   ----
    A    PASS         148   chain OK up to the LLM call; endpoint unreachable + exit 2 …
    B    PASS         132   …
    C    SKIP           0   no release binary and no runnable image — publish pending
         TOTAL        280
```

- **PASS** — the scenario met its assertion (exit 2 no-key, or exit 0 with a key).
- **FAIL** — build/config/registry breakage, a wrong exit code, or a timeout. The
  last 15 log lines of the run are printed above the summary for triage.
- **SKIP** — the scenario could not be attempted (e.g. scenario C with nothing to
  run, or the experiments submodule not checked out). Skips never fail the smoke.

The script exits `0` when no non-skipped scenario failed, `1` otherwise.

## Scope / non-goals

- **Windows is out of scope** for this lot (declared in the remediation plan §9);
  the script targets Linux/macOS (`bash`, `docker`, coreutils).
- The smoke does not measure wall-clock download time (SDK, NuGet, image pulls) —
  only active run time, matching the acceptance criterion.
