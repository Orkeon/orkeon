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

- **Configuration/** — a lossless `appsettings.json` editing model (`AppSettingsDocument`) plus typed sections (`LlmSection`, `LlmLoggingSection`, `LoggingSection`, `MountsSection`, `RagSection`, `RateLimitingSection`, and since STUDIO-21 `McpSection` and `ShellToolsSection`), `LlmProviderDetector`, and the tool catalogue (`Tools/ToolCatalog`: the tools a run exposes, by family, with what each one needs).
- **FileSystem/** — mount editing: `MountDefinition`, `MountRights`, `MountValidator`, directory browsing.
- **Presets/ & Llm/** — the LLM preset catalogue (`LlmPresets`, `OrkeonCliDefaults`), endpoint probing (`ILlmEndpointProbe`/`HttpLlmEndpointProbe`, `LlmApiKeyResolver`), the balance probe (`IProviderBalanceProbe`/`HttpProviderBalanceProbe`, STUDIO-33) and the accounts a balance read covers (`ProviderBalanceAccount`/`ProviderBalanceTarget`, STUDIO-35).
- **Targets/** — run-target detection (`RunTargetDetector`): a `config.yaml`, a multi-file crew directory, or a `.ork.ts` script.
- **Launch/ & Process/** — building the `orkeon run` command line (`RunArgumentsBuilder`, `RunLaunchOptions`), locating the binary (`OrkeonBinaryLocator` — in order: the `--cli-dir` argument, next to the executable, the `ORKEON_CLI_DIR` environment variable, `PATH`, then the development checkout), running it and streaming output (`OrkeonProcessRunner`, `IProcessLauncher`), interpreting exit codes (`OrkeonExitCodes`, `LaunchOutcomeFormatter`), and the `orkeon doctor` report (`DoctorReport`).
- **Forge/** — the typed client of `orkeon forge --events jsonl` (the engine behind the creation wizard): a tolerant line parser pinned against the CLI's golden protocol lines, the session projection every front reads (`ForgeSessionModel`, milestone mapping, the ✔/✘ checklist rules), the child-process driver with the stdin answer channel (`ForgeClient`, whose start request carries environment overrides — how Studio's assistant profile reaches the engine), the on-disk session catalogue and the resume hydrator. Studio's process never touches an LLM — it only ever sees JSON lines.
- **Profiles/** — the named model settings of the v3 design (`ModelProfile`, `ModelProfileSet`, `ModelProfileFileStore` → `studio-model-profiles.json` next to the settings file): reusable "model settings", a default election mirrored into the `Llm` section, the profile Studio's own assistant runs on, and per-profile `ORKEON_Llm__*` environment overrides for launches (model, endpoint, temperature, timeout and the response budget `MaxTokens` — empty leaves the cap to the engine, which sends the model's documented maximum, and the hint under the field says what that is for the chosen model, or that the model is unknown to the catalogue and gets 4096 unless pinned — LLM-10; the thinking switch — provider default / on / off — and the reasoning-effort hint, `ORKEON_Llm__Thinking__{Enabled,Effort}`, and a timeout pre-filled to 600 s when the picked provider's default model reasons before it answers — Kimi, DeepSeek, Z.AI, MiniMax — LLM-11). The profile editor offers the full provider catalogue (`LlmPresets.ProviderCatalogFor` — the two local runtimes plus every cloud the framework ships a provider for, endpoint/model pre-filled from the drift-pinned runtime defaults), and a novice pastes the API key right in the editor: it lands in a **user environment variable** (`IApiKeyStore`/`EnvironmentApiKeyStore`, the vendor's conventional name such as `DEEPSEEK_API_KEY`) — the store file only ever carries the *name* of that variable (`ModelProfile.KeyEnvName`), and launches lay the resolved value over the child process as `ORKEON_Llm__ApiKey`. The key itself never enters any file. The store reads its file with case-insensitive property names — it is hand-editable, and `"profiles"` is what people type — and a file that exists but cannot be parsed is reported on the settings screen instead of loading as an empty set indistinguishable from a first run.
- **Teams/** — the teams directory (`TeamCatalog`, default `~/Orkeon/teams`): every adopted team is an ordinary folder — listed (the active teams, the archived ones or all of them; a dot folder, or a hidden or system one on Windows, is never a team), duplicated, deleted, imported (with an inline-secret scan, and refused before any copy when the launcher's detector cannot resolve it to a crew definition, with the detector's message; a duplicate or an import that fails halfway leaves no partial folder behind) — plus the `studio-team.json` sidecar recording what the crew definition cannot say (name, need, profile, the schedule chosen — whether the system runs it is the engine's answer, STUDIO-27 — whether the team is archived, and when it last ran from Studio, STUDIO-31 — and when a copy of it entered the teams root, STUDIO-32). Every writer merges into the sidecar, never rebuilds it: a re-adoption keeps what it does not own. The recorded profile is not decorative: launching an adopted team resolves it against the profile store and lays it over the run as `ORKEON_Llm__*`.
- **Run/** — the typed client of a **watched** `orkeon run --events jsonl` (BUS-06): `RunClient`, ForgeClient's sibling and deliberately its twin — same launcher, same locator, same envelope parser — and `RunProgressModel`, which folds the stream into what a screen shows (the tasks in progress and every tool at work, delegations under way, finished tasks, cost, the question the run is waiting on — the whole state is under *The progress state of a run*, below). The client also carries the seat the run's hub gives a watching process: post to an agent, publish, subscribe, reply — and the Launch screen staffs that seat too: an agent's `send` (marked `expectsReply`) shows up as a request panel, and the typed reply goes back down stdin. See [The run event bus](run-event-bus.md).
- **UseCases/** — the typed client of `orkeon usecases` (STUDIO-39): `UseCaseClient`, `ForgeClient`'s twin — `usecases list` run to completion for the gallery's catalogue, one `usecases search` session kept open for the suggestions under the need, each query matched to its answer by correlation id, `usecases export` run to completion for « Import as is » (STUDIO-41), failures typed rather than thrown — plus the readers of the catalogue and of an answer (`UseCaseCatalog`, `UseCaseAnswer`), the rule that decides which answers are close enough to suggest (`UseCaseSuggestions`), which reads how many use cases carry a term off the answer itself — Studio never normalizes a spelling of its own — and `UseCaseImporter`, which brings an exported case into the teams root through the import every team takes.
- **Storage/ & History/** — settings locations and resolution chain (`SettingsLocations`, `AppSettingsFile`), launch history (`LaunchHistoryStore`).
- **Validation/** — `AppSettingsValidator` + `ValidationMessageFormatter`.
- **Localization/** — the `IStudioStrings` port (below).

Core's dependency list is deliberately slim: only `Orkeon.Domain` (mounts, `LlmDefaults`) and `Orkeon.Rag.Abstractions` (`RagProfilePresets`, the closed list of RAG profile names the UIs offer). Core is referenced by three self-contained front-ends, so every transitive dependency is paid three times on disk — heavier references were dropped, and the few duplicated constants are pinned against the originals by drift tests in `Orkeon.Studio.Core.Tests`.

### The front-ends

- **`orkeon-studio-config`** (TUI) — full-screen editor for the settings file: provider presets, model and endpoint, logging, rate limiting, RAG profile, the VFS mount table, a raw-JSON view, and a diagnostic screen running `orkeon doctor`. Rendering only: every behaviour comes from Core.
- **`orkeon-studio-run`** (TUI) — pick a target, set the run options (including `--validate` for a dry run), watch the output live, cancel if needed. `--version` and `--help` are answered headlessly before Terminal.Gui initializes, so both TUIs stay scriptable and CI-checkable.
- **`orkeon-studio`** (WPF, Windows) — one desktop window in the v3 "volets" design: a sidebar in team-lifecycle order — **Agent teams** (Create a team, My teams, Import), **Work** (Test, Run, History), **Environment** (Settings, Diagnostic — the doctor report is copyable as plain text) — under a global **Novice/Expert** switch. Novice explains every step, shows the contextual help and hides the machinery; Expert shows everything: command lines, raw JSON, the technical journal, the expert-only Test screen. The window opens on a startup screen (Kama, the mascot, click to skip), carries an About overlay, a five-stop guided tour, light/dark themes and a hot five-language switch; it holds a 1024×768 minimum and every control is styled — no native Windows chrome. Novice screens follow the v3 mock closely: **Run** is a team card (sidecar-backed name and meta line), a plain-language progress card with a tone badge and an "open the result" action, and a technical journal folded by default; **History** is a card list with per-run duration and a localized outcome sentence; **Diagnostic** opens on a verdict card fed by a silent first doctor run at startup, with plain-language check names; **Settings** opens on the per-user file (`%APPDATA%\Orkeon\appsettings.json`, the one `orkeon init` writes), so the folders it declares, its `Llm` section and its raw JSON are on screen from the first frame; it saves itself on every novice edit (the explicit Validate/Save cycle is the expert's), writing that file back with its other keys intact, and the authorized folders are one card per mount, « Allow a folder » opening the OS folder dialog directly (STUDIO-19); the expert limits tab shows the engine's default in every empty field, as a watermark carried by the theme, and its switches are one-click booleans that remove their key when set back to the default (STUDIO-22). It references only `Orkeon.Studio.Core`. Besides `--smoke-exit`, it accepts `--cli-dir <dir>` (names the CLI's directory, beating every other lookup) and `--capture-screens <dir>`: a headless screenshot campaign — the fidelity-remediation reference against the design mock. It builds the window over a **seeded scenario** (six adopted teams, one of them archived, seven past runs, four forge sessions, four model profiles, a doctor with one warning and one failure, plus a first-run machine with nothing on it and no CLI), then walks every screen and every gated state of it — the four wizard steps, the five modals, the guided tour, the assistant, empty lists beside populated ones — in both modes and **both themes**, plus a language sweep over the densest screens. Roughly 350 images over eight passes, one PNG per stop under `<lang>/<theme>/<mode>/`, the same relative path in every pass so comparing two of them is a directory diff, and a `manifest.json` carrying each image's reason, its SHA-256 and the reason for every stop that failed. The scenario lives in a throwaway temp directory: the campaign never reads or writes the operator's teams, history, settings or preferences. Exit 0, or 1 with each failed stop named on stderr.

The creation wizard is the doctrine at work: "Create a team" walks Describe ▸ Compose ▸ Try ▸ Adopt over `orkeon forge --events jsonl` launched as a child process — composing runs with `--dry`, so the engine generates and validates then **pauses at the Composer step**; the trial is the user's own « Try the team » click, which resumes the session without dry (a session reopened from "My teams" at that pause lands back on Composer the same way) — the stepper is a projection of the engine's milestones, the per-step "instruction + questions" blocks travel down the ordinary `user.message` channel, the arbitration buttons are generated from the engine's own `decision.needed` options, and adoption promotes straight into the teams directory with the engine's real schedule grammar (on demand, `daily@HH:mm`, `hourly`), then hands the wizard back at a blank step 1 with one line saying the team is in My teams (STUDIO-20). A capability absent from the stream does not exist on the screen — which is exactly what keeps the terminal `orkeon forge` and the WPF wizard from drifting apart. The wizard is gated until Studio's assistant has a model profile; the unified Settings screen (AI model tab with the named profiles, authorized folders, the tools tab, the Studio tab, and the expert limits, MCP and raw-file tabs) is where that election lives. When the click fails — no `orkeon` binary on the machine, a configuration the engine refuses, a non-zero exit with or without stderr, a refused promotion at step 4 — a card under the status line says so in the user's language, keeps the engine's own text raw (wrapped, bounded, scrollable, never translated) and offers « Copy the report » (command line, exit code, engine error, the whole stderr and journal), « Try again » and, by family, the diagnostic or the settings; it shows in both modes, clears on the next composition, and « Stop » never produces one.

### The use-case gallery (STUDIO-39)

Step 1 of the wizard no longer stops at four hard-coded examples. Those four stay first, as quick suggestions, and a link under them — « Browse the use cases (105) », the count read from the catalogue — opens a side panel over the window: the whole example catalogue the `orkeon` tool embeds (STUDIO-36 to 38), searched as you type (every word must start a word of the card, accents and case aside) and filtered by category (nine chips), by how the team works (the process, in plain words), without web access, and without a third-party key. A card carries the title and the problem in the window's language, a badge for the process, and « Reference only » on the finance cases whose crews depend on files the CLI does not carry: they stay browsable and usable as a reference. The expert also sees each case's id and the engine's spelling of its process.

Choosing a card writes its problem into the need, in the window's language — Studio's language switch says `zh` where the catalogue says `zh-Hans` — and attaches the case as the creation's **reference**: a chip « Inspired by: <title> » under the need, whose ✕ takes the reference away and leaves the words. The reference is wizard state (`CreateTeamViewModel.ReferenceUseCaseId`): composing hands it to the engine as `forge --reference <id>` (STUDIO-40) — the forge designs the team on the model of the case's structure, and the session records it. It is cleared when the creation ends (Start over, adoption), and read back from the session on a resume or a reopen, whose chip names the case again.

While the user types, the wizard proposes. After a 500 ms pause in the need box it asks the CLI's search session — `orkeon usecases search --events jsonl` in session mode: one process for the life of the window, started by the first query, its stdin closed when the window closes — and shows « N close use cases » under the box; the link opens the gallery on those cases, best first. Which answers count is Studio's rule, measured against the real catalogue: a match counts when it shares with the need at least one **distinctive** term — a rare one, which at most 3 % of the use cases contain (3 of 105), and long enough to be a word of the need rather than of the sentence: four characters or more in a sentence, three in a keyword search of one or two words (`kyc`, `etl`), any length in Chinese, whose terms are character pairs. A match by meaning alone never counts — in hybrid mode a nonsense query still gets five of them — and sharing terms is not enough either: a plain French sentence shares `de`, `un` or `mes` with almost every sheet, and the catalogue holds `est` in three of them. How many use cases carry a term is read off the answer itself: the wizard asks the session for the whole catalogue (`top` = its size), and an answer by terms lists every sheet that shares a term with the need, each with the terms it shares, spelled by the CLI's own normalization — Studio never normalizes a spelling of its own, and its search box compares with the platform's collation, accents and case aside. In English, searched in hybrid mode, the terms half of the fusion is the best twenty sheets, which is where the sheets of a rare term rank anyway. When no match qualifies, nothing is shown: no hint rather than noise. A need a chosen case wrote is not searched.

There is no fallback on Studio's side: the catalogue comes from the CLI like every other answer of the wizard, read once at start-up through the runner the doctor and the launcher share, so the gallery can never show the catalogue of another binary than the one that runs the teams. Without the CLI the panel shows the wizard's own « engine not found » card — the locator's words, a retry, the way to the diagnostic — and nothing is suggested. A session that ended without ever announcing itself (an engine older than STUDIO-38) is not reopened by every keystroke; a catalogue that loads again lifts that.

### Importing a use case as it is (STUDIO-41)

In expert mode, every card that is not reference-only also offers « Import as is »: the case becomes a team of My teams — ready to run with its data, and modifiable — without the workshop. Novice mode never shows the action, and a reference-only case never offers it. The name the team takes is the case's title in the window's language, and its folder follows the folder rule of every team — the case's id names the folder when the title keeps nothing that rule can use, as a Chinese one does. The name is checked first, by the adoption's own rule (STUDIO-26, D-07): when something already holds its folder, a banner above the cards says what, offers the free name (« Import as “… (2)” ») and the way to the team that holds this one, and nothing is exported until the name is free. Then `UseCaseImporter` has the CLI write the case as a team folder — `orkeon usecases export <id> --to <staging> --lang <code> --events jsonl`, into a staging folder named like the team's folder — and brings that folder into the teams root through the import every team takes (`TeamCatalog.Import`: the launcher's own detector, the copy, the sidecar read back and normalized); the staging folder goes either way. The team carries its crew as `crew/config.yaml`, its sample data, and a sidecar that names and describes it and records the manifest's mounts team-relative (`./data:/data:ro`, `./output:/output:rw`). « Modify » is active, since `forge reopen` rebuilds a session from `crew/` with a brief derived from the plan; and the launcher resolves the mounts under the team like any adopted team's, so the team runs with its data mounted. The banner ends on the adoption's own line and on the Import screen's report on the folders, with « Open My teams »; My teams refreshes on its own.

### The Launch screen is no longer a terminal

It used to be a twenty-thousand-line list: honest, and a terminal with a theme. The person launching a crew from Studio wants two things scrollback does not give — is it advancing, and is it waiting on me — so the screen now watches the run through the same protocol the creation wizard uses (`--events jsonl`, on by default; unchecking the option gives the plain argv back).

What it shows: finished tasks with their agent, duration and tokens; a cost line; and **the run's question, asked on screen**. Before this, a task declared `humanInput: true` was auto-approved behind the user's back — a defensible fallback for an unattended run, and the wrong answer entirely once a screen is watching.

The raw log is **demoted, not removed**. A line the panel cannot read falls through to it rather than into nothing, which is the rule the terminal launcher already followed.

Three refusals keep the panel honest, and each is pinned by a test. A silent run says "nothing reported yet" rather than implying progress. A question arriving without a correlation id is not shown as pending, because answering needs an address. And an answer that could not be written leaves the question open instead of pretending it landed.

The first version showed only what had **finished**. During a seven-minute task the card held a still "running" badge and the rows of the tasks already done, and nothing told a working run from a stalled one — the owner's own words: "on ne sait pas si le processus est en cours". The engine now announces each task as it starts (`task.started`, every mode), so the card carries a row for **what is running now** — its agent, a turning glyph, "since HH:mm:ss" from the run's own clock — and, under it, the tool at work, straight from `tool.called` / `tool.returned`. The badge pulses while the child process lives and stops the moment it exits. Each finished row also says how many tools the task called: a task meant to write a file and reporting zero is the diagnosis a green run hides. Two smaller things the same review asked for: every journal line leads with the time Studio read it, on screen and in the copied text; and every launch starts from a clean screen — the previous run's journal and verdict go, so nothing on screen can be taken for the run about to start. "Copy" is how a journal survives the next click; "validate first" keeps its dry run and its real run in one journal, because the clean start is per click, not per pass.

### The progress state of a run (STUDIO-30)

`RunProgressModel` (Core, `Run/`) holds the whole live state of one watched run, so a screen that wants it — a status bar, say — reads the model and never the raw lines. Each launcher feeds its own: the Launch tab (`Launch.Progress`) and the Test screen (`Test.Launcher.Progress`, which folds the same events; its own view does not show them, the status bar does — STUDIO-34). The terminal launcher, `orkeon-studio-run`, does not use the model.

| State | Read from |
|---|---|
| `ActiveTools` — every tool at work, oldest first, with its name and `StartedAt` (the envelope `ts`). `ActiveToolName`, the latest of them, is what the Launch card's activity line names | `tool.called`, closed by the `tool.returned` under the same correlation id |
| `ToolCallCount`, `SucceededToolCalls`, `FailedToolCalls` | `tool.called`; the `success` of `tool.returned` |
| `RunningTasks` | `task.started`, until its `task.completed` |
| `ActiveDelegations` — the role the work went to, and since when | `delegation.started`, until its `tool.returned` |
| `SpawnedAgents` — role, reason, time | `agent.spawned` |
| `IsWaitingForAnswer` — a question for a human or an agent's request is pending (`PendingQuestion`, `PendingAgentRequest`) | `input.needed`; `hub.message` marked `expectsReply` |
| `Cost` — ↑ `PromptTokens`, ↓ `CompletionTokens`, the cache pair, `EstimatedTokens` (the part the runtime estimated), the vendor's own `Amount` and `Currency`, and the `Model` and `Provider` of the agents' calls | `cost.updated` (STUDIO-29); its `operation` says whose call it was |
| `StartedAt`, `Elapsed` | `run.started`; the `durationMs` of `run.finished` |
| `UnfinishedTools`, `UnfinishedDelegations` | the calls still open at `run.finished` |

A return closes the call its correlation id names, so parallel calls — two of the same tool included — close in whatever order they come back. A delegation is a tool call underneath, but the CLI reports it *instead of* its `tool.called` and closes it with that call's `tool.returned` — there is no `delegation.finished` — so it is under way until then, and it is not counted among the tool calls. A spawned agent stays counted whatever its spawn call returns: a spawn that waits for its agent reports that agent's own failure. `spawn_agent` is attached to no agent by default, so that list is usually empty.

The end of the run empties what is at work, and a call still open then **never came back**: it moves to `UnfinishedTools` (or `UnfinishedDelegations`) and is never counted a success. Every tool call announced sits in exactly one place — at work, succeeded, failed, or not finished.

What was not measured stays absent. No `cost.updated`, no `Cost` — even when the close carries a token total — and a field the meter did not carry stays null, never a zero. `Elapsed` is the model's one reading of the local clock: while the run goes, the time since the start the run stamped, which moves between events, so a screen refreshes it on its own timer; once the run reported its end, the run's own wall time, frozen. While the run goes, a start whose `ts` does not parse gives no elapsed time rather than a guessed one.

An estimate is said as one. When a provider counted nothing for a call, the runtime estimates it, and `EstimatedTokens` (live) and `FinalEstimatedTokens` (at the close) say how much of the total is that estimate — null while every call was counted. Since every call of a run is on the meter (STUDIO-42), a reading can also come from a judge, a RAG pipeline, the manager or the planner: such a reading moves the meter and leaves `Model` and `Provider`, which name the model **the agents** work on — an agent's turn (`operation: agent`), or a script's `ctx.llm.*` call, which names its method. A reading that names no kind of work leaves them too: nothing says an agent made the call. Until an agent's call answers, the meter can move with no model named.

`Changed` fires once per event that moved the state and names its kind (`RunProgressChangedEventArgs.Kind`; null for an answer or reply accepted on this side), so a screen that shows no generated text can skip the per-token `llm.delta`.

### The status bar (STUDIO-34)

A third row at the foot of the window says what runs, what it spends and which tools are at work, without changing screen. `StatusBarViewModel` (`ViewModels/Shell/`) feeds it; it holds no view logic, and its whole behaviour is asserted in `Orkeon.Studio.Wpf.Tests`.

Three activities can run at once, each with its own engine, and each gets **a group of its own** while it runs — none hidden, none merged (DD-2):

| Group | On the bar while | Read from |
|---|---|---|
| **Run** | the Run screen's child process lives (`Launch.IsRunning`) | `Launch.Progress.Model`, the run's `RunProgressModel` (STUDIO-30) |
| **Test** | the Test screen's own launcher runs (`Test.Launcher.IsRunning`) | `Test.Launcher.Progress.Model` |
| **Create a team** — the assistant | the forge engine lives, working or waiting on the user | the wizard's progress card, `CreateTeam.Progress` |

A launcher's group says the team (read when the run starts, and kept if the launcher is aimed at another team meanwhile), the state — running, **waiting for an answer**, then succeeded or failed between the run's own end and the process exit —, the task in progress, the elapsed time, ↑ and ↓ (marked «≈» when the runtime estimated part of them), the cache chip, the vendor's real charge when there is one, the tools at work (their count and the first name; the whole list, each with the run's clock at its call, on hover), the delegations under way, and the provider and model of **the agents' calls**, as the meter reported them — a judge's, a RAG pipeline's or the manager's reading never renames them. During a run the bar never names a profile it supposed: each team picks its own. The assistant's group says the stage in the card's own words, ↑ and ↓ (marked «≈» when estimated), and what the session's token allowance has left (`budgetRemaining`, carried by the card as `TokensRemaining`). At rest the bar shows the provider and model of the **default** profile — there is no «active» one.

A segment nothing measured is absent, never a zero. The novice reads each group's state and meters, and the Balance; the expert reads everything, a launcher's other segments as one line the bar trims on a narrow window and shows whole on hover (D-03). The active groups share the width, so a narrow window trims their expert line and never pushes a group off the bar. A click on a group opens its activity's screen — Run, Test or Create a team; in novice mode, which has no Test screen, the Test group does not answer the click (D-04). Runs started outside Studio are out of scope (D-05).

The elapsed time moves between events, so the bar refreshes it on a one-second beat of its own — `IUiTicker`: a `DispatcherTimer` in the app, a beat that never fires in the tests and the screenshot campaign — kept only while a launcher's group is on the bar. The campaign pins the clock the elapsed time is read on (`StudioServices.Clock`), and its in-flight Run shot parks the scripted run before the line that reports its end, so the shot is of a run still in flight.

The **Balance** segment on the right says what the provider accounts have left — see *The provider balance*, below.

### The provider balance (STUDIO-35)

The **Balance** segment on the right of the status bar says what the provider accounts behind the model profiles have left, in both modes. The probe is STUDIO-33's (`IProviderBalanceProbe`, Core `Llm/`); the WPF side reads it through `BalanceReadings` (`ViewModels/Shell/`), which the bar, the profile rows and the profile editor share.

- **What it covers (D-01).** The accounts of the default profile, of the assistant's and of the profiles the teams name — one entry per account: a provider, the host of its endpoint and the variable its key lives in (`ProviderBalanceAccount`, Core). Two profiles on one account cost one request; the host is part of the account because Kimi's `.ai` and `.cn` platforms refuse each other's keys. A local runtime, or a profile with no endpoint, has no account and is not asked. The teams come in through one seam, `StatusBarSources.TeamProfiles`, which archiving (STUDIO-31) narrows to the active teams.
- **When it is read — the network policy (D-02).** At startup, once the profiles are loaded; when an activity ends — a run, a trial, a composition (the live consumption is the activity groups' business, STUDIO-34); and on a click. Nothing else: a window left open sends no request of its own accord, and a read already under way is joined, never repeated. The one exception is opt-in: Settings › Studio can read it again every 5, 15, 30 or 60 minutes — off by default. A provider whose documentation settles that it exposes no balance is answered without any request (STUDIO-33), so only DeepSeek, Kimi and OpenRouter accounts ever cost one.
- **What an entry says.** «DeepSeek 110.00 CNY» — the amounts as the provider returned them, never converted; an account not read yet shows its provider with the read-again glyph; a refused key, no answer or an answer out of shape is said quietly («Kimi · key refused»). The tooltip gives one line per account — the profiles on it, what it has left or why not, the time of the read — with the probe's own English detail under a failure. A click reads again; on a provider whose balance only its console shows (not exposed, or an admin key required), the click opens that console (`LlmPresets.KeyConsoleFor`).
- **The threshold (D-03).** An optional threshold per provider whose balance a key reads, in the currency that provider returns: under it, the amount takes the warning tone on the bar, on the profile row and in the editor.
- **The profiles (D-04).** Each profile row carries a balance chip once its account was read this session. The profile editor has a « Read the balance » line under « Test connection »: it reads the endpoint being edited with the key a run would present — the one typed and not yet remembered first —, refuses to ask without a key, and offers the console where only the console shows the balance.
- **Nothing is written down (D-05).** A reading lives in memory for the session: a figure read yesterday must not pass for today's, so a new window starts with none. No key is ever shown: the probe masks it (STUDIO-33 D-05), and the readings are masked once more where they are filed.
- **Who may reach a provider.** The balance is read without a click, so the HTTP probe is named in one place only: `MainWindowViewModel.CreateForCurrentMachine`. A window built without a probe in `StudioServices.BalanceProbe` reads nothing and shows no segment — every test that names none; the screenshot campaign passes an offline one (`OfflineBalanceProbe`), so its shots carry a balance and no request can leave.

### Settings › Studio (STUDIO-35)

A seventh settings tab, **Studio**, in both modes, holds how Studio itself behaves on this machine: today the provider balance card — the automatic reading, off by default, and the thresholds, with the last amount read beside each provider to say which currency to type. Its values are `StudioSettings` (`ViewModels/Services/`), kept in `ui-preferences.json` under a `Studio` section — never in the settings file the teams run on — and written at once through `StudioUiPreferences.PersistStudio`. Its second card is the archive suggestion of My teams (STUDIO-32, below): its switch, on by default, and its threshold, sixty days by default. A new setting is one more `StudioSettings` property, one more key in `UiPreferencesDocument`, one more card in `StudioSettingsView`.

`ui-preferences.json` is written **by merge**: `UiPreferencesDocument` (pure text in, text out, asserted without a disk) writes the keys a gesture owns and leaves every other key as it found it. The theme, language and mode written by the window's three gestures keep Settings › Studio, its writes keep them, and a key a later Studio adds survives an older one.

### Team folders end to end (remediation v2)

An adopted team's folders are part of the team: the sidecar `studio-team.json`
records them as mount strings (`mounts`), next to the display name, profile and
schedule. The "My teams" cards show them as chips; « Change the folders »
edits them in the team-mounts modal. A team associates a folder the settings
declare: « Allow another folder… » on an adopted team, and the wizard's « This
team's folders » block under the « Later » policy, open the « Add an allowed
folder » chooser, a checkbox list of the folders held in « Settings › Authorized
folders » (`Orkeon:FileSystem:Mounts`). The picked entries are carried over
verbatim, **rights included**: the settings are the single place a folder and its
rights are decided, and a team that could widen them would make that declaration
a suggestion. A row the team already carries, or whose virtual root another
folder already spends, says so and cannot be picked — a team's own list, like the
settings', names each root once. The one gesture that declares from the wizard is
the disk pick of « Existing folders » below, and it declares in the settings on
the way — the settings stay the source of the rights.

**A team is a folder one carries.** The sidecar records the team's own folders
relative to it: a physical segment that starts with `./` — `./input:/workspace:ro`,
`./output:/output:rw`, `./rapports:/rapports:rw` — names a folder inside the team
folder; one segment, `/` on both OSes, never `..`, never quoted. An absolute entry
is a folder of the user's, outside the team, and stays what it is; an entry nobody
can parse passes through untouched, both ways. The runtime resolves a relative
physical path against the process cwd and nothing else, so `TeamCatalog` is the
only place that knows the convention (`TeamMountPaths` in Core is its one helper):
it resolves relative entries to absolute paths when it describes a team
(`Describe`, `DescribeTarget`, hence `List` — the raw strings stay on
`Metadata.Mounts`), and relativizes on the way in (`SaveMetadata`, `SaveMounts`),
creating each relative folder there — the single point where an in-team folder is
materialised, at adoption and at every later edit alike. The cards, the launcher
and the folders modal receive absolute paths as before and never learn it. A
duplicate, an export and an import copy the entries verbatim and resolve them under
the copy; an older sidecar that recorded absolute paths under its own folder is
rewritten relative on the way — there is no rebase step any more, because a copy is
a safeguard, not a compatibility layer. `DeclaredMounts.IsInsideTeam` vouches for a
`./` entry even before the team folder exists (relative *is* inside the team, by
construction), and `MountValidator` skips the existence check of such an entry until
it is told which team folder to look under.

**Where the folders live is asked at step 1.** A fourth question, « Where are
your folders? », answers the two roots a team can address before it has a
blueprint — `/workspace` read (« Your documents ») and `/output` written (« The
results »), the only ones `DeriveMounts` can produce without one — and composing
never waits for it (`FolderPolicy`, `Later` by default). « Existing folders »
shows the two rows; « Choose the folder… » on either opens the **disk picker** on
the row's rights (read-only for the documents, read-and-write for the results),
and the pick is declared in « Settings › Authorized folders » unless the settings
already hold that folder, saved, then bound behind the row under the row's
rights — one gesture, the status line says which of the two things happened, and
a refused save still binds (`MainWindowViewModel.DeclareAndBindAsync`). A folder
picked inside the reopened team itself is bound and never declared: it is the
team's own, and the save relativizes it. « Created inside the team » answers both
rows team-relative on the spot — `./input:/workspace:ro`, `./output:/output:rw`,
read « inside the team: input / output » — and nothing is created on disk before
the adoption. « Later » behaves as before: no rows, the Composer step asks. The
chip moves the two canonical roots and nothing else; composing keeps them
(`KeepOnlyStepOneMounts` — the rest belonged to the blueprint being replaced),
« Restart », a resume and « Modify » on a card forget them and the policy with
them, so a stale step-1 choice never leaks into the sidecar of another team.
The two rows are a start, not a limit (owner review of 2026-09-19): « Add the
folder » names as many further mount points as the need calls for — a name the
agents will use (`/factures`, `/archives`), read or written — and each becomes a
row like the canonical ones, answered the same two ways (« Created inside the
team » answers a new one on the spot), listed on the Composer step next to the
blueprint's own roots, kept across compositions, and created inside the team at
adoption when left unanswered. Which of them the agents address is the
blueprint's business: the need has to name them. A name is normalized the way the
picker derives one from a folder (lowercased, one segment), and a name the runner
reserves or a row already carries cannot be added.

The wizard's block on the Composer step is **one line per mount point** (lot 3):
the name the agents address, who addresses it — provenance, never permission —
and the folder behind it, or two buttons when there is none yet. « Choose the
folder… » opens the chooser **targeted at that virtual path** (the disk picker
under « Existing folders »): the picked entry keeps its folder and its rights,
and only the name the agents use for it is the team's to choose. A targeted open
takes ONE folder and judges its rows where the pick will LAND, not on the root
the settings happened to declare — otherwise every folder the team already uses
elsewhere would refuse itself. « Create inside the team » is the other answer,
per row, and « Create every folder inside the team » answers every unanswered
row at once; under the « Created inside the team » policy a root a later
blueprint adds — `/rapports` beside `/output`, which step 1 could not foresee —
is answered the same way as it appears (`AnswerNewDerivedRoots`), unless the
user dropped it. A row inside the team shows the label, never a disk path, and
**never reads red**: `DeclaredMounts.IsVouchedFor` — declared in the settings,
or the team's own — is the rule of the wizard, of the "My teams" cards and of
the launcher alike. Without those gestures an agent-implied root could only ever
be answered at adoption, by a folder created inside the team and left empty:
which is why the card also says, while there is still time, that a reading team
with no folder chosen — or one answered « inside the team », whose `input/` is
created just as empty — will be given its own empty `input/` and that nothing
copies documents into it.

**The trial reads where the documents are.** `orkeon forge … --read <dir>`
mounts `<dir>` as `/workspace`, read-only, in place of the working directory and
changes nothing else — the session stays under the forge home, the settings still
resolve next to it. The wizard passes the folder bound behind `/workspace` on
every engine invocation (`ForgeStartRequest.ReadDirectory`, the seven sites; the
promotion keeps the workspace alone): a real folder as it is, a team-relative
answer resolved under the team folder once there is one — a reopened team
retries on its own `input/`, filled since — and nothing before the team exists,
where the argv is exactly what it was before the option and step 3 says the trial
runs on an empty folder. An engine that predates `--read` only ever meets it when
a folder is known, and then refuses it out loud on the failure card.

**A team's own folders are vouched for by living inside the team, and never written
to the global settings.** Two allow-lists would be one too many: declaring a team's
`output/` in `Orkeon:FileSystem:Mounts` would duplicate the sidecar in a file shared by
every team, and put one team's private folders in the list every other team picks
from. So the rule is implicit — `DeclaredMounts.IsVouchedFor`: declared in the
settings, *or* inside the team — and it is the one the launcher answers
(`BlockingFolders` never counts a team's own `/output` or `/workspace`). The settings
screen still has to show these folders, or the one screen that claims to list what
the agents can see would be missing the folders every adopted team writes into. So
« Settings › Authorized folders » ends, in both modes, with a read-only « Team
folders » section (`TeamFoldersViewModel`): one line per in-team folder of each
adopted team — « Veille concurrentielle · /output → output » with the one-word rights
badge — read from the sidecars' raw entries, the `./` ones and the absolute
under-the-team spelling of an older sidecar alike, never a folder outside the team,
never a disk path. No command, no ✕, nothing written: its hint says these folders
belong to their teams and are changed from « My teams » › « Change the folders ».
The section follows the team list — an adoption, an import, a deletion or a
duplication from a card, a save of the folders modal all end in a rebuild of the
cards, and it re-reads the sidecars on that signal and again when the folders tab
opens — so it never shows a team that is gone, and never misses one adopted a minute
ago.

Declaring is the settings' own gesture, and the chooser's « Declare a new
folder… » is one door to it: it closes and lands on « Settings › Authorized
folders », on that tab and not merely on that screen. One door, so a folder
cannot be declared from two places and drift between them. There is no in-app
picker any more (STUDIO-19): « Allow a folder » on the settings' novice card
opens the OS folder dialog, and the pick lands read-only under a virtual name
derived from the folder's own name (`MountDefinition.SuggestVirtualPath`, the
first free suggestion when that name is taken or unusable) — the card's rights
badge flips it to read-and-write. The wizard's « Existing folders » rows open
the same dialog on the row's rights and declare the pick **under the row's root**,
with an id of its own, then bind that very entry (VFS-90, D-01) — a second entry on
a root another folder already claims, told apart by its id, never a rename to
`/docs`; a writable extra folder at the Composer step goes through a named root.

A team folder that nothing vouches for — neither declared in the settings nor the
team's own — reads red, on the wizard's rows, the "My teams" cards and the
team-mounts modal alike. A team's `./input` and `./output` are the team's own:
created inside it at adoption, never declared, never red. Red is the one thing a
row cannot say by naming a virtual path, and a team reaching outside the machine's
authorized folders should not have to be discovered by reading a sidecar. Declaring
a folder in the settings un-reds it at once: the cards and the Run screen recompute
their verdicts on every edit of the declared list, and the window reads that list
from the per-user file before it builds them (STUDIO-18).

A team reaching outside the settings does not launch. « Run » refuses a team
carrying a folder that no settings entry allows: the run button is disabled and
the card names the folders and the two ways out (declare them, or take them off
the team), with a button onto « Settings › Authorized folders ». Discovering
that refusal from a run that failed halfway, its reason buried in a log, is the
outcome this replaces. The rule lives in `Orkeon.Studio.Core`
(`DeclaredMounts.BlockingFolders`) rather than in the WPF screens, so the TUI
launcher cannot answer it differently — and a team's own `/output` and `/input`
never block it: they are the team's plumbing, and counting them would make every
adopted team unlaunchable.

The folders the blueprint implies are removable like any other. They used to be
informative chips with no ✕ — "edit an agent to change them" — which left a team
carrying a root its owner did not want with no way to say so. Dropping one now
sticks: `SidecarMounts` no longer re-adds it, the same silent undo that method
exists to prevent. The screen warns and names the dropped roots, because nothing
will be bound to them and the agents writing there will fail; a single
« Restore » is the way back from a wrong ✕. What `SidecarMounts` records is
team-relative for every in-team answer and every root the blueprint addresses
that nothing answered (`./output:/output:rw`, `./input:/workspace:ro`); the save
creates the folders. At launch, the catalog reads the sidecar against the settings
(`TeamMountResolution.Resolve`) and `LaunchMountPlan.For` says what reaches the
command line: a settings declaration the team names goes as `--mount-id <ulid>` —
no path, the machine's own entry as it stands today; the team's own folders and
any copy the settings do not hold as recorded go as `--mount`, ahead of the
per-launch ones, so the chips and the command cannot disagree; an id this machine
does not declare blocks the launch (below). The `--allow-external-mounts` flag
follows the sidecar too: a team folder outside the team turns it on, a team whose
folders all resolve under it — the launch's working directory — needs none, and
the expert checkbox stays for the per-launch mounts. The engine places every
`--mount` **by virtual root** (`RunnerHost`): a team folder under a name the
settings spend on another folder replaces every settings entry of that root for
the run, and a folder under a new name is appended after the declared ones. The
Run screen's effective-mounts table predicts exactly that — one row per settings
entry, origin *appsettings* for what the settings provide, `--mount (replaces «…»)`
for a replacement, *selected by id among N* / *not mounted for this run* for the
entries of a shared root — and the sentence above it states the rule
(`MountOverrideSemantics`). Before this, the chooser's verbatim copy met its own
twin from the settings and every adopted team that used an allowed folder failed
at kickoff on "Duplicate virtual paths"; the settings' declared folders are also
whitelisted for `PathValidator` without any flag, so a team reading an allowed
folder outside its own directory is no longer refused file by file. Deliberate
limit: a bare `orkeon run` in a terminal does not read the sidecar — the crew's
own `mounts:` block is what it reads (VFS-90); like the `profile` field, the
sidecar is Studio's comfort, not the engine's contract.

### A mount has an identity (VFS-90)

Every entry of « Settings › Authorized folders » carries an **id** — the
26-character ULID before the `|` of its mount string,
`01J9Z3K4M5N6P7Q8R9S0T1V2W3|C:\data\out:/output:rw` — assigned by the editor at
load to an entry that has none and written at the next save, shown on the row
with a one-click copy for a hand-written crew's `mounts:` block. The id is what a
team's sidecar names its declarations by, and it is why two entries may now
declare one root: the owner's machine keeps experiment 09's `…\09\output:/output:rw`
and experiment 10's `…\10\output:/output:rw` side by side, and each team names
its own. The consequences, screen by screen:

- **The entry is authoritative** (D-01). The chooser opened for one mount point
  offers only the entries declared under that root — an entry declared as `/docs`
  reads « declared as /docs — this mount point is /output » and cannot be picked —
  and records the pick verbatim, id included. The disk pick declares the folder
  **under the row's root** (a second `/output` entry when one exists) and binds
  that entry; an equal entry — same folder, root and rights — is reused, never
  declared twice. Renaming to `/docs` to dodge a taken root is gone with it.
- **The sidecar keeps a copy** (D-02): `<ulid>|<folder>:/root:rights`, portable,
  the id winning over the copy on the machine that has it. A team's own `./output`
  carries no id (D-07). A sidecar written before ids resolves its copies by folder,
  root and rights, and « Change the folders » upgrades an exact copy to the entry
  itself on save.
- **The ids travel with the team** (D-06). « Duplicate », « Export » and
  « Import » copy them verbatim. A team naming a declaration this machine does not
  have — an import, or an entry removed since — reads « declaration … is missing
  on this machine » on its rows and in the folders tab, and « Run » refuses it
  naming the ids; the import review offers « Authorize them as recorded », which
  declares the copies **under the same ids** (a new id only when this machine
  already spends it on something else).
- **The settings say who depends on an entry**: each row reads « Used by … » from
  the sidecars, and removing an entry a team names asks first, naming the teams —
  they stop starting the moment it is gone. `MountValidator` files two entries on
  one root as information (`STUDIO-MOUNT-SHARED`), a shared root with an entry
  that has no id as the error the engine would raise, and one id on two entries
  as `STUDIO-MOUNT-ID`.
- **Agents never see an id**: `list_mounts`, the prompt's mount table and the
  access-denied messages name virtual paths only.

A team folder may hold a **single-file crew**. Every `examples/` crew — and every
crew written by hand — is one `config.yaml` or `crew.yaml` carrying `agents:` and
`tasks:` inline, not the promoted `crew/config.yaml` + `crew/agents/*.yaml` +
`crew/tasks/*.yaml` layout `forge promote` writes. The target detector
(`RunTargetDetector`) resolves a folder holding no layout marker and no script but
one `*.yaml`/`*.yml` file as that file: the run path is the file, the selected path
stays the folder, so the sidecar beside it and the launch directory keep working
exactly as for a promoted team, at the folder's root or under its `crew/` nesting
alike. Several YAML files resolve to `crew.yaml`, then `config.yaml`, and are
otherwise offered as candidates the way scripts are. This is what makes the whole
examples catalogue importable: « Import » runs that detector on the source before
copying anything, and a folder it cannot resolve is refused with the detector's
message in the status line rather than landing in « My teams » as a card nothing
can run.

**« Open the folder », at every step, in both modes.** The wizard's header offers
it as soon as the engine answered: the working session — which holds the generated
`crew/` — before the adoption, the adopted (or reopened) team afterwards, and the
tooltip says which. Same `IShellOpener` port as the team cards, gated the same way:
no opener wired, no button.

### The blueprint, edited by hand

The forge protocol's `edit` arbitration is real: interactive mode arbitrates
every verdict (a conforming one costs one "accept" click), and
`decision.made {edit}` followed by `blueprint.edited {blueprint}` re-renders the
crew deterministically — zero LLM tokens — then re-earns its verdict through the
unchanged validate/test/diagnose path; the engine re-validates everything it
receives (parse, compile, tool catalogue) and answers an invalid edit with a
recoverable `FORGE-BLUEPRINT-INVALID`, reopening the arbitration. In Studio, the
Composer step shows one card per blueprint agent with « Edit » opening the
agent editor — the name maps to the blueprint's `role`, "what it does" to its
`goal`, the capability chips to its `tools`; « Remove from the team » and
« Add an agent » travel the same path. The buttons are actionable at the
engine's two edit points: while it waits at its arbitration (the live channel —
decision `edit`, then the amended blueprint), and at the Composer step's dry
pause, where the engine is off — there the apply is a `forge resume --edit --dry`
child run carrying the amended blueprint as its first stdin line: the engine
validates it in full, re-renders deterministically (zero LLM tokens, same
iteration) and pauses again at the same boundary, so the Composer repaints with
the amended team. While the assistant composes or a trial runs, the buttons wait
with the engine.

The same dry pause carries a second answer beside « Try the team »:
« Adopt without trying » (`forge resume --adopt`), which moves the session
straight to Ready — offline, no run directory, zero tokens. `Ready` used to have
exactly one predecessor, an accepted verdict, so keeping the team as generated
required sitting through an execution that nothing downstream consumed:
`verdict.json` is optional at promotion and the generated `FORGE.md` already
knows how to say « no verdict recorded ». What the trial buys is *evidence*, not
permission, so the button's tooltip says exactly that, and the transition gets
its own trigger (`TrialSkipped`) so the session history never reads as a verdict
that was never earned.

Which engine answered is on screen too, beside the assistant's name («&nbsp;engine
1.0.0-rc.2&nbsp;», from `session.started`). Studio does not embed the CLI — it
launches whichever `orkeon` its locator finds first, co-installed, on `PATH` or
built from the checkout — so without that line a session driven by a stale binary
is indistinguishable from a working one that happens to have nothing to report.

### What a run costs, on screen (remediation v3)

Wherever a trial or a run finishes, Studio shows what it cost — as mono chips in
one shared recipe (`UsageMetricsFormatter`): total tokens, the prompt-cache hit
(`cache 62 % · 7 980 tokens` — the hit/miss pair is a *partition* of the prompt
tokens, never an addition), and the wall time. The wizard's verdict card reads
them off the enriched `verdict.ready` (the last trial's own figures, distinct
from the session-cumulative `cost.updated` meter); the launcher's finish line
reads the enriched `run.finished`; the history records tokens and the cache pair
per entry (tolerant schema — old files load) and shows them in the meta line.
A metric the providers did not measure produces **no chip** — never a zero.

### Modify, re-try, re-adopt (remediation v3)

Adoption is no longer a one-way door. « Modify » on a team card reopens the
wizard at the Composer step with the whole stepper reachable: the engine resumes
the team's session into a reopened arbitration (the stored verdict is re-announced
first), so agents are editable again, a new
`retry` decision re-runs the trial as-is (zero compose tokens, one budget
iteration), and re-adoption **updates the same team folder** — generated files
(`crew/`, launchers, `FORGE.md`, `schedule/`) are regenerated, the sidecar and
the user's own files survive, and renaming the team there only changes its
title — the folder is renamed with « Rename » (STUDIO-28, below). After an adoption the wizard is a blank step 1 again (STUDIO-20): modifying
an adopted team goes through « Modify » on its card, and the reopened arbitration
offers `retry`. Which session a team is linked to is the engine's answer, never
Studio's (STUDIO-25): « Modify » always runs `forge reopen <team-folder>`. A session
carries a stable id, announced on `session.started` and copied by its promotion into
the team's `forge.json`, and one rule decides the link — rule R,
`Orkeon.Domain.FileSystem.TeamSessionLink`, the same for the CLI and Studio: the
session carrying the folder's id is linked when its `promotedTo` designates the
folder, or when the folder it designates is gone or no longer carries the id — the
team was moved or renamed, and the session follows it; when `promotedTo` designates
another existing folder carrying the same id, this folder is a copy, linked to
nothing. No path decides the link on its own. A team no session is linked to —
imported, whose session was deleted, duplicated, or without an id — is modifiable
too (FORGE-09): `forge reopen` rebuilds a session from the team's own `crew/` (brief
from the promotion's `forge.json`, derived from the plan otherwise), writes the new
session's id into the folder's `forge.json` — a duplicated team thereby becomes
independent and can never reach its original's session — and parks it at the dry
pause; the wizard reads the session off `session.started` / `team.reopened` and
opens the Composer without an engine, as after `--dry` — amend an agent, try the
team, or keep it as it is, then re-adopt onto the same folder. The card only gates
the button: « Modify » is offered when the team's `forge.json` names a session
(`TeamSummary.ForgeSessionId`) or its YAML crew can be read back
(`TeamSummary.HasYamlCrew`); a team with neither (a script crew, a foreign layout,
no record) keeps it disabled, the tooltip saying why; the tooltip also says when the
reopen goes through a rebuilt session. Discarding a session under « Sessions in
progress » while the wizard is open on it ends that
creation as well: the wizard goes back to the blank step 1 of « Restart » (a running
engine is stopped first) rather than keep a Composer over a directory that no longer
exists; a session it is not open on leaves it untouched. « Modify » brings the wizard
forward on the click itself, before the engine has answered — the reopen, a rebuild when
no session is linked, takes a moment, and the screen used to move only once it was over
(the owner's « two clicks », 2026-09-21); the reopen shows as the engine working. A click
on « Modify » or « Resume » while the engine is busy on another creation is refused in words on the
wizard's status line, nothing stopped, instead of being dropped. A forge run's task
completes only once its epilogue has landed on the UI thread, and with it every event
posted before: WPF resumes an await begun in an input handler at Send priority, above the
Normal priority the reader thread's posts travel at, and the rebuild used to read the
session off a model the events had not reached yet — step 1, with the session on disk for
the second click to find. The disk is the fallback when the stream announces nothing —
the session named by the id the engine left in the team's `forge.json`, taken only when
rule R links it to that very folder, so a copy never lands on its original's session — and
a card says so when neither has it.

### Adopting under the team's name (STUDIO-26)

The adoption hands the engine the team's name: `forge promote --name` titles `FORGE.md`,
`forge.json` and the session, and once the promotion is written the engine renames the
session folder after the team folder (`-2` when another session already holds the name) and
says so on `session.renamed`, which moves the wizard's session slug and directory with it; the
schedule artifacts and the launchers carry the team folder's name as well. A rename the disk
refuses leaves the adoption standing: the engine's `warning` joins the one line the wizard
leaves after an adoption, and the id keeps the link. Before promoting a **new** team the wizard
looks at the folder first: when something already occupies it — a team, a folder holding
none, a file (`TeamCatalog.OccupantOf`) — step 4 says what, offers a free name whose folder is
the taken one suffixed `-2`… (`TeamCatalog.FreeSibling`), and, when a team holds it, « Open
the existing team », which brings My teams forward. The engine is not asked until the name is
free, so its refusal of a non-empty destination never reaches the screen. A re-adoption writes
into its own team's folder, which is no collision.

### The schedule, installed and stopped (STUDIO-27)

Studio installs and removes a team's schedule itself, with the user's consent. The operating
system runs the team — Orkeon still has no scheduler of its own — and the CLI, which owns the
artifacts, does the operating system's part: `forge schedule`, `forge schedule --check` and
`forge unschedule` ([CLI reference](../reference/cli.md#orkeon-forge)). Studio never runs
`schtasks`, `systemctl` or `crontab` itself; it reads the engine's `schedule.state` event, or its
`error`, whose `command` is what a person can run by hand. After adopting a scheduled team, the
wizard asks under its status line « Install the schedule (every day at 08:00)? »
(`ScheduleOfferViewModel`): « Install » runs `forge schedule` on the new folder, « Later » leaves
the team installable from its card, and the outcome stays on screen as one line — with the
manual command when the system refused. A re-adoption asks only when the schedule is no longer
installed as declared (it checks first); a re-adoption « on demand » of a scheduled team stops the
schedule before promoting — choosing on demand is the consent — and a refusal stops the save, the
manual command in the status line. The « My teams » card shows the real state, the engine's answer
and never the sidecar's: « Scheduled » — the badge turns green only then — « Schedule not
installed » or « Schedule to reinstall » (moved, renamed, rescheduled or disabled), both amber;
before the engine answered, the card claims nothing. The state is checked at startup and after
each gesture — an adoption, an import, a duplication, an install, a stop — and every refresh lays
the answers back on the rebuilt cards without asking again. The card carries two actions:
« Install the schedule » (a declared schedule nothing runs, or one to reinstall) and « Stop the
schedule » (`forge unschedule`, then `schedule` is removed from `studio-team.json`: the team is on
demand). A refusal changes nothing, and the card shows what to run by hand.

### Deleting a team leaves nothing behind (STUDIO-27)

The in-place delete banner stops the team's schedule first — declared in the sidecar, or recorded
as installed in its `forge.json` (`TeamSummary.HasSchedule`) — through `forge unschedule`; when
the system refuses, the team stays and the banner says why, with the command to run by hand. The
banner offers « Also delete the workshop session », ticked by default, for the session rule R links
to the team (`ForgeSessionCatalog.LinkedSession`): rule R links a copy to no session, so deleting a
copy never offers — nor deletes — its original's. The folder goes, then the session, announced to
the wizard, which forgets it if it was open on it. The Diagnostic screen lists the orphan workshop
sessions (`ForgeSessionCatalog.FindOrphans`): adopted sessions — listed nowhere else — whose
`promotedTo` folder no longer exists; a team moved or renamed inside the teams directory is found
by its id and is not one. Each row has a « Clean » that asks in place before deleting
(`DiagnosticViewModel` takes the forge workspace and the teams root). A team moved outside the
teams directory can show there too: nothing is deleted without the user.

### Archiving a team (STUDIO-31)

Archiving takes a team out of the active list without deleting it, and gives it back intact. It is
a **flag** in `studio-team.json` — `archived`, and `archivedAt` for the date — and nothing else: the
folder does not move, so nothing that points at it breaks — its path, its workshop session's link,
its scheduled task, its history. `TeamCatalog.List` takes a filter (`TeamListFilter`: `Active`, the
default; `Archived`; `All`) and each screen asks for what it shows: My teams reads every team and
splits the cards (`Teams`, the active ones, which the sidebar counts; `ArchivedTeams`); the Test
picker and the Balance segment of the status bar cover the active teams; « Used by » of a folder or
of a profile counts every team — removing what an archived team names would break it the day it is
restored — and Settings › Team folders lists every team, an archived one marked « (archived) ». The
list never shows a dot folder (an engine's workspace state, a VCS folder) nor, on Windows, a hidden
or system folder, and a duplicate or an import that fails halfway deletes its partial folder rather
than leave a card nothing can run.

The sidecar is **merged, never rebuilt** (`TeamCatalog.UpdateMetadata`): an adoption, a re-adoption
after « Modify », a change of folders write the fields they own and keep the others — the archive
flag, the last run. A duplicate or an import is a team in use and comes out active; an export carries
the flag as the team has it, and importing it clears it anyway. And a copy's arrival is its activity
(STUDIO-32): a fresh copy of an old team is not an old team, so the copy forgets the last run its
original's sidecar recorded and stamps `addedAt` — in a minimal sidecar when it came without one.

**Last activity.** At the end of a real run from the Run screen — never a trial, never a
`--validate` — `RunSession` stamps `lastRunAt` into the team it ran: only a team folder right under
the teams root that already has its sidecar, the target being the folder or a file inside it
(`DeclaredMounts.TeamDirectoryOf`); a launch pointed elsewhere writes nothing. A team's **last
activity** is the most recent of four dates — `lastRunAt`, its latest launch-history entry (the
history keeps fifty), the `promotedAt` of its `forge.json`, and a copy's `addedAt` (STUDIO-32) —
computed when the list is read, with no migration (`TeamSummary.LastActivity`,
`TeamCardViewModel.LastActivity`). A card's last run is its latest history entry, or the sidecar's
`lastRunAt` once the history forgot it — the date alone, the outcome being the history's.

**Nothing relaunches an archived team by mistake.** Studio neither launches nor tests one, and each
guard offers the way back rather than refusing in silence — « Archived team — restore it? »: the
Run and Test screens keep the run and the dry run off under a banner with « Restore »
(`TargetDescription.IsArchived`), and read the archived state again right before a launch, so a team
archived behind their back — its sidecar edited by hand, a second window — is refused there too
(STUDIO-32); « Replay » in the History reads the entry's own target — not the
form's — and, on an archived team, runs nothing: its card asks in place; the card's Test icon, which
bypasses the Test picker, asks too; the picker itself lists the active teams only. **The rules**:
archiving a scheduled team — declared, or recorded as installed — is refused unless « Stop the
schedule and archive », which runs `forge unschedule` first (STUDIO-27); archiving and restoring are
refused while the team is the target of a run in flight on the Run or Test screen, or open in the
wizard — the busy-team seam shared with the rename (STUDIO-28: `LaunchTabViewModel.RunningTarget`,
`TeamsDependencies.ActivityOf`). A restored team simply takes its place again in the order. Every
archive and restore refreshes the Test picker and the two launchers.

**Archiving is Studio's notion.** `orkeon run <folder>` and the terminal launcher
`orkeon-studio-run` run an archived team like any other, and a run they start — or one the operating
system starts — does not stamp its last run. The screen around it is the next section (STUDIO-32).

### My teams: finding a team among many (STUDIO-32)

My teams stays readable with many teams. Above the cards, three controls work on the cards already
in memory — never through `Refresh()`, which resets the list and makes the shell re-read the
settings' team folders and the mount references:

- **the search** reads a team's name and its need: every word typed must start a word of them,
  accents and case aside — the rule of the use-case gallery, one helper for both (`TextSearch`);
- **the order** is the last activity, most recent first — a team whose activity is unknown last — or
  the name. There is no « creation date » order: no reliable date says when a team was made;
- **« Archives (N) »**, shown while something is archived, switches the list to the archived teams
  (with a line saying what an archive keeps) and back. It closes by itself when the last archive
  is restored or deleted.

The sidebar counts the **active** teams (`TeamsViewModel.ActiveCount`): neither the search nor the
Archives view moves it. The empty screen tells three states apart: no team at all (create or
import one), every team archived (« Open the archives »), and a search that found nothing (said,
with « Clear the search »).

**Archive, then undo.** An active card offers « Archive » — a labelled button in novice mode, an icon
in expert mode — and asks nothing: the archive is undone in one click. A banner « Team “…”
archived — Undo » holds the top of the screen for a few seconds, on a timer of its own
(`StudioServices.UndoDelay`): dropping a pause cancels everything pending on its timer, and the
chat's holds the assistant's beats. A scheduled team goes through « Stop the schedule and archive »
and a busy one is refused on its card, as STUDIO-31 set out. Undoing « Stop the schedule and
archive » brings the team back, not its schedule — the banner says so: the schedule the user chose
returns to the sidecar, the registration the engine removed does not, and the card shows « Schedule
not installed » with its « Install the schedule »; nothing is reinstalled behind the user's back.
An archived card offers « Restore » and « Delete » and nothing else — no launch, no modify, no
rename, no copy — under a muted « Archived » badge and the date it was archived. One question on
screen at a time: opening a card's question closes the undo banner and any other card's question.

**The archive suggestion (DB-1).** A banner proposes « N teams not launched for 60 days — archive
them? », naming them. It only proposes: nothing is archived without the click, which archives
exactly the teams named, each read again at that moment (one running since is refused on its card),
and one undo brings them all back; « Not now » puts the proposal away for the session. It never
lists a scheduled team — the system runs it without Studio — nor one whose last activity is
unknown, and it waits for the launch history, which may know a later run than the sidecars. It
steps aside while a card asks something or the undo banner shows. The threshold and the switch that
turns it off are in Settings › Studio (`StudioSettings.ArchiveSuggestion`, `ArchiveSuggestionDays`,
written into `ui-preferences.json` by merge), and a change applies at once.

### Renaming a team: its title and its folder (STUDIO-28)

A team has two names. Its **title** is what the cards, `FORGE.md`, `forge.json` and the session
show; its **folder** is where it lives, and what everything else points at — the linked session,
the launch history, the schedule the system runs, the schedule artifacts, the launchers.
« Modify » changes the title only: the wizard it reopens re-adopts into the very folder the team is
in, and step 4 says so under the name field — to rename the folder, use Rename in My teams.
« Rename », a labelled button of the card in both modes, opens an editor in place of the action
row — no dialog — and runs `forge rename <team-folder> --name <name>` in the workshop's workspace
([CLI reference](../reference/cli.md#orkeon-forge)): the engine moves the folder to the name's own
(the one folder rule), the linked session follows it, every title and generated file takes the new
name, and a schedule the system runs is reinstalled under it — all of it or nothing, a failed step
putting back everything done before it. Studio refuses it, in the editor, while the team is the
target of the run in flight on the Run or Test screen (`LaunchTabViewModel.RunningTarget`: the path
the run started on, never the live picker) or open in the wizard (`CreateTeam.ReopenedTeamPath`) —
the busy hook `TeamsDependencies.ActivityOf`, which archiving shares — and when the new name's
folder is taken, saying what holds it in the wizard's own words. A run started outside Studio (the
CLI, the system's scheduler) is invisible to it: the engine's undo is what protects that one. Once
the engine answered, Studio rewrites what is its own: the launch history, whose entries of the
former folder — target, working directory, settings file, arguments — are spelled under the new
one, so the card keeps its last run and « Relaunch » replays where the team is; a launcher aimed at
the former folder follows it. An allowed folder the settings declare by an absolute path inside the
former folder pointed into the team and now points nowhere: the line after the rename says so, and
the settings are never rewritten. Team-relative folders (`./input`, `./output`) need nothing — they
are the team's own, and they moved with it.

### Tools and MCP in the settings (STUDIO-21)

Two tabs the settings screen lacked. **Tools**, open to both modes, is three cards. The
tool keys: one row per key a tool needs — the Tavily key of `web_search`
(`ORKEON_TAVILY_API_KEY`, the spelling the secret chain reads) and the Brave key of
`brave_search` (`BRAVE_API_KEY`, read as-is by the runner host) — on the same rows and
the same store as the API keys of the model tab (`SecretRowViewModel`, `IApiKeyStore`: the
value goes to the user environment, never to a file, and the row says where a key is
issued). The catalogue: every tool `orkeon run` registers, by family, each as a chip, and
under each family one line per tool that needs something — a key remembered above, a
tool present only once its key is (`brave_search`), a key given at the call by the agent
(`image_generation`), connection parameters given at the call (the database and graph
tools), an expert setting below (`shell_command`). The catalogue is declared in Core
(`ToolCatalog`): the framework carries no "required settings" metadata and its registry
lists names only, so the list is the `orkeon run` column of the availability matrix in
`docs/tools/inventory.md`, and a test pins every name against that file. The expert card:
the `shell_command` allow-list (`Orkeon:Tools:Shell`), the interpreters switch and the two
command lists, one command per line — never written as an empty array, which the runtime
would read as "block every command".

**MCP**, expert only, is the `MCP` section: the switch, and one card per server under
`MCP:Servers` — identifier, transport (`Stdio` or `Sse`, the runtime's spelling), command
and arguments and environment for a stdio server, URL for an HTTP one. Every keystroke
writes in place through `McpSection`, so a key Studio does not model survives an edit of
the server that carries it, and a rename moves the whole node. Each row says its own
problem the way the validator will refuse the save (`STUDIO-MCP-*`): an identifier the
binder would mangle, a stdio server without a command, an HTTP server without an absolute
http(s) URL; a value of the environment block that reads as a secret is reported at
information level, since the file is clear text and the server inherits the user
environment. The section is honoured by the runner: `orkeon run` connects the declared
servers before the crew loads (see [MCP integration](./mcp.md)), which is what makes the
tab worth having — until then nothing read it.

## Localization: the `IStudioStrings` port

`Orkeon.Studio.Core` defines a localization port, `IStudioStrings` (`Localization/StudioStrings.cs`): a key indexer plus a `CultureChanged` event so ViewModels can re-emit their bindings when the language switches. The English defaults in `EnglishStudioStrings` are the key registry of record. Each front decides the language: the WPF app bridges the port onto its resx-backed `I18n` service (`I18nStudioStrings`, `Strings.resx` plus the `fr`, `es`, `de` and `zh-Hans` satellites) with a **hot five-language switch** relayed via `CultureChanged`, driven by `LanguageSelectorViewModel` — the system language is detected and deliberately never persisted, only an explicit pick is recorded; the TUIs keep the English default. Deliberately untranslated, by CLI-contract policy: `orkeon doctor` check details, LLM probe results, exit-code descriptions and the `VALIDATION OK/FAILED` verdicts — translating Studio's copy would desynchronize it from what the CLI prints in a terminal. Validator messages and doctor check names follow a revised split: the raw English line stays as the expert detail (tooltip or mono side label), and a per-code plain-language overlay is what the lists show first — `Studio.Diagnostics.Code.<code>` for validator messages, `Studio.Diagnostics.Check.<name>` for doctor checks, both under the single `Studio.<Screen>.<Label>` key convention the parity tests enforce.

## Where Studio keeps things

Two roots, one rule: **application state** lives in the per-user config directory,
**documents** live in the user profile. Nothing is ever generated into the working
directory, and API keys live in neither — they stay in the user's environment
variables, never in a file.

**The key is typed in one place only.** The paste-a-key row (`IApiKeyStore` /
`EnvironmentApiKeyStore`, shared by the model tab and the tool keys of STUDIO-21) exists in
the **WPF app alone**: the value goes to the user environment
(`EnvironmentVariableTarget.User` plus the process, so the session and every child it spawns
see it at once), which persists across sessions on Windows and is a documented no-op on
Unix — where the WPF app does not run. The two TUIs carry no key field at all: on Linux and
macOS the variable is set by the operator's own shell. Which variable, per provider, and the
three names it gets confused with:
[API keys: the variable per provider](../reference/llm-providers-comparison.md).

**`%APPDATA%\Orkeon\`** (`$XDG_CONFIG_HOME/Orkeon/` elsewhere) — application state:

| Entry | What it is |
|---|---|
| `appsettings.json` | the per-user global settings — the durable base every launch composes on |
| `studio-model-profiles.json` | the named model profiles (provider, model, URL, temperature, response budget, timeout, thinking switch and effort, **key env-var name only**) |
| `studio-history.json` | the launch history the History screen and the team cards read |
| `.orkeon\forge\<slug>\` | the **atelier sessions** — resumable works-in-progress (brief, blueprint, provisional `crew/` render, trial `runs/`), not adopted crews. The dot-name is the engine's workspace-state convention (SPEC §4.1, like `.git`): Studio hands `%APPDATA%\Orkeon` to the engine as its forge workspace, so `forge resume <slug>` works identically from a terminal and from Studio |

**`%LOCALAPPDATA%\Orkeon\Studio\ui-preferences.json`** — window comfort: mode,
language, theme, and Settings › Studio (STUDIO-35's balance, STUDIO-32's archive suggestion). Apart from the two roots above: it is
written by the WPF app alone (Windows-only), in the **non-roaming** local application data;
every write merges into what the file holds, and every disk touch is tolerant — a missing,
corrupt or unwritable file degrades to the defaults, never to a crash. A balance read is
never written here, nor anywhere else.

**`%USERPROFILE%\Orkeon\teams\<slug>\`** — documents: the adopted teams. Each is an
ordinary, self-contained folder (crew definition, `run.cmd`/`run.sh`, the
`studio-team.json` sidecar with name, need, profile, schedule and mounts — and, once they
apply, the archive flag and the date of the last run from Studio, STUDIO-31, and the arrival of a
copy, STUDIO-32) — copiable,
shareable, deletable, runnable with `orkeon run <folder>` alone. The `<slug>` is the
team's name through the folder-name rule the engine also names its sessions with — one
implementation, `FolderSlug` in `Orkeon.Domain.FileSystem`: lowercase ASCII, accents
dropped, one dash between words, cut at a word under 64 characters; a name that keeps
no ASCII letter or digit (one written in Chinese, say) gives `equipe`. Adoption *moves* a
session's result from the state root to the documents root; that is the boundary
between a draft and a deliverable. In the sidecar, `name` is normalized at write
(one line, Markdown stripped, cut at a word under 64 characters — the slug's cap;
import applies the same rule to the copy it makes) while `description` is the whole
need, untouched: the one-paragraph summary the cards show is derived at read time
(`TeamCatalog.Summarize`) and never stored.

## How Studio ships

Studio is installed **next to the CLI** by the release packages — see the [publication matrix](../reference/publication-matrix.md) for the exact artifacts and [Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md) for the walkthrough:

- **Windows** — the `orkeon-cli-<version>-win-x64.zip` and the per-user MSI carry the WPF desktop app (`orkeon-studio`, self-contained); the MSI also registers an "Orkeon Studio" Start-menu shortcut.
- **Debian/Ubuntu** — the `.deb` and the Linux multi-app archives carry the two terminal apps (`orkeon-studio-config`, `orkeon-studio-run`), self-contained.
- **macOS** — CLI-only on the onboarding channel in V1; the multi-app `osx-*` archives do carry the two TUIs (only the WPF app has a RID filter), untested on macOS in V1.

The two TUIs target plain `net10.0` and are therefore cross-platform builds; only `Orkeon.Studio.Wpf` is Windows-bound (`net10.0-windows`, WPF).

## What Studio is not

- **Not a NuGet package** — all four projects set `IsPackable=false`; the only distribution channel is the release installers.
- **Not a separate engine** — Studio never re-implements a workflow: it edits the CLI's settings file and spawns the CLI itself (`OrkeonProcessRunner`), so its results are exactly `orkeon run`'s.
- **Not a scheduler** — a scheduled team is run by the operating system; Studio asks the CLI to install and remove the registration, with the user's consent (STUDIO-27).

---

> **See also**: [Publication matrix](../reference/publication-matrix.md) ·
> [Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md) ·
> [Back to index](../INDEX.md)
