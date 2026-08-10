# Onboarding smoke tests

This directory holds four smokes. They answer two different questions, so read
the one that matches yours:

| Script | Question it answers | Runs where |
|--------|--------------------|------------|
| `run-smoke.sh` | Can a newcomer reach a successful crew run in ≤ 20 min, **from a clean checkout**? | Linux/macOS, locally |
| `run-smoke.ps1` | Does the **published Windows archive** install, run and uninstall on a real Windows box? (WIN-06) | `windows-latest`, in `release.yml` |
| `run-smoke-deb.sh` | Does the **published `.deb`** install through apt, run and remove cleanly? (LIN-02) | `ubuntu-latest`, in `release.yml` |
| `run-smoke-tarball.sh` | Does the **published `.tar.gz`** install through `install.sh`, run and uninstall cleanly? (MAC-02) | `macos-latest`, in `release.yml`; also locally on Linux |

The rest of this page documents `run-smoke.sh`; the three released-artefact
smokes are covered in [their own section](#released-artefact-smokes-win-06--lin-02--mac-02)
at the bottom.

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

- **Windows is out of scope** for `run-smoke.sh` (declared in the remediation
  plan §9); it targets Linux/macOS (`bash`, `docker`, coreutils). Windows is
  covered by `run-smoke.ps1` below.
- The smoke does not measure wall-clock download time (SDK, NuGet, image pulls) —
  only active run time, matching the acceptance criterion.

---

## Released-artefact smokes (WIN-06 / LIN-02 / MAC-02)

`run-smoke.ps1`, `run-smoke-deb.sh` and `run-smoke-tarball.sh` are siblings. They
take the artefact `release.yml` just built, **install it the way a user would**,
walk the whole onboarding chain on the installed binary, and uninstall. All three
are wired as jobs that gate publication: the Release is attached only after they
pass, and each installs from the *job* artefact rather than the Release, so a
broken payload never reaches a user.

They share everything but the install and uninstall phases. For the two shell
ones that sharing is literal — the behavioural steps, the payload whitelist and
the doctor verdict live in **`lib/smoke-common.sh`**, which both source, so they
cannot drift. `run-smoke.ps1` mirrors the same logic in PowerShell. All three use
the same fixtures in `fixtures/`:

| Fixture | Why it exists |
|---------|---------------|
| `offline-crew.yaml` | One agent, one task, no tools, no network. With no `Llm` section configured the runtime falls back to the `<undefined-llm>` echo provider, which makes the run deterministic (exit 0 + the WIN-01 warning). It is copied into a scratch directory so the settings resolution chain reaches the per-user global config instead of `examples/appsettings/appsettings.json`. |
| `rag-corpus/*.md` | Two short documents to ingest and query. |
| `rag-settings.json` | Points the RAG document store at SQLite. The default in-memory store dies with the `rag ingest` process, so a two-process ingest-then-search would always answer "no relevant context". No `Llm` section, on purpose. |

### The steps

1. **Install** — `Expand-Archive` + `install.ps1` (Windows) / `apt-get install ./orkeon_*.deb` (Debian) / `tar -xzf` + `./install.sh --prefix ~/.local` (tar.gz). The apt step doubles as the check that the package's `Depends` resolve on a stock image, with no dotnet repository.
2. **Payload** — `esbuild(.exe)`, `LocalEmbeddingsModel/default/{model.onnx,vocab.txt}` and the **7 whitelisted tree-sitter grammars** (WIN-04 pruning) must all be present. The grammar suffix follows the platform: `.dll`, `.so` or `.dylib`.
3. **Fresh session** — Windows: the user `PATH` is re-read from the registry and `orkeon` must resolve from it alone, plus the Add/Remove Programs entry must be registered (WIN-05). tar.gz: `<prefix>/bin/orkeon` must be a symlink into `<prefix>/lib/orkeon`, and `command -v orkeon` must find it once `<prefix>/bin` is on the `PATH`.
4. **`orkeon init --provider none --force`** — writes `%APPDATA%\Orkeon` / `~/.config/Orkeon` (WIN-02).
5. **`orkeon doctor --json`** — no check may report `fail`, **and** `esbuild`, `local-embeddings` and `tree-sitter` must be `ok`. That second half is what gives the smoke teeth: doctor only *warns* when those are missing, so "no fail" alone would happily pass a stripped archive (WIN-03).
6. **`orkeon run offline-crew.yaml`** — exit 0 and the `orkeon init` warning on stderr (WIN-01).
7. **`orkeon rag ingest` + `orkeon rag search`** — exit 0 and at least one citation with a score. The *answer* is empty without an LLM; the citations are the retrieval evidence, and that is all that is asserted.
8. **Uninstall** — `install.ps1 -Uninstall` (install dir, ARP key and PATH entry all gone) / `apt-get remove -y orkeon` (`/usr/bin/orkeon` and the payload gone) / `install.sh --uninstall` (`<prefix>/lib/orkeon` and the launcher symlinks gone). In all three the user configuration must survive.

Each script leaves the machine as it found it: a pre-existing user config is
backed up and restored, one the smoke created is removed.

> `orkeon --version` and `orkeon --help` exit **1** (a pre-existing
> CommandLineParser behaviour), so `orkeon doctor` is the liveness probe in all
> three scripts. Do not add a `--version` smoke without accounting for that.

### macOS: what only this job can prove

`smoke-macos` is the only place the **Gatekeeper / code-signing** story gets
exercised. A native library that is unsigned, quarantined or malformed —
`libtree-sitter*.dylib`, the ONNX runtime, the bundled `esbuild` — is killed by
the OS at load time, not at packaging time. The failure therefore surfaces in
`doctor`, `run` or `rag`, and nowhere earlier in the pipeline.

### bash 3.2

`macos-latest` still ships bash 3.2 as `/bin/bash`. `lib/smoke-common.sh` and
`run-smoke-tarball.sh` stay inside that dialect: no `mapfile`/`readarray`, no
associative arrays, and never a bare `${#arr[@]}` on a possibly-empty array
(an "unbound variable" error under `set -u` before bash 4.4 — accumulators that
can legitimately stay empty are plain strings for that reason). Keep any new
assertion inside the same constraints.

### Usage

```powershell
# Windows — PowerShell 5.1 or 7.
.\scripts\smoke-onboarding\run-smoke.ps1 -ArchivePath .\artifacts\installers\orkeon-cli-0.9.2-beta-win-x64.zip
```

```bash
# Debian/Ubuntu — installs and removes the package (needs sudo).
./scripts/smoke-onboarding/run-smoke-deb.sh --deb artifacts/installers/orkeon_0.9.2~beta_amd64.deb

# Degraded local mode: no apt, smoke a binary you already have. The install,
# launcher and removal steps report SKIP; everything else runs for real.
./scripts/smoke-onboarding/run-smoke-deb.sh --orkeon /usr/lib/orkeon/orkeon

# tar.gz — macOS in CI, but it runs identically on Linux against the linux-x64
# CLI archive, which is the supported local-development mode.
./scripts/smoke-onboarding/run-smoke-tarball.sh \
  --tarball artifacts/installers/orkeon-cli-0.9.2-beta-linux-x64.tar.gz

# ...installing somewhere other than ~/.local (the default):
./scripts/smoke-onboarding/run-smoke-tarball.sh --tarball <archive> --prefix /tmp/orkeon-smoke-prefix
```

`run-smoke-tarball.sh` **refuses to start** when `<prefix>/lib/orkeon` already
exists: it installs and then uninstalls, which would destroy an install it did
not create. Use `--prefix` for a scratch location, or `--force` if you really
mean it.

Both exit `0` when every non-skipped step passed, `1` otherwise, and print a
per-step summary. A failed Windows run keeps its scratch directory: the
per-step `.out`/`.err` captures under `logs\` are the only forensics left once
the runner is gone.
