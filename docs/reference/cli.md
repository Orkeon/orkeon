> 🇫🇷 [Version française](../fr/reference/cli.md)

# `orkeon` CLI reference

The `orkeon` command-line tool is the main entry point of the framework: it runs YAML crews and TypeScript scripts (`.ork.ts`), scaffolds a configuration, probes LLM providers, drives the RAG subsystem, and diagnoses an installation. It is built from `src/scripting/Orkeon.Scripting.Cli` and packs as the dotnet tool `orkeon`:

```bash
dotnet tool install --global Orkeon.Scripting.Cli --prerelease
orkeon doctor
```

The release binaries and installers (Windows zip/MSI, Debian package, macOS tarballs) also ship the same CLI, self-contained — no .NET SDK required. See [Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md).

**Exit codes** (stable): `0` OK · `1` script/config error (missing file, invalid script, validation failure) · `2` unexpected runtime error · `130` cancelled with Ctrl+C.

## `orkeon run`

```bash
orkeon run <crew.ork.ts | crew.yaml | crew-directory/> [options]
```

Runs a crew definition and prints its result on stdout. Dispatch is by target type: `.ork.ts`/`.js` goes to the scripting host (esbuild transpile + Jint), `.yaml`/`.yml` — or a directory holding a multi-file YAML crew (`config.yaml` + `agents/` + `tasks/`, or the flat `crew.yaml`/`agents.yaml`/`tasks.yaml` triplet) — goes to the shared one-shot YAML runner.

| Option | Description |
|---|---|
| `-s, --settings <path>` | Path to `appsettings.json`. Without it, a fallback chain applies (below). |
| `-m, --mount <spec>` | VFS mount, Docker-style `<physical>:<virtual>:<rights>[;sub:rights]`. Several mounts go **space-separated after one flag** (`--mount a:/x:ro b:/y:rw`) — the parser rejects a repeated `--mount`. |
| `--allow-external-mounts` | Allow mounts outside the workspace root (or `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`). |
| `-v, --verbose <0-2>` | `0` quiet, `1` LLM & tool exchanges, `2` full debug. |
| `--llm-log` / `--llm-log-path <dir>` | Log full LLM exchanges as JSONL (default directory `./llm-logs`). |
| `--inputs <json>` / `--inputs-file <path>` | Structured inputs for **scripts** (global `inputs` variable). |
| `-V, --var KEY=VALUE` | Variable for a **YAML crew**'s `CrewInput` (task templates `{KEY}`). Several variables go space-separated after one `-V` (a repeated flag is rejected). |
| `--initial-context <text>` | Initial context string for a **YAML crew**'s `CrewInput`. |
| `--memory-limit-mb <n>` | Jint memory limit override for this run (`0` disables it). |
| `--validate` | Dry run: resolve settings, build the host, load the crew with strict tool resolution — no LLM call, no kickoff. Prints `VALIDATION OK/FAILED: …`. |
| `--list-tools` | Build the host, print the sorted runtime tool registry, exit. No crew path needed. |
| `--events jsonl` | Emit the versioned event protocol on stdout instead of the plain rendering, and read commands on stdin. This is how Orkeon Studio watches a run. See [The run event bus](../architecture/run-event-bus.md). |
| `--stream` | With `--events`, also emit `llm.delta` events token by token. Verbose by nature: off unless asked for. |
| `--client <name>` | With `--events`, the name the watching process answers to on the run's hub (`client://<name>`, default `studio`). Agents can post and send to that address; a crew's `links:` block authorizes it. |

```bash
orkeon run examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount ./out:/output:rw -v 1
```

**Settings resolution** — when `--settings` is omitted, the CLI walks a fallback chain: `appsettings.json` next to the crew file, then an `appsettings/appsettings.json` found by walking up the parent directories, then the global per-user file written by `orkeon init`, then `ORKEON_*` environment variables alone. Details and the ready-made profile matrix: [Run your first example](../getting-started/run-your-first-example.md).

## `orkeon forge`

```bash
orkeon forge "summarize my supplier's new offers every morning"   # start from a need
orkeon forge                                   # start with the interview
orkeon forge list                              # list the workspace's sessions
orkeon forge resume <slug>                     # pick a session up exactly where it stopped
orkeon forge promote <slug> --to <dir>         # ship a ready session as an ordinary folder
```

