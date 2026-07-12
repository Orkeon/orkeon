# Run your first example (from source)

> **See also**: [Three ways to run Orkeon](./three-ways-to-run-orkeon.md) · [Overview](./overview.md) · [Bootstrap and execution](./bootstrap.md) · [Back to the index](../INDEX.md)

This is the entry point if you have cloned the repository and want to run a bundled
crew from source. In about five minutes you will run a real 3-agent example and get a
synthesis report back. If you would rather download a prebuilt binary or use a
container, see [Three ways to run Orkeon](./three-ways-to-run-orkeon.md).

## Prerequisites

| Requirement | Notes |
|---|---|
| **.NET SDK ≥ 10.0.300** | The repository pins the SDK in [`global.json`](../../global.json) with `rollForward: latestFeature`. An older SDK fails the build (see [Troubleshooting](#troubleshooting)). Verify with `dotnet --version`. |
| **Git** | To clone the repository. |
| **An LLM endpoint + key** | Any of the 12 supported providers, or a local endpoint such as Docker Model Runner / Ollama. Supplied through an `appsettings` profile (below). |

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
>
> **Finance / trading examples** (`examples/03-finance-trading/*`) run on the
> specialized `orkeon-trading` runner instead — `orkeon-trading --config
> examples/03-finance-trading/<name>/config.yaml --settings …` — which adds the
> 44 trading tools the base CLI does not carry.

## Every flag, explained

The `orkeon` CLI and the specialized runners share the same base options. The
ones you will actually reach for:

| Flag | Short | What it does |
|---|---|---|
| `<config>` (positional) | — | **Required.** The crew definition passed to `orkeon run <config>` — a `.yaml` file or an `.ork.ts` [scripting](../architecture/scripting.md) file. The `orkeon-trading` runner takes it as `--config <path>` / `-c` instead. |
| `--settings <path>` | `-s` | Path to the `appsettings.json` holding LLM config. Optional — see [settings resolution](#how-settings-are-resolved). |
| `--verbose <0-2>` | `-v` | Verbosity. `0` (default) = quiet, `1` = LLM & tool exchanges, `2` = full debug. |
| `--mount <phys>:<virt>:<rights>` | `-m` | Expose a host directory to the crew's virtual file system. `rights` is `ro` or `rw`. Repeatable. A crew that writes results needs a `:rw` mount (`/output` is the convention that triggers the auto-summary writer). |
| `--allow-external-mounts` | | Permit mounts (and a `--config` / `--llm-log-path`) located **outside** the current working directory. Without it, external paths are refused as a safety guard. The env var `ORKEON_ALLOW_EXTERNAL_MOUNTS=1` enables it for every invocation (the `orkeon-runners` container image bakes this in). |
| `--var KEY=VALUE` | `-V` | Inject a variable into the crew input. Task descriptions that contain `{KEY}` are expanded to `VALUE`. Repeatable. |
| `--initial-context <text>` | | A free-form context string passed to the crew input. |
| `--llm-log` | | Capture every LLM HTTP exchange (request + response, headers + payload) as `.jsonl` under `./llm-logs`. |
| `--llm-log-path <dir>` | | Same as `--llm-log`, but writes to `<dir>` (and implies `--llm-log`). |

### Mounts and the VFS

Orkeon framework code never touches the disk directly — all I/O goes through the
[virtual file system](../architecture/vfs-compliance.md). `--mount` is how you
bridge a host directory into that virtual space:

```
--mount ./out:/output:rw        # host ./out  ->  virtual /output  (read-write)
--mount ./data:/data:ro         # host ./data ->  virtual /data    (read-only)
```

The runner automatically mounts the config's own directory read-only, so the
YAML and any sibling data files are always visible. Paths outside the working
directory require `--allow-external-mounts` (or `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`
in the environment — the container image's default).

### How settings are resolved

When you omit `--settings`, the runner looks for an `appsettings.json` in this
order (first hit wins):

1. The explicit `--settings <path>`, if given.
2. `appsettings.json` sitting next to the `--config` file.
3. Walking up the directory tree from the config: the shared
   `examples/appsettings/appsettings.json` profile matrix
   (the legacy `_shared/appsettings.json` remains a fallback for one release).

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
</content>
