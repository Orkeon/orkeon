> 🇫🇷 [Version française](../fr/reference/cli.md)

# `orkeon` CLI reference

The `orkeon` command-line tool is the main entry point of the framework: it runs YAML crews and TypeScript scripts (`.ork.ts`), scaffolds a configuration, probes LLM providers, drives the RAG subsystem, searches the example use cases, and diagnoses an installation. It is built from `src/scripting/Orkeon.Scripting.Cli` and packs as the dotnet tool `orkeon`:

```bash
dotnet tool install --global Orkeon.Scripting.Cli --prerelease
orkeon doctor
```

The release binaries and installers (Windows zip/MSI, Debian package, macOS tarballs) also ship the same CLI, self-contained — no .NET SDK required. See [Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md).

**Exit codes** (stable): `0` OK · `1` script/config error (missing file, invalid script, validation failure) · `2` the run failed — an unexpected runtime error, a service the host could not build at kickoff, or a crew that ran and did not succeed (a task without a final answer, a tripped circuit breaker, a consensus not reached) · `130` cancelled with Ctrl+C. On exit `2` the **last stderr line** is `ERROR: <reason>` — the sentence that says why; the exception type and its stack trace are logged only at `--verbose 2` (or `ORKEON_DEBUG=1`).

## `orkeon run`

```bash
orkeon run <crew.ork.ts | crew.yaml | crew-directory/> [options]
```

Runs a crew definition and prints its result on stdout. Dispatch is by target type: `.ork.ts`/`.js` goes to the scripting host (esbuild transpile + Jint), `.yaml`/`.yml` — or a directory holding a multi-file YAML crew (`config.yaml` + `agents/` + `tasks/`, or the flat `crew.yaml`/`agents.yaml`/`tasks.yaml` triplet) — goes to the shared one-shot YAML runner.