The Atelier: a guided path from a need in plain words to a deployable crew. An assistant interviews you and captures a structured brief — goal, inputs, **acceptance criteria**, a sample input — then proposes a team plan, renders it, validates it, **tries it in a sandbox on your sample**, and judges the result **against your own criteria**. Not conforming? The diagnosis feeds a refine loop, bounded by a hard budget (iterations, tokens, wall time). Every session lives under `.orkeon/forge/<slug>/` — resumable, diffable between attempts, auditable.

Starting or resuming a cycle requires a configured LLM (`orkeon init`): the forge refuses to open the interview without one (`FORGE-LLM-UNAVAILABLE`) rather than degrade silently. `list` and `promote` are fully offline.

| Option | Description |
|---|---|
| `--format yaml\|script` | Rendered format (default `yaml`). `script` renders an editable `crew.ork.ts` and needs esbuild — absent, a new session falls back to YAML with `FORGE-ESBUILD-MISSING`. A session's format never changes on resume. |
| `--events jsonl` | Emit the versioned event protocol on stdout instead of the terminal rendering; answers go down stdin (this is how Orkeon Studio drives the forge). |
| `--auto` | Arbitrate non-conforming verdicts without a human, within the budget. |
| `--dry` | Stop after validation — generate and validate, never execute. Resume without `--dry` to try it. |
| `--edit` | *(resume)* Amend the blueprint of a session paused before its trial: the amended JSON goes down the channel (`blueprint.edited` on stdin in `--events` mode, one pasted line in the terminal), is validated in full, then re-rendered deterministically — zero LLM tokens, same iteration. With `--dry`, the session pauses again at the same boundary. At the arbitration, use the `edit` decision instead. |
| `--max-iterations <n>` / `--max-tokens <n>` / `--max-seconds <n>` | The budget (default 3 iterations; `0` = unlimited tokens/time). Resuming may raise it; consumption always carries over. |
| `--settings <path>` | Same semantics as `orkeon run` — **long form only**: the forge parser is bespoke and defines no short aliases. |
| `--pack <dir>` | Override the embedded prompt pack. |
| `--to <dir>` | *(promote)* Destination folder; must not exist or be empty. |
| `--schedule daily@HH:mm\|hourly` | *(promote)* Generate schedule artifacts under `schedule/` — Windows task XML, systemd timer, cron line. The install command is **displayed, never executed**: Orkeon has no scheduler. |
| `--with-settings` | *(promote)* Copy the resolved settings file into the folder. Off by default — a settings file usually carries API keys and the folder is made to be shared. |

The sandbox: the try runs in-process with writes confined to the session's `/output` mount, and `shell_command`/`code_interpreter` removed from the tool catalogue — the team plan can only name tools the validation will accept.

The promoted folder is ordinary: `crew/` (or `crew/crew.ork.ts`), `run.sh`/`run.cmd` composed against the `orkeon run` grammar with your sample inputs pre-filled, and `FORGE.md` — the crew's identity card (goal, acceptance criteria, verdict, version), written in the interview's language. `orkeon run <dir>/crew` launches it; the Studio launcher detects it.

## `orkeon init`

Configuration assistant. Generates a valid `appsettings.json` at the global per-user path (`%APPDATA%\Orkeon\appsettings.json` on Windows, `~/.config/Orkeon/appsettings.json` on Linux/macOS) from an interactive 5-choice wizard — `ollama`, `docker-model-runner`, `openai`, `custom`, `none` — or non-interactively via flags, then probes the endpoint (unless `--no-probe`).

| Option | Description |
|---|---|
| `-p, --provider <preset>` | `ollama` \| `docker-model-runner` \| `openai` \| `custom` \| `none`. |
| `-u, --base-url <url>` / `-m, --model <id>` | Endpoint and model. Required for `custom`; presets have defaults. |
| `-k, --api-key-env <name>` / `--api-key <value>` | Env var holding the key, or a key to store. |
| `--path <file>` | Write somewhere other than the global per-user path. |
| `-f, --force` | Overwrite an existing file. |
| `--no-probe` | Skip the endpoint probe. |

```bash
orkeon init --provider ollama --model llama3.2 --no-probe
```

## `orkeon llm`

Two verbs against a live provider endpoint.

