> 🇫🇷 [Version française](../fr/getting-started/run-your-first-example.md)

# Run your first example (from source)

> **See also**: [Three ways to run Orkeon](./three-ways-to-run-orkeon.md) · [Overview](./overview.md) · [Bootstrap and execution](./bootstrap.md) · [Back to the index](../INDEX.md)

This is the entry point if you have cloned the repository and want to run a bundled
crew from source. In about five minutes you will run a real 3-agent example and get a
synthesis report back. If you would rather download a prebuilt binary or use a
container, see [Three ways to run Orkeon](./three-ways-to-run-orkeon.md).

> **Fastest path (no build, no clone)**:
> `docker run -it --rm -e ORKEON_RUNNER=shell ghcr.io/orkeon/orkeon-runners`
> then `orkeon-example run 1` — details in
> [Three ways to run Orkeon §3](./three-ways-to-run-orkeon.md#3-container).

## Prerequisites

| Requirement | Notes |
|---|---|
| **.NET SDK ≥ 10.0.300** | The repository pins the SDK in `global.json` with `rollForward: latestFeature`. An older SDK fails the build (see [Troubleshooting](#troubleshooting)). Verify with `dotnet --version`. |
| **Git** | To clone the repository. |
| **An LLM endpoint + key** | Any of the 14 supported providers, or a local endpoint such as Docker Model Runner / Ollama. Supplied through an `appsettings` profile (below). |

## 1. Clone and build

```bash
git clone https://github.com/Orkeon/orkeon.git
cd orkeon
dotnet build Orkeon.sln
```

## 2. Pick an example

Every crew under `examples/` is described by a `config.yaml`. Browse the
[catalog of examples](../reference/examples-catalog.md), or just start with the
research assistant — a sequential 3-agent crew that scrapes the web, analyses
documents, and writes a cited synthesis report:

```
examples/01-enterprise/01-research-assistant/config.yaml
```

## 3. Choose an LLM profile

Runners read their LLM configuration (endpoint, model, API key) from an
`appsettings.json`. The repository ships a **profile matrix** under
`examples/appsettings/` so you don't have to hand-write one:

| File | Target |
|---|---|
| `appsettings.json` | Default — [Docker Model Runner](https://docs.docker.com/desktop/features/model-runner/) at `localhost:12434` (no API key) |
| `appsettings.docker-model-runner.local.json.example` | Docker Model Runner template |
| `appsettings.deepseek.local.json.example` | DeepSeek cloud |
| `appsettings.openai.local.json.example` | OpenAI cloud |
| `appsettings.glm.local.json.example` / `appsettings.glm-medium.local.json.example` | Z.AI (GLM) |
| `appsettings.gemini.local.json.example` | Google Gemini |
| `appsettings.local.json.example` | Blank template to fill in |

Copy the template that matches your provider, drop it to a real `*.local.json`
(git-ignored), and paste your key:

```bash
cp examples/appsettings/appsettings.deepseek.local.json.example \
   examples/appsettings/appsettings.deepseek.local.json
# then edit the file and set your API key
```

> The `${DEEPSEEK_API_KEY}`-style placeholders are **not** expanded by .NET
> configuration — replace them with the literal key, or leave the file as-is
> and override via environment variable instead:
> `export ORKEON_Llm__ApiKey=sk-...` (prefix `ORKEON_`, `__` as section
> separator).

> If you have Docker Model Runner (or another `localhost:12434` endpoint)
> running, the default `examples/appsettings/appsettings.json` needs no key and
> no copy — skip straight to step 4 without `--settings`.

There is no `Provider` field to set: the provider is auto-detected from the
`Llm.BaseUrl` host in the profile, so switching providers is just a matter of
pointing at the right profile.

## 4. Run it

Every non-finance example runs through the **`orkeon` CLI**. From a source
checkout, invoke it via its project (no install needed — it also picks up your
local code changes):

```bash
mkdir -p out
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run \
  examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount ./out:/output:rw \
  -v 1
```

That single command runs the crew end-to-end and exits. The writer agent's
`file_write` tool lands its report on the `/output` mount, i.e. your local
`./out` directory.

> **Installed the CLI?** With a [release archive](./three-ways-to-run-orkeon.md)
> or `dotnet tool install`, the same run is just:
> `orkeon run examples/01-enterprise/01-research-assistant/config.yaml --settings … --mount ./out:/output:rw -v 1`.

## Every flag, explained

The `orkeon` CLI options you will actually reach for:

| Flag | Short | What it does |
|---|---|---|
| `<config>` (positional) | — | **Required.** The crew definition passed to `orkeon run <config>` — a `.yaml` file or an `.ork.ts` [scripting](../architecture/scripting.md) file. |
| `--settings <path>` | `-s` | Path to the `appsettings.json` holding LLM config. Optional — see [settings resolution](#how-settings-are-resolved). |
| `--verbose <0-2>` | `-v` | Verbosity. `0` (default) = quiet, `1` = LLM & tool exchanges, `2` = full debug. |
| `--mount <phys>:<virt>:<rights>` | `-m` | Expose a host directory to the crew's virtual file system. `rights` is `ro` or `rw`. Several mounts go **space-separated after a single flag** (`--mount a:/x:ro b:/y:rw`) — the parser rejects a repeated `--mount`. A crew that writes results needs a `:rw` mount (`/output` is the convention that triggers the auto-summary writer). |
| `--allow-external-mounts` | | Permit mounts (and a `--config` / `--llm-log-path`) located **outside** the current working directory. Without it, external paths are refused as a safety guard. The env var `ORKEON_ALLOW_EXTERNAL_MOUNTS=1` enables it for every invocation (the `orkeon-runners` container image bakes this in). |
| `--var KEY=VALUE` | `-V` | Inject a variable into the crew input. Task descriptions that contain `{KEY}` are expanded to `VALUE`. Several variables go space-separated after a single `-V` (a repeated flag is rejected). **YAML crews only** — ignored for `.ork.ts` scripts, which take `--inputs`. |
| `--initial-context <text>` | | A free-form context string passed to the crew input. **YAML crews only** — ignored for `.ork.ts` scripts. |
| `--inputs <json>` | | Inline JSON inputs forwarded to a script as the global `inputs` variable (`.ork.ts` path). |
| `--inputs-file <path>` | | Same as `--inputs`, read from a JSON file. |
| `--llm-log` | | Capture every LLM HTTP exchange (request + response, headers + payload) as `.jsonl` under `./llm-logs`. |
| `--llm-log-path <dir>` | | Same as `--llm-log`, but writes to `<dir>` (and implies `--llm-log`). |
| `--validate` | | Dry-run: resolve settings, build the host and load the crew (strict tool resolution) **without** probing the LLM or running anything. Prints `VALIDATION OK/FAILED` and exits 0 / non-zero. |
| `--list-tools` | | Build the host and print the sorted registered tool names, one per line, then exit — no crew required. |
| `--events jsonl` | | Emit the versioned JSONL event protocol on stdout instead of plain text (how Orkeon Studio drives a run) — see [the run event bus](../architecture/run-event-bus.md). |
| `--stream` | | With `--events`, also emit token-by-token `llm.delta` events (verbose by nature; off unless asked). |
| `--client <name>` | | With `--events`, the observing peer's hub name (`client://<name>`, default `studio`). |
| `--memory-limit-mb <n>` | | Jint memory ceiling for a `.ork.ts` run (overrides appsettings; `0` disables it). |

### Mounts and the VFS

Orkeon framework code never touches the disk directly — all I/O goes through the
[virtual file system](../architecture/vfs-compliance.md). `--mount` is how you
bridge a host directory into that virtual space:

```
--mount ./out:/output:rw        # host ./out  ->  virtual /output  (read-write)
--mount ./data:/data:ro         # host ./data ->  virtual /data    (read-only)
```

The runner automatically mounts the config's own directory read-only, **under the
name `/crew`**, so the YAML and any sibling data files are always visible — a
`data.csv` next to `config.yaml` is read as `/crew/data.csv`, never by its path on
your disk ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)). Paths
outside the working directory require `--allow-external-mounts` (or `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`
in the environment — the container image's default).

### How settings are resolved

When you omit `--settings`, the runner looks for an `appsettings.json` in this
order (first hit wins):

1. The explicit `--settings <path>`, if given.
2. `appsettings.json` sitting next to the `--config` file.
3. Walking up the directory tree from the config, looking for an
   `appsettings/appsettings.json` sub-directory at each level — that is how the
   shared `examples/appsettings/appsettings.json` profile matrix is found
   (the legacy `_shared/appsettings.json` remains a fallback for one release).
4. The global per-user config written by `orkeon init`
   (`%APPDATA%\Orkeon\appsettings.json` on Windows,
   `~/.config/Orkeon/appsettings.json` elsewhere).

If none is found, the runner falls back to environment variables only and prints
a warning. Being explicit with `--settings` is the most predictable option.

## Troubleshooting

Symptoms you may hit on a fresh machine, with the exact message and fix:

| Symptom / message | Cause | Fix |
|---|---|---|
| `error CS9057: analyzer assembly ... references version 5.3.0 of the compiler` | .NET SDK older than 10.0.300 | Install SDK ≥ 10.0.300 (the version pinned in `global.json`). Check with `dotnet --version`. |
| `error CS8795: Partial method ... must have an implementation part` | Source generator did not run — usually the same stale-SDK situation as above | Upgrade the SDK to ≥ 10.0.300 and rebuild. |
| `error NU1008: Projects that use central package version management should not define the version` | A stray `Version=` on a `PackageReference` while Central Package Management is on | Remove the inline version; declare it in `Directory.Packages.props`. |
| `dotnet: command not found` (in a script, though `dotnet` works interactively) | `dotnet` is a shell alias/function not visible to non-interactive shells | Put the SDK on `PATH` in `~/.zprofile` / `~/.profile`, e.g. `export PATH="$HOME/.dotnet:$PATH"`. |
| `Connection refused (localhost:12434)` | The default profile targets Docker Model Runner, which isn't running | Start Docker Model Runner, or copy a cloud profile (e.g. `appsettings.deepseek.local.json`) and pass it with `--settings`. |
| `401 (Unauthorized)` when restoring from GitHub Packages | `gh` token lacks the `read:packages` scope, or you used a fine-grained PAT | Use a **classic** PAT with `read:packages` (fine-grained tokens are not supported). Test: `curl -u <user>:$TOKEN https://nuget.pkg.github.com/Orkeon/orkeon.hosting/index.json` must return `200`. |
| `ERROR: --allow-external-mounts is required ...` | Your `--config`, a `--mount`, or `--llm-log-path` points outside the working directory | Add `--allow-external-mounts` (or set `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`), or move the paths under the cwd. |
| `WARNING: No appsettings.json found. Using environment variables only.` | Settings resolution found nothing | Pass `--settings <path>` explicitly (see [resolution order](#how-settings-are-resolved)). |

## Next steps

- [Three ways to run Orkeon](./three-ways-to-run-orkeon.md) — binaries and containers, no source checkout.
- [YAML, Builders and CrewFactory](./yaml-and-builders.md) — the schema behind every `config.yaml`.
- [Catalog of examples](../reference/examples-catalog.md) — 100+ crews across 9 domains.
- [Tool inventory](../tools/inventory.md) — what the agents can actually do.