| Option | Description |
|---|---|
| `-s, --settings <path>` | Path to `appsettings.json`. Without it, a fallback chain applies (below). |
| `-m, --mount <spec>` | VFS mount, Docker-style `<physical>:<virtual>:<rights>[;sub:rights]`. Several mounts go **space-separated after one flag** (`--mount a:/x:ro b:/y:rw`) — the parser rejects a repeated `--mount`. A Windows drive letter needs nothing special (`C:\src:/workspace:ro`); a path the bare form cannot carry — one containing a `:` or a `;`, or ending with a backslash — is **quoted**: `"/data/odd:name":/data:ro`, `"C:\src\":/workspace:ro`. Those double quotes belong to the *mount* grammar, so your shell must not eat them: write the whole spec inside single quotes in bash/zsh (`--mount '"/data/odd:name":/data:ro'`) and double the quotes in PowerShell (`--mount '""/data/odd:name"":/data:ro'`). Backslashes are never escape characters. The virtual path is always a name starting with `/` — never a disk path ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)), and `/crew`, `/script`, `/llm-logs` and `/sandbox` are reserved by the runner (`RunnerVirtualRoots.All`): a mount claiming one is refused with exit 1, and refused the same way whether it was written here or declared in the settings file — the guard reads both, and names the roots *this* command reserves rather than a fixed list. `orkeon forge` reserves only `/sandbox`: it takes no `--mount`, mounts `/workspace`, `/forge` and `/output` itself, and places those three against the settings exactly as a `--mount` is placed (next sentence) — a settings entry on one of them is replaced for the trial, so a settings file naming `/output`, an ordinary mount for a normal run and the name Studio gives a team's write folder, forges unchanged. **Against the settings file, a `--mount` is placed by virtual root**: on a root `Orkeon:FileSystem:Mounts` already declares (settings file or `ORKEON_` environment), the `--mount` **replaces every settings entry of that root for this run** — it is written at the first entry's own index, the others are withdrawn, and the log says `mount /x: --mount replaces the settings entry`; on a new root it is **appended** after every declared entry. Settings entries no `--mount` names stay in force. A root is a duplicate only when one *source* claims it twice and nothing can tell the claims apart: two `--mount` on the same root (`--mount a:/x:ro b:/x:rw`), or a settings file declaring one root twice with an entry that carries no id (next row), are refused with exit 1 and one line (`ERROR: '/x' is mounted twice on the command line: … Keep one.` / `… declared twice in <settings> (…) and '<entry>' has no id. Give every entry an id …`) before any host is built. |
| `--mount-id <ulid>` | Selects, among several settings entries declaring **one virtual root**, the entry this run keeps (VFS-90). A settings entry may carry an id — the 26-character ULID before its `|`, `01J9Z3K4M5N6P7Q8R9S0T1V2W3|C:\data:/output:rw`; Orkeon Studio writes one on every save — and two entries may share a root only when both carry one. Several ids go **space-separated after one flag**, like `--mount`. The entry named is kept as declared (folder, rights); the other entries of its root are **withdrawn for the run** — not mounted, not whitelisted, their folder not probed. Without the option, the crew's `mounts:` block (`<ulid>|/output`, see the [YAML schema](../architecture/yaml-schema.md)) selects the same way; with neither, a root declared several times is refused with exit 1 and one line naming every id (`ERROR: '/output' is declared twice in <settings> (<idA>: <folderA>, <idB>: <folderB>) and nothing selects one. Pass --mount-id <id>, list '<id>|/output' under mounts: in the crew, or pass --mount <folder>:/output:rw to replace them all.`). An id no entry carries, or a malformed one, is refused the same way; a `--mount` on the same root wins over the option, with a `WARNING:` line; a root the crew requires that nothing provides is refused too (`the crew requires '/output' … pass --mount <folder>:/output:rw`). Agents never see an id: `list_mounts` and the access-denied messages name virtual paths only. |
| `--allow-external-mounts` | Allow `--mount` arguments outside the working directory (or `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`): each `--mount` base path is added to the `PathSecurity:AdditionalAllowedDirectories` whitelist `PathValidator` checks resolved paths against, after whatever the settings already list there. A mount **declared in the settings file** (or through `ORKEON_` variables) needs no flag: its base path is always whitelisted, because a declared folder is the machine owner's explicit intent — until then such a folder was mounted and every access to it refused as "outside the allowed workspace directory", and no flag could rescue it. |
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
orkeon forge resume <slug> --read <dir>        # try it on the documents in <dir>
orkeon forge resume <slug> --adopt             # keep the team as generated, without a trial
orkeon forge promote <slug> --to <dir> --name <team>   # ship a ready session as an ordinary folder, under the team's name
orkeon forge reopen <team-folder>              # find — or rebuild from crew/ — the session linked to a promoted team
orkeon forge schedule <team-folder>            # install the team's schedule with the system's own scheduler
orkeon forge schedule <team-folder> --check    # say whether it is installed, absent or to reinstall
orkeon forge unschedule <team-folder>          # remove it, with schedule/ and the forge.json record
```

The Atelier: a guided path from a need in plain words to a deployable crew. An assistant interviews you and captures a structured brief — goal, inputs, **acceptance criteria**, a sample input — then proposes a team plan, renders it, validates it, **tries it in a sandbox on your sample**, and judges the result **against your own criteria**. Not conforming? The diagnosis feeds a refine loop, bounded by a hard budget (iterations, tokens, wall time). Every session lives under `.orkeon/forge/<slug>/` — resumable, diffable between attempts, auditable.

Starting or resuming a cycle requires a configured LLM (`orkeon init`): the forge refuses to open the interview without one (`FORGE-LLM-UNAVAILABLE`) rather than degrade silently. `list`, `promote`, `reopen`, `schedule` and `unschedule` are fully offline.

`reopen <team-folder>` makes a promoted team modifiable again, whatever became of its session. One rule decides which session a folder is linked to (rule R): every session carries a stable id — announced on `session.started` — that its promotion copies into the folder's `forge.json`, and the session carrying that id is linked when its `promotedTo` names the folder, or when the folder it names is gone or no longer carries the id: the team was moved or renamed, and the session is pointed at its new place. When `promotedTo` names another existing folder carrying the same id, this folder is a **copy**, linked to nothing; a folder without an id, or with one no session carries, is linked to nothing either. A linked session is only named: resume it. Otherwise — the session deleted, the folder forged on another machine or imported, a copy, a folder without an id — a session is **rebuilt** from the folder itself: the plan is read back from `crew/` (per-entity `config.yaml` + `agents/` + `tasks/`, or a single `crew.yaml`), the brief comes from the `forge.json` every promotion writes next to `FORGE.md` — or is derived from the plan, and the command says so — and the crew is copied verbatim. The rebuilt session gets a new id, written into the folder's `forge.json` (created without a brief when the folder had none; a copy's is rewritten, so it can never reach its original's session, and its session is named after the copy's own folder), and lands at the `--dry` pause, pointing back at the folder: the next `reopen` finds it, `resume --edit --dry`, `resume`, `resume --adopt` follow as usual, and a `promote --to` onto the same folder updates it in place. A folder with no YAML crew (a script crew, a foreign layout, files that do not describe a valid plan) is refused with `FORGE-TEAM-UNREADABLE` and the reasons; the verb takes no option but `--events`. This is how Orkeon Studio's « Modify » works, on every team.

| Option | Description |
|---|---|
| `--format yaml\|script` | Rendered format (default `yaml`). `script` renders an editable `crew.ork.ts` and needs esbuild — absent, a new session falls back to YAML with `FORGE-ESBUILD-MISSING`. A session's format never changes on resume. |
| `--events jsonl` | Emit the versioned event protocol on stdout instead of the terminal rendering; answers go down stdin (this is how Orkeon Studio drives the forge). |
| `--auto` | Arbitrate non-conforming verdicts without a human, within the budget. |
| `--dry` | Stop after validation — generate and validate, never execute. Resume without `--dry` to try it. |
| `--edit` | *(resume)* Amend the blueprint of a session paused before its trial: the amended JSON goes down the channel (`blueprint.edited` on stdin in `--events` mode, one pasted line in the terminal), is validated in full, then re-rendered deterministically — zero LLM tokens, same iteration. With `--dry`, the session pauses again at the same boundary. At the arbitration, use the `edit` decision instead. |
| `--adopt` | *(resume)* Take the team as generated, without running a trial: a session paused by `--dry` goes straight to Ready. Fully offline — no host, no LLM, no run directory, zero tokens. It skips the **evidence** a trial produces, never a check: the crew is rendered and validated at that pause, and promotion never consumed a trial artefact (`verdict.json` is optional and `FORGE.md` says «no verdict recorded»). Refused anywhere else, with `FORGE-INVALID-STATE`. |
| `--max-iterations <n>` / `--max-tokens <n>` / `--max-seconds <n>` | The budget (default 3 iterations; `0` = unlimited tokens/time). Resuming may raise it; consumption always carries over. |
| `--settings <path>` | Same semantics as `orkeon run` — **long form only**: the forge parser is bespoke and defines no short aliases. |
| `--read <dir>` | *(new session, resume)* The folder the trial reads as `/workspace`, in place of the working directory. The working directory keeps every other role — the session still lives under its `.orkeon/forge/<slug>/`, the settings still resolve next to it: `--read` moves the documents, not the atelier. A folder that does not exist is refused with exit 1 before any session is created (`--read names no directory`); `promote` refuses the option, since it mounts nothing. A read folder outside the working directory is whitelisted for the file tools automatically, the way `orkeon run` whitelists its script directory — the forge's mounts are its own three roots, so there is no `--allow-external-mounts` here. This is how Orkeon Studio tries a team on the folder chosen at its first step. |
| `--pack <dir>` | Override the embedded prompt pack. |
| `--to <dir>` | *(promote)* Destination folder; must not exist or be empty — unless it is the folder the session is linked to (where it promoted to, or that folder moved or renamed since; never a copy of it, which is refused with the reason), which is then updated in place. |
| `--name <team>` | *(promote)* The team's name: it titles `FORGE.md`, `forge.json` and the session itself — without it they keep the brief's goal. Taken as written, a leading dash included: the value of `--name` is never read as the next option. Orkeon Studio always passes it. |
| `--schedule daily@HH:mm\|hourly` | *(promote)* Generate the schedule artifacts under `schedule/` — Windows task XML, systemd timer, cron line — and record the schedule in `forge.json`. The promotion itself installs nothing: `forge schedule` does (below). |
| `--check` | *(schedule)* Say where the schedule stands — `installed`, `absent` or `stale` — and change nothing. |
| `--with-settings` | *(promote)* Copy the resolved settings file into the folder. Off by default — a settings file usually carries API keys and the folder is made to be shared. |

The sandbox: the try runs in-process with writes confined to the session's own directory (`/output` for deliverables, `/forge` for its working files), the working directory — or the `--read` folder — mounted read-only as `/workspace`, and `shell_command`/`code_interpreter` removed from the tool catalogue — the team plan can only name tools the validation will accept.

The promoted folder is ordinary: `crew/` (or `crew/crew.ork.ts`), `run.sh`/`run.cmd` composed against the `orkeon run` grammar with your sample inputs pre-filled, `FORGE.md` — the crew's identity card (goal, acceptance criteria, verdict, version), written in the interview's language — and `forge.json`, its machine-readable twin (the session's id, slug, title, format, promotion instant, brief) that `forge reopen` reads — the id is what links the folder back to its session wherever the folder goes. `orkeon run <dir>/crew` launches it — from inside `<dir>`, and without the `--mount` arguments `run.sh` supplies, so a team that writes deliverables writes nothing that way; the Studio launcher detects the folder and lays the mounts itself.

Once the promotion is written, the session follows its team: its folder under `.orkeon/forge/` takes the destination folder's name, as it is — suffixed `-2`, `-3`… when another session already has that name, which is never overwritten — and `session.json` and `forge.json` take the new slug, so `forge list` shows each adopted session under the name of the team it made rather than the need it was opened with. `--events` announces it: `session.renamed` carries `from`, `to`, the new `dir` and `suffixed`. A rename the disk refuses (a handle held open on Windows, an antivirus) leaves the promotion successful — exit 0 — with a `warning` event (`FORGE-SESSION-NOT-RENAMED`): the team stays linked to its session by the id either way. The generated artifacts carry the team folder's name too, never the session's: `schedule/orkeon-<team>.service` and `.timer` with their descriptions, the scheduled task `Orkeon <team>`, the install command, the launchers' header. `<team>` is the folder's name as the folder rule spells it — lowercase ASCII and dashes, what a unit name can carry — which is the name itself for every folder Studio creates.

**The schedule.** Orkeon has no scheduler of its own: the operating system runs a scheduled team, and the CLI — which owns the artifacts — installs and removes it there. `promote --schedule` generates `schedule/` (the three families, whatever the machine) and records the declared schedule in `forge.json`; it never touches the system's scheduler. `forge schedule <team-folder>` registers it for the current user, without elevation and without a password:

- **Windows** — `schtasks /Create /TN "Orkeon <team>" /XML schedule/windows-task.xml /F`. The task runs while the user is logged on; a run missed while the machine was off or on battery happens when it next can.
- **Linux** — the two units are copied into `~/.config/systemd/user/` (`$XDG_CONFIG_HOME` honoured), then `systemctl --user daemon-reload` and `enable --now orkeon-<team>.timer`. A user timer runs while the user's systemd instance does — from login to logout, or always once `loginctl enable-linger` is set.
- **macOS and other systems** — one line of the user's crontab, marked `# orkeon:<team>`, read with `crontab -l` and written back whole with `crontab -`: every other line stays exactly as it was, and nothing is ever written over a crontab that could not be read. launchd is not used.