**`orkeon llm probe`** — exercises the LLM test protocol against a provider and optionally archives the campaign trace. Key options: `-p, --provider` (required: `openai | anthropic | ollama | azure | groq | together | qwen | deepseek | kimi | mistral | huggingface | zai | gemini`), `-m, --model`, `-u, --base-url` (required for Azure), `--api-version` (Azure deployment mode), `-k, --api-key-env` (default `ORKEON_LLM_API_KEY` — the key itself is never accepted on the command line), `--modes` (comma-separated, e.g. `M1,M2,M8`; default all), `--archive <dir>`, `--format md|json`, `--commit`, `--timeout` (seconds, default 180), `--temperature` (default 0).

**`orkeon llm models`** — lists the models a provider currently serves. Options: `-p, --provider` (required), `-u, --base-url`, `-k, --api-key-env`, `-f, --filter` (shell-style glob), `--json`.

```bash
ORKEON_LLM_API_KEY=... orkeon llm probe -p deepseek --modes M1,M2 --format json
orkeon llm models -p ollama --filter 'llama*'
```

## `orkeon rag`

Three verbs over the RAG subsystem (`ingest`, `search`, `eval`). All share the host options of `run`: `-s/--settings`, `-m/--mount`, `--allow-external-mounts`, `-v/--verbose`. Relative sources resolve against an automatic `{cwd} → /workspace:ro` mount; state lands in `{cwd}/.orkeon → /output:rw`.

**`orkeon rag ingest`** — incremental ingestion (unchanged sources are skipped): `-c, --collection` (required), `--source <path|glob>` (required; several sources space-separated after one flag), `--chunking recursive|sentence|structural|semantic`, `--reindex` (full reindex — the only way past an embedding model/dimension change).

**`orkeon rag search`** — asks a question, prints the grounded answer with citations and scores: positional `<question>`, `-c, --collection` (required), `--top-n` (default 5).

**`orkeon rag eval`** — evaluates a collection against a golden dataset (recall@k, MRR, groundedness) and writes markdown/JSON reports: `-d, --dataset` (required), `-c, --collection`, `--profile fast|balanced|quality|adaptive|corrective|default` (default `default` = the configured `Orkeon:Rag:Profile`) or `--compare fast,balanced,…`, `-k` (default 5), `--llm-judge`, `--offline` (zero-network: deterministic extractive stub, no LLM key needed), `--no-ingest`, `--reindex`, `--min-recall` / `--min-mrr` (anti-regression gates, exit 1 below threshold), `--output` (default `/output/rag/eval`).

```bash
orkeon rag eval --dataset examples/rag/eval/golden.yaml \
  --compare fast,balanced,quality,corrective,adaptive --offline
```

## `orkeon doctor`

Installation diagnostic: says in under 15 seconds what works and what is missing, as a ✅/⚠️/❌ table or `--json` (stable `{check, status, detail}` schema for CI). Nine checks: `dotnet-runtime`, `appsettings`, `llm-config`, `llm-reachability`, `esbuild`, `local-embeddings`, `onnx-reranker`, `tree-sitter`, `workspace-write`. Exit codes: `0` all green or warnings only, `1` at least one failing check.

```bash
orkeon doctor --json
```

## `orkeon-repl` — the separate interactive console

`orkeon-repl` is a **different tool** built from `src/apps/Orkeon.ConsoleApp` (dotnet tool command `orkeon-repl`): a full interactive REPL that drives agents, crews and tools from a Terminal.Gui split-pane console (logs + REPL), with the full framework stack wired in — built-in tools, RAG, code analysis, local embeddings — and TypeScript-scripted commands. It deliberately does not share the `orkeon` assembly name. See [CLI TypeScript commands](../architecture/cli-ts-commands.md).

## The other shipped binaries

The release archives carry more launchers than the two documented here: `orkeon-slim`
(the same CLI, framework-dependent), **`orkeon-host`** (the long-running service daemon —
see [the service host](../architecture/service-host.md)), the two Studio TUIs
(`orkeon-studio-config`, `orkeon-studio-run`) and the Windows desktop app (`orkeon-studio`)
— see [Orkeon Studio](../architecture/studio.md) — plus the example runners. The
[publication matrix](./publication-matrix.md) and
[Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md) list exactly
which archive carries what.

---

> **See also**: [Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md) ·
> [Run your first example](../getting-started/run-your-first-example.md) ·
> [Back to index](../INDEX.md)
