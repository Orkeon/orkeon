> 🇫🇷 [Version française](../fr/architecture/studio.md)

# Orkeon Studio

Orkeon Studio is the family of graphical and terminal front-ends over the `orkeon` CLI workflows: edit and validate the very `appsettings.json` that `orkeon init` writes, then pick a crew and launch it by running the co-installed `orkeon` binary. It is a front-end, not a second product — anything Studio does can be done from the terminal, and anything it writes is readable by the CLI, so you can switch between the two at any point.

## The four projects

All four live under `src/apps/` and none is published on NuGet (`IsPackable=false` in every csproj — they ship through the release installers instead).

| Project | Target | Output | Role |
|---|---|---|---|
| `Orkeon.Studio.Core` | `net10.0` | library | UI-agnostic core: everything the screens render lives here. |
| `Orkeon.Studio.Config` | `net10.0` | exe (`orkeon-studio-config`) | Terminal.Gui v2 settings editor: typed section forms, VFS mount editor, LLM presets, validation, `orkeon doctor`. |
| `Orkeon.Studio.Run` | `net10.0` | exe (`orkeon-studio-run`) | Terminal.Gui v2 crew launcher: points the co-installed `orkeon` CLI at a crew and streams its output. |
| `Orkeon.Studio.Wpf` | `net10.0-windows` | WinExe, `AssemblyName=Orkeon.Studio` (`orkeon-studio`) | WPF desktop app combining both workflows in one window. |

### `Orkeon.Studio.Core` — the shared core

A library with no UI and no entry point, consumed only by the three front-ends. Its folders map to the behaviours:

