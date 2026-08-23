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
- **Launch/ & Process/** — building the `orkeon run` command line (`RunArgumentsBuilder`, `RunLaunchOptions`), locating the binary (`OrkeonBinaryLocator`), running it and streaming output (`OrkeonProcessRunner`, `IProcessLauncher`), interpreting exit codes (`OrkeonExitCodes`, `LaunchOutcomeFormatter`), and the `orkeon doctor` report (`DoctorReport`).
- **Forge/** — the typed client of `orkeon forge --events jsonl` (the engine behind the creation wizard): a tolerant line parser pinned against the CLI's golden protocol lines, the session projection every front reads (`ForgeSessionModel`, milestone mapping, the ✔/✘ checklist rules), the child-process driver with the stdin answer channel (`ForgeClient`, whose start request carries environment overrides — how Studio's assistant profile reaches the engine), the on-disk session catalogue and the resume hydrator. Studio's process never touches an LLM — it only ever sees JSON lines.
- **Profiles/** — the named model settings of the v3 design (`ModelProfile`, `ModelProfileSet`, `ModelProfileFileStore` → `studio-model-profiles.json` next to the settings file): reusable "réglages de modèle", a default election mirrored into the `Llm` section, the profile Studio's own assistant runs on, and per-profile `ORKEON_Llm__*` environment overrides for launches. The API key never enters the store.
- **Teams/** — the teams directory (`TeamCatalog`, default `~/Orkeon/teams`): every adopted team is an ordinary folder — listed, duplicated, deleted, imported (with an inline-secret scan) — plus the `studio-team.json` sidecar recording what the crew definition cannot say (name, need, profile, displayed schedule).
- **Run/** — the typed client of a **watched** `orkeon run --events jsonl` (BUS-06): `RunClient`, ForgeClient's sibling and deliberately its twin — same launcher, same locator, same envelope parser — and `RunProgressModel`, which folds the stream into what a screen shows (finished tasks, cost, the question the run is waiting on). The client also carries the seat the run's hub gives a watching process: post to an agent, publish, subscribe, reply — and the Launch screen staffs that seat too: an agent's `send` (marked `expectsReply`) shows up as a request panel, and the typed reply goes back down stdin. See [The run event bus](run-event-bus.md).
- **Storage/ & History/** — settings locations and resolution chain (`SettingsLocations`, `AppSettingsFile`), launch history (`LaunchHistoryStore`).
- **Validation/** — `AppSettingsValidator` + `ValidationMessageFormatter`.
- **Localization/** — the `IStudioStrings` port (below).

Core's dependency list is deliberately slim: only `Orkeon.Domain` (mounts, `LlmDefaults`) and `Orkeon.Rag.Abstractions` (`RagProfilePresets`, the closed list of RAG profile names the UIs offer). Core is referenced by three self-contained front-ends, so every transitive dependency is paid three times on disk — heavier references were dropped, and the few duplicated constants are pinned against the originals by drift tests in `Orkeon.Studio.Core.Tests`.

### The front-ends

- **`orkeon-studio-config`** (TUI) — full-screen editor for the settings file: provider presets, model and endpoint, logging, rate limiting, RAG profile, the VFS mount table, a raw-JSON view, and a diagnostic screen running `orkeon doctor`. Rendering only: every behaviour comes from Core.
- **`orkeon-studio-run`** (TUI) — pick a target, set the run options (including `--validate` for a dry run), watch the output live, cancel if needed. `--version` and `--help` are answered headlessly before Terminal.Gui initializes, so both TUIs stay scriptable and CI-checkable.
- **`orkeon-studio`** (WPF, Windows) — one desktop window in the v3 "volets" design: a sidebar in team-lifecycle order — **Agent teams** (Create a team, My teams, Import), **Work** (Test, Run, History), **Environment** (Settings, Diagnostic) — under a global **Novice/Expert** switch. Novice explains every step, shows the contextual help and hides the machinery; Expert shows everything: command lines, raw JSON, the technical journal, the expert-only Test screen. The window opens on a startup screen (Kama, the mascot, click to skip), carries an About overlay, a five-stop guided tour, light/dark themes and a hot EN/FR toggle. It references only `Orkeon.Studio.Core`.

The creation wizard is the doctrine at work: "Créer une équipe" walks Décrire ▸ Composer ▸ Essayer ▸ Adopter over `orkeon forge --events jsonl` launched as a child process — the stepper is a projection of the engine's milestones, the per-step "consigne + questions" blocks travel down the ordinary `user.message` channel, the arbitration buttons are generated from the engine's own `decision.needed` options, and adoption promotes straight into the teams directory with the engine's real schedule grammar (on demand, `daily@HH:mm`, `hourly`). A capability absent from the stream does not exist on the screen — which is exactly what keeps the terminal `orkeon forge` and the WPF wizard from drifting apart. The wizard is gated until Studio's assistant has a model profile; the unified Settings screen (AI model tab with the named profiles, authorized folders, and the expert limits/raw-file tabs) is where that election lives.

### The Launch screen is no longer a terminal

It used to be a twenty-thousand-line list: honest, and a terminal with a theme. The person launching a crew from Studio wants two things scrollback does not give — is it advancing, and is it waiting on me — so the screen now watches the run through the same protocol the Solve screen uses (`--events jsonl`, on by default; unchecking the option gives the plain argv back).

What it shows: finished tasks with their agent, duration and tokens; a cost line; and **the run's question, asked on screen**. Before this, a task declared `humanInput: true` was auto-approved behind the user's back — a defensible fallback for an unattended run, and the wrong answer entirely once a screen is watching.

The raw log is **demoted, not removed**. A line the panel cannot read falls through to it rather than into nothing, which is the rule the terminal launcher already followed.

Three refusals keep the panel honest, and each is pinned by a test. A silent run says "nothing reported yet" rather than implying progress. A question arriving without a correlation id is not shown as pending, because answering needs an address. And an answer that could not be written leaves the question open instead of pretending it landed.

## Localization: the `IStudioStrings` port

`Orkeon.Studio.Core` defines a localization port, `IStudioStrings` (`Localization/StudioStrings.cs`): a key indexer plus a `CultureChanged` event so ViewModels can re-emit their bindings when the language switches. The English defaults in `EnglishStudioStrings` are the key registry of record. Each front decides the language: the WPF app bridges the port onto its resx-backed `I18n` service (`I18nStudioStrings`, `Strings.resx`/`Strings.fr.resx`) with a **hot EN/FR switch** relayed via `CultureChanged`; the TUIs keep the English default. Deliberately untranslated, by CLI-contract policy: `orkeon doctor` check names and details, validator message bodies, LLM probe results, exit-code descriptions and the `VALIDATION OK/FAILED` verdicts — translating Studio's copy would desynchronize it from what the CLI prints in a terminal.

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