Every command is a program and its list of arguments, never a shell. Installing again reinstalls. Artifacts that describe another folder — a copy, a team moved or renamed since its promotion — are regenerated first, and a registration this folder made under a former name is removed. What was installed is recorded in `forge.json` (`schedule.installed`: expression, family, names, folder, date); the check and the removal act on those recorded names, never on names they would compute. `--check` changes nothing and answers `installed`, `absent` (never installed, removed outside Orkeon, made on another system — or the folder is a copy, and the recorded registration is its original's) or `stale`, to reinstall (it runs another path, carries another name, fires on another schedule than the declared one, or is disabled). `forge unschedule <team-folder>` removes this folder's registration, `schedule/` and the `forge.json` record; nothing to remove is a success, and a copy never removes its original's registration. A name another team folder already holds is never replaced (`FORGE-SCHEDULE-NAME-TAKEN`); a folder that declares no schedule has nothing to install (`FORGE-SCHEDULE-NONE`). When the system refuses — a policy, no user session bus, no `crontab` — the verb exits 1 with `FORGE-SCHEDULE-REFUSED`, and the `error` event carries `command`: what to run by hand instead. With `--events` the outcome is one `schedule.state` event (`path`, `state`, `reason`, `expression`, `family`, `names`, and `removed` for a removal). A re-adoption that drops the schedule leaves the registration running and says so with a `warning` (`FORGE-SCHEDULE-STILL-INSTALLED`): only `unschedule` removes it. Both verbs take no option but `--events` (and `--check` for `schedule`). The launchers call `orkeon` by name, and cron and systemd give a scheduled run a minimal `PATH`: a CLI installed with `dotnet tool install` (under `~/.dotnet/tools`) is not found there, the `.deb` package's `/usr/bin/orkeon` is. This is how Orkeon Studio installs, checks and stops a team's schedule.

## `orkeon usecases`

```bash
orkeon usecases search "summarize my emails every morning"          # the closest use cases
orkeon usecases search "je veux un résumé de mes mails chaque matin" --top 3
orkeon usecases list --category finance-trading --process parallel  # the catalogue, filtered
orkeon usecases show 03-email-pipeline --crew                        # one sheet, and its crew file
```

The catalogue of the example use cases: the 105 numbered examples of `examples/`, each described by a sheet written in five languages ([usecases.json](../../examples/usecases.json)). The tool carries the catalogue itself — the manifest, each example's crew file and its `data/` folder — so all three subcommands work offline, read nothing from disk, and call no LLM. The finance examples are **reference only**: searchable and readable, not importable, since their crews depend on a shared `_tools/` folder the tool does not carry.

**`search <text>`** ranks the catalogue against a need written in plain words, in French, English, Spanish, German or Simplified Chinese.

- **By terms**: BM25 over each sheet's title and problem in the five languages, its tags, its tools and its category. The query and the sheets are normalized alike — lowercase, accents folded (`resume` finds `résumé`), Chinese cut into character bigrams — so a query matches whatever language it is typed in.
- **By meaning**: the local embedding model (BGE-micro-v2, on-device) compares the query with each sheet's English text, and its ranking is fused with the terms' by RRF. The model reads English only (see [Known limitations](./limitations.md)), so meaning is fused in only for the languages where the golden set ([usecases.golden.yaml](../../examples/usecases.golden.yaml)) measured a gain: English today (measured 2026-09-24 on the five-language texts — terms alone reach recall@5 1.00 in every language, and meaning helps the ranking in English only); French, Spanish, German and Chinese are searched by terms. The model loads at the first search that needs it — about a second — and each later search takes a few milliseconds.
- **Without the model** (its files belong in `LocalEmbeddingsModel/default/` next to the binary; `orkeon doctor` checks them), the search runs by terms and every answer says so. It never degrades silently.

| Option | Description |
|---|---|
| `--top <n>` | Number of use cases to answer with (default 5). |
| `--lang fr\|en\|es\|de\|zh-Hans` | The query's language (`zh` is accepted for `zh-Hans`). Omitted, it is read from the text — function words, accented letters, Chinese characters — and keywords that give nothing away count as English. It picks the search mode and the titles shown. |
| `--events jsonl` | Answer with one `usecases.results` line on stdout instead of the text rendering. **Without a text, session mode**: one query per stdin line, one answer per query, the model loaded once for all of them, and the end of stdin ends the process with exit 0. |

Each result carries the use case's `id`, its `rank`, its `score` (BM25 by terms, RRF in hybrid mode — comparable within one answer only), the `reason` it matched (`terms`, `meaning` or `terms+meaning`), the `terms` it matched on, its `similarity` to the query in hybrid mode, and its title in the query's language. The answer carries the `mode` it ran in (`bm25` or `hybrid`), the language and how it was settled (`langSource`: `option`, `detected` or `default`), and `degraded`, the reason, when meaning was given up.

Session mode is how Orkeon Studio suggests use cases while you type: the process opens with a `usecases.ready` line (catalogue size, languages, the mode of each), then answers every query with the query's `correlationId` in the envelope.

```text
→ {"kind":"usecases.query","correlationId":"q1","text":"relancer les factures impayées","lang":"fr","top":5}
← {"v":2,"seq":2,"ts":"…","kind":"usecases.results","correlationId":"q1","query":"relancer les factures impayées","lang":"fr","langSource":"option","mode":"bm25","results":[{"rank":1,"id":"40-invoice-processing","score":7.8412,"reason":"terms","terms":["factures"],"title":"…"}]}
```

A query without `top` or `lang` takes the command line's `--top` and `--lang`. A line that is not a query is skipped. A query that cannot run — no `text`, a `lang` outside the five, a `top` below 1 — is answered by an `error` line carrying its `correlationId` and the code `USECASES-QUERY-INVALID`, and the session goes on. The event kinds are declared once, in `Orkeon.Constants.Protocol.UseCaseEventKinds`.

**`list`** prints the catalogue: id, process, title, and the flags `data` (sample data), `web` (needs the network), `keys` (needs a third-party key) and `reference only`. The filters combine: `--category` (`03-finance-trading`, or `finance-trading`), `--process` (`sequential`, `hierarchical`, `parallel`, `consensual`, `graph`, `autonomous`), `--tag`. An unknown category or process is refused with the list of valid ones. `--lang` picks the titles (default `en`). `--events jsonl` emits one `usecases.catalog` line holding every sheet in full, under the manifest's field names.

**`show <id>`** prints one sheet: its category, process, agents and tasks, tools, tags, what it needs (network, keys), its mounts, whether it is importable, the files the tool carries for it, and its title and problem in every language written (`--lang` for one). `--crew` appends the crew file. `--events jsonl` emits a `usecases.sheet` line, with `crew` when asked. An unknown id exits 1 with `USECASES-UNKNOWN-ID` (an `error` line in `--events` mode).

Exit codes: `0` answered (an empty answer included), `1` refused (an unknown id, an invalid option), `2` unexpected error, `130` Ctrl+C.

## `orkeon init`

Configuration assistant. Generates a valid `appsettings.json` at the global per-user path (`%APPDATA%\Orkeon\appsettings.json` on Windows, `$XDG_CONFIG_HOME/Orkeon/appsettings.json` — else `~/.config/Orkeon/appsettings.json` — on Linux **and** macOS, which deliberately does not use `~/Library/Application Support`) from an interactive 5-choice wizard — `ollama`, `docker-model-runner`, `openai`, `custom`, `none` — or non-interactively via flags, then probes the endpoint (unless `--no-probe`).

| Option | Description |
|---|---|
| `-p, --provider <preset>` | `ollama` \| `docker-model-runner` \| `openai` \| `custom` \| `none`. |
| `-u, --base-url <url>` / `-m, --model <id>` | Endpoint and model. Required for `custom`; presets have defaults. |
| `-k, --api-key-env <name>` / `--api-key <value>` | The variable init's own probe reads — it is **not** written to the generated file, and init prints that the runtime reads `ORKEON_Llm__ApiKey` natively — or a key stored inline in clear text (discouraged). See [the variable per provider](./llm-providers-comparison.md). |
| `--path <file>` | Write somewhere other than the global per-user path. |
| `-f, --force` | Overwrite an existing file. |
| `--no-probe` | Skip the endpoint probe. |

```bash
orkeon init --provider ollama --model llama3.2 --no-probe
```

## `orkeon llm`

Two verbs against a live provider endpoint.

**`orkeon llm probe`** — exercises the LLM test protocol against a provider and optionally archives the campaign trace. Key options: `-p, --provider` (required: `openai | anthropic | ollama | azure | together | qwen | deepseek | kimi | mistral | huggingface | zai | gemini | grok | minimax | openrouter | mammouth`), `-m, --model`, `-u, --base-url` (required for Azure), `--api-version` (Azure deployment mode), `--workspace-id` (for workspace-scoped keys — Anthropic identity-linked keys require it), `-k, --api-key-env` (default `ORKEON_LLM_API_KEY` — the key itself is never accepted on the command line), `--modes` (comma-separated, e.g. `M1,M2,M8`; default all), `--archive <dir>`, `--format md|json`, `--commit`, `--timeout` (seconds, default 180), `--temperature` (default 0), `--thinking-effort` (base reasoning-effort hint, e.g. `none` — some models refuse function tools while reasoning), `--m7-effort` (effort the M7 thinking probe uses, default `low` — for models whose supported set excludes it).

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
— see [Orkeon Studio](../architecture/studio.md). That is the whole list. The
[publication matrix](./publication-matrix.md) and
[Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md) list exactly
which archive carries what.

---

> **See also**: [Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md) ·
> [Run your first example](../getting-started/run-your-first-example.md) ·
> [Back to index](../INDEX.md)