- **Configuration/** — a lossless `appsettings.json` editing model (`AppSettingsDocument`) plus typed sections (`LlmSection`, `LlmLoggingSection`, `LoggingSection`, `MountsSection`, `RagSection`, `RateLimitingSection`) and `LlmProviderDetector`.
- **FileSystem/** — mount editing: `MountDefinition`, `MountRights`, `MountValidator`, directory browsing.
- **Presets/ & Llm/** — the LLM preset catalogue (`LlmPresets`, `OrkeonCliDefaults`) and endpoint probing (`ILlmEndpointProbe`/`HttpLlmEndpointProbe`, `LlmApiKeyResolver`).
- **Targets/** — run-target detection (`RunTargetDetector`): a `config.yaml`, a multi-file crew directory, or a `.ork.ts` script.
- **Launch/ & Process/** — building the `orkeon run` command line (`RunArgumentsBuilder`, `RunLaunchOptions`), locating the binary (`OrkeonBinaryLocator` — in order: the `--cli-dir` argument, next to the executable, the `ORKEON_CLI_DIR` environment variable, `PATH`, then the development checkout), running it and streaming output (`OrkeonProcessRunner`, `IProcessLauncher`), interpreting exit codes (`OrkeonExitCodes`, `LaunchOutcomeFormatter`), and the `orkeon doctor` report (`DoctorReport`).
- **Forge/** — the typed client of `orkeon forge --events jsonl` (the engine behind the creation wizard): a tolerant line parser pinned against the CLI's golden protocol lines, the session projection every front reads (`ForgeSessionModel`, milestone mapping, the ✔/✘ checklist rules), the child-process driver with the stdin answer channel (`ForgeClient`, whose start request carries environment overrides — how Studio's assistant profile reaches the engine), the on-disk session catalogue and the resume hydrator. Studio's process never touches an LLM — it only ever sees JSON lines.
- **Profiles/** — the named model settings of the v3 design (`ModelProfile`, `ModelProfileSet`, `ModelProfileFileStore` → `studio-model-profiles.json` next to the settings file): reusable "réglages de modèle", a default election mirrored into the `Llm` section, the profile Studio's own assistant runs on, and per-profile `ORKEON_Llm__*` environment overrides for launches. The profile editor offers the full provider catalogue (`LlmPresets.ProviderCatalogFor` — the two local runtimes plus every cloud the framework ships a provider for, endpoint/model pre-filled from the drift-pinned runtime defaults), and a novice pastes the API key right in the editor: it lands in a **user environment variable** (`IApiKeyStore`/`EnvironmentApiKeyStore`, the vendor's conventional name such as `DEEPSEEK_API_KEY`) — the store file only ever carries the *name* of that variable (`ModelProfile.KeyEnvName`), and launches lay the resolved value over the child process as `ORKEON_Llm__ApiKey`. The key itself never enters any file.
- **Teams/** — the teams directory (`TeamCatalog`, default `~/Orkeon/teams`): every adopted team is an ordinary folder — listed, duplicated, deleted, imported (with an inline-secret scan) — plus the `studio-team.json` sidecar recording what the crew definition cannot say (name, need, profile, displayed schedule). The recorded profile is not decorative: launching an adopted team resolves it against the profile store and lays it over the run as `ORKEON_Llm__*`.
- **Run/** — the typed client of a **watched** `orkeon run --events jsonl` (BUS-06): `RunClient`, ForgeClient's sibling and deliberately its twin — same launcher, same locator, same envelope parser — and `RunProgressModel`, which folds the stream into what a screen shows (finished tasks, cost, the question the run is waiting on). The client also carries the seat the run's hub gives a watching process: post to an agent, publish, subscribe, reply — and the Launch screen staffs that seat too: an agent's `send` (marked `expectsReply`) shows up as a request panel, and the typed reply goes back down stdin. See [The run event bus](run-event-bus.md).
- **Storage/ & History/** — settings locations and resolution chain (`SettingsLocations`, `AppSettingsFile`), launch history (`LaunchHistoryStore`).
- **Validation/** — `AppSettingsValidator` + `ValidationMessageFormatter`.
- **Localization/** — the `IStudioStrings` port (below).

Core's dependency list is deliberately slim: only `Orkeon.Domain` (mounts, `LlmDefaults`) and `Orkeon.Rag.Abstractions` (`RagProfilePresets`, the closed list of RAG profile names the UIs offer). Core is referenced by three self-contained front-ends, so every transitive dependency is paid three times on disk — heavier references were dropped, and the few duplicated constants are pinned against the originals by drift tests in `Orkeon.Studio.Core.Tests`.

### The front-ends

- **`orkeon-studio-config`** (TUI) — full-screen editor for the settings file: provider presets, model and endpoint, logging, rate limiting, RAG profile, the VFS mount table, a raw-JSON view, and a diagnostic screen running `orkeon doctor`. Rendering only: every behaviour comes from Core.
- **`orkeon-studio-run`** (TUI) — pick a target, set the run options (including `--validate` for a dry run), watch the output live, cancel if needed. `--version` and `--help` are answered headlessly before Terminal.Gui initializes, so both TUIs stay scriptable and CI-checkable.
- **`orkeon-studio`** (WPF, Windows) — one desktop window in the v3 "volets" design: a sidebar in team-lifecycle order — **Agent teams** (Create a team, My teams, Import), **Work** (Test, Run, History), **Environment** (Settings, Diagnostic — the doctor report is copyable as plain text) — under a global **Novice/Expert** switch. Novice explains every step, shows the contextual help and hides the machinery; Expert shows everything: command lines, raw JSON, the technical journal, the expert-only Test screen. The window opens on a startup screen (Kama, the mascot, click to skip), carries an About overlay, a five-stop guided tour, light/dark themes and a hot EN/FR toggle; it holds a 1024×768 minimum and every control is styled — no native Windows chrome. Novice screens follow the v3 mock closely: **Run** is a team card (sidecar-backed name and meta line), a plain-language progress card with a tone badge and an "open the result" action, and a technical journal folded by default; **History** is a card list with per-run duration and a localized outcome sentence; **Diagnostic** opens on a verdict card fed by a silent first doctor run at startup, with plain-language check names; **Settings** saves itself on every novice edit (the explicit Validate/Save cycle is the expert's), and the authorized folders are one card per mount with an "allow a folder" picker flow. It references only `Orkeon.Studio.Core`. Besides `--smoke-exit`, it accepts `--cli-dir <dir>` (names the CLI's directory, beating every other lookup) and `--capture-screens <dir>`: a headless screenshot campaign that walks every screen in both modes (plus the profile-editor and About overlays) and writes one PNG per stop — the fidelity-remediation reference against the design mock.

The creation wizard is the doctrine at work: "Créer une équipe" walks Décrire ▸ Composer ▸ Essayer ▸ Adopter over `orkeon forge --events jsonl` launched as a child process — the stepper is a projection of the engine's milestones, the per-step "consigne + questions" blocks travel down the ordinary `user.message` channel, the arbitration buttons are generated from the engine's own `decision.needed` options, and adoption promotes straight into the teams directory with the engine's real schedule grammar (on demand, `daily@HH:mm`, `hourly`). A capability absent from the stream does not exist on the screen — which is exactly what keeps the terminal `orkeon forge` and the WPF wizard from drifting apart. The wizard is gated until Studio's assistant has a model profile; the unified Settings screen (AI model tab with the named profiles, authorized folders, and the expert limits/raw-file tabs) is where that election lives.

### The Launch screen is no longer a terminal

It used to be a twenty-thousand-line list: honest, and a terminal with a theme. The person launching a crew from Studio wants two things scrollback does not give — is it advancing, and is it waiting on me — so the screen now watches the run through the same protocol the creation wizard uses (`--events jsonl`, on by default; unchecking the option gives the plain argv back).

What it shows: finished tasks with their agent, duration and tokens; a cost line; and **the run's question, asked on screen**. Before this, a task declared `humanInput: true` was auto-approved behind the user's back — a defensible fallback for an unattended run, and the wrong answer entirely once a screen is watching.

The raw log is **demoted, not removed**. A line the panel cannot read falls through to it rather than into nothing, which is the rule the terminal launcher already followed.

Three refusals keep the panel honest, and each is pinned by a test. A silent run says "nothing reported yet" rather than implying progress. A question arriving without a correlation id is not shown as pending, because answering needs an address. And an answer that could not be written leaves the question open instead of pretending it landed.

### Team folders end to end (remediation v2)

An adopted team's folders are part of the team: the sidecar `studio-team.json`
records them as mount strings (`mounts`), next to the display name, profile and
schedule. The "Mes équipes" cards show them as chips; « Changer les dossiers »
edits them in the team-mounts modal; every folder choice — there, in the
wizard's « Dossiers de cette équipe » block, or on the novice settings card —
goes through the shared « Autoriser un dossier » picker (path + browse, a
one-level tree with "already allowed" notes, rights as two radio rows, and an
expert preview of the exact mount string). At launch, Studio lays the sidecar's
mounts on the run as `--mount` arguments ahead of the per-launch ones, so the
chips and the command cannot disagree. Deliberate limit: a bare `orkeon run` in
a terminal does not read the sidecar — like the `profile` field, this is
Studio's comfort, not the engine's contract.

### The blueprint, edited by hand

The forge protocol's `edit` arbitration is real: interactive mode arbitrates
every verdict (a conforming one costs one "accept" click), and
`decision.made {edit}` followed by `blueprint.edited {blueprint}` re-renders the
crew deterministically — zero LLM tokens — then re-earns its verdict through the
unchanged validate/test/diagnose path; the engine re-validates everything it
receives (parse, compile, tool catalogue) and answers an invalid edit with a
recoverable `FORGE-BLUEPRINT-INVALID`, reopening the arbitration. In Studio, the
Composer step shows one card per blueprint agent with « Modifier » opening the
agent editor — the name maps to the blueprint's `role`, "what it does" to its
`goal`, the capability chips to its `tools`; « Retirer de l'équipe » and
« Ajouter un agent » travel the same path. The buttons are actionable exactly
while the engine waits at its arbitration — during a trial they wait with it.

## Localization: the `IStudioStrings` port

`Orkeon.Studio.Core` defines a localization port, `IStudioStrings` (`Localization/StudioStrings.cs`): a key indexer plus a `CultureChanged` event so ViewModels can re-emit their bindings when the language switches. The English defaults in `EnglishStudioStrings` are the key registry of record. Each front decides the language: the WPF app bridges the port onto its resx-backed `I18n` service (`I18nStudioStrings`, `Strings.resx`/`Strings.fr.resx`) with a **hot EN/FR switch** relayed via `CultureChanged`; the TUIs keep the English default. Deliberately untranslated, by CLI-contract policy: `orkeon doctor` check details, LLM probe results, exit-code descriptions and the `VALIDATION OK/FAILED` verdicts — translating Studio's copy would desynchronize it from what the CLI prints in a terminal. Validator messages and doctor check names follow a revised split: the raw English line stays as the expert detail (tooltip or mono side label), and a per-code plain-language overlay (`Vm_ValMsg_*`, `Vm_Doctor_*` keys) is what the lists show first.

## Where Studio keeps things

Two roots, one rule: **application state** lives in the per-user config directory,
**documents** live in the user profile. Nothing is ever generated into the working
directory, and API keys live in neither — they stay in the user's environment
variables, never in a file.

**`%APPDATA%\Orkeon\`** (`$XDG_CONFIG_HOME/Orkeon/` elsewhere) — application state:

| Entry | What it is |
|---|---|
| `appsettings.json` | the per-user global settings — the durable base every launch composes on |
| `studio-model-profiles.json` | the named model profiles (provider, model, URL, temperature, timeout, **key env-var name only**) |
| `studio-history.json` | the launch history the Historique screen and the team cards read |
| `Studio\ui-preferences.json` | window comfort: mode, language, theme |
| `.orkeon\forge\<slug>\` | the **atelier sessions** — resumable works-in-progress (brief, blueprint, provisional `crew/` render, trial `runs/`), not adopted crews. The dot-name is the engine's workspace-state convention (SPEC §4.1, like `.git`): Studio hands `%APPDATA%\Orkeon` to the engine as its forge workspace, so `forge resume <slug>` works identically from a terminal and from Studio |

**`%USERPROFILE%\Orkeon\teams\<slug>\`** — documents: the adopted teams. Each is an
ordinary, self-contained folder (crew definition, `run.cmd`/`run.sh`, the
`studio-team.json` sidecar with name, need, profile, schedule and mounts) — copiable,
shareable, deletable, runnable with `orkeon run <folder>` alone. Adoption *moves* a
session's result from the state root to the documents root; that is the boundary
between a draft and a deliverable.

## How Studio ships

Studio is installed **next to the CLI** by the release packages — see the [publication matrix](../reference/publication-matrix.md) for the exact artifacts and [Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md) for the walkthrough:

- **Windows** — the `orkeon-cli-<version>-win-x64.zip` and the per-user MSI carry the WPF desktop app (`orkeon-studio`, self-contained); the MSI also registers an "Orkeon Studio" Start-menu shortcut.
- **Debian/Ubuntu** — the `.deb` and the Linux multi-app archives carry the two terminal apps (`orkeon-studio-config`, `orkeon-studio-run`), self-contained.
- **macOS** — CLI-only on the onboarding channel in V1; the multi-app `osx-*` archives do carry the two TUIs (only the WPF app has a RID filter), untested on macOS in V1.

The two TUIs target plain `net10.0` and are therefore cross-platform builds; only `Orkeon.Studio.Wpf` is Windows-bound (`net10.0-windows`, WPF).

## What Studio is not

- **Not a NuGet package** — all four projects set `IsPackable=false`; the only distribution channel is the release installers.
- **Not a separate engine** — Studio never re-implements a workflow: it edits the CLI's settings file and spawns the CLI itself (`OrkeonProcessRunner`), so its results are exactly `orkeon run`'s.

---

> **See also**: [Publication matrix](../reference/publication-matrix.md) ·
> [Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md) ·
> [Back to index](../INDEX.md)
