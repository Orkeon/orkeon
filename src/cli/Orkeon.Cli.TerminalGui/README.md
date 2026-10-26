# Orkeon.Cli.TerminalGui

Terminal.Gui v2-based split-pane console for Orkeon interactive runners.
Routes `ILogger` writes into a dedicated **logs pane** at the top of the screen
and the REPL into the **bottom pane** — so log lines no longer interleave with
the prompt.

## Install

This project is no longer distributed as a NuGet package — reference it from source (`ProjectReference` inside this repository); its assembly ships through the release artifacts. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).


## What it does

- Implements `IConsoleAdapter` (from `Orkeon.Cli.Abstractions`) on top of a
  Terminal.Gui v2 split-pane window. Existing runners using the abstraction
  (`MainMenuRunner`, `QaRunner`) get the new UI for free.
- Provides an `ILoggerProvider` that pushes log entries into the logs pane
  with a stable `HH:mm:ss [LVL] CategoryShort: Message` format.
- Publishes itself as a process-wide `AmbientLoggerProvider` so child
  `IHost`s (e.g. spawned by `RunOneShotAsync` for a verify command) re-route
  their `AddSimpleConsole` writes into the same pane instead of polluting stdout.
- Adds keyboard shortcuts via a `StatusBar` (every shortcut is bound at the
  application level via `Shortcut.BindKeyToApplication = true` so focused
  TextFields don't swallow them):

| Shortcut | Action |
|---|---|
| `Ctrl+L` | Clear logs pane |
| `Ctrl+K` | Clear REPL history |
| `Ctrl+F` | Find in logs (modal) |
| `Ctrl+G` | Show / hide logs pane (REPL fills the screen when hidden) |
| `Ctrl+↑` / `Ctrl+↓` | Resize the split (±5 %) |
| `F2` | More log details (lower threshold, towards Trace; clamped) |
| `Shift+F2` | Less log details (higher threshold, towards Error; clamped) |
| `Esc` `Esc` (prompt) | First `Esc` clears the draft; the second, right after it, interrupts the prompt's turn |
| `Ctrl+C` | Copy the selection (never cancels, never quits; without a selection it prints a one-line hint) |
| `Ctrl+V` | Paste the clipboard into the input |
| `↑` (prompt, first visual row) | Enter the history: the typed text is kept as the draft, the newest entry is shown; in the history, `↑` older, `↓` newer, then the draft back; an edit leaves the history and keeps the text |
| `↓` (prompt, last visual row) / `F4` | Focus the tasks list (only while it holds a task other than `main`) |
| `↑` / `↓` / `Home` / `End` (tasks list) | Move the selection (no wrap); `↑` on the first row returns to the prompt |
| `Esc` / `F4` / `Tab` (tasks list) | Return to the prompt, text and caret restored; after `Esc` only (not `F4` or `Tab`), a second `Esc` right after it interrupts the prompt's turn |
| `Ctrl+V` / right-click (tasks list) | Return to the prompt and paste at the restored caret |
| click / double-click (tasks list row) | Focus the list on that row / and print its detail |
| `Enter` (tasks list) | Print the selected task's detail; sends nothing |
| printable key (tasks list) | Return to the prompt with the character typed |
| `Home` / `End`, `Ctrl+Home` / `Ctrl+End` (prompt) | Start / end of the line, of the text |
| `Ctrl+Q` | Quit (with confirmation dialog if a command or the prompt's turn is running) |

The active log level is shown in the Logs pane title in real time
(`Logs — Level: Information`).

## Copy / paste — including Docker and SSH

Clipboard traffic goes through `Layout.TuiClipboard`, a hybrid built for hosts
where Terminal.Gui's OS clipboard integration is unavailable (a Docker container
or an SSH session has no xclip/X11 and no `powershell.exe`):

- **Copy** (`Ctrl+C` on a selection, `Ctrl+A` select-all, or mouse drag-select —
  in the transcript and in the input — auto-copies on release) fans out to the OS clipboard when supported, to the
  **hosting terminal via OSC 52** (Windows Terminal, iTerm2, kitty… set the
  *host* clipboard even across docker/ssh), and to an in-process cache.
- **Paste** (`Ctrl+V` or right click) reads the OS clipboard when available,
  else the in-process cache (so copy → paste round-trips inside the TUI).
  `Ctrl+V` is an application-level key handler: it pastes into the input
  whichever view has focus, and stands down while a modal dialog has focus.
- **Paste from the host OS** when the container cannot read the host clipboard:
  use the **terminal's own paste** — `Ctrl+Shift+V` (or the terminal's menu) —
  which arrives as bracketed paste and is inserted natively by Terminal.Gui.
- **Select with the terminal instead of the TUI**: hold **Shift** while
  dragging — the terminal bypasses mouse tracking and its native
  `Ctrl+Shift+C` copies without involving the app at all.

> Windows Terminal note: plain `Ctrl+V` is a Windows Terminal *paste* binding —
> it never reaches the app; the pasted text arrives as bracketed-paste input,
> which lands in the focused field. Both routes end up doing the right thing.

Color scheme: `Orkeon.Cli.TerminalGui.Layout.SchemeFactory` builds the schemes
from semantic roles (`PaletteRole`): Normal (the terminal's own foreground),
Dim, Accent, Ok, Alert and Error, plus bold and italic. Every attribute keeps the
terminal's background (`Color.None`), so the console follows the user's theme;
only the selection keeps explicit black on white. A terminal announcing 24-bit
colour through `COLORTERM` (`truecolor` or `24bit`) gets an RGB palette, any
other a named 16-colour one. Main text carries no colour and a line holds at most
one vivid role (the model witness keeps its own availability colour).

## How to wire it

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.TerminalGui.Hosting;

// 1. Parse the --ui flag (tui|plain|auto). null = auto.
UiMode? requestedUi = UiFlagParser.Parse(args);
UiMode effectiveUi = TtyDetector.ResolveEffectiveMode(requestedUi);

// 2. Conditionally wire Terminal.Gui in DI + logging pipeline.
var host = Host.CreateDefaultBuilder()
    .ConfigureLogging(b =>
    {
        if (effectiveUi == UiMode.Tui)
        {
            // Strip Console/Debug/EventSource providers — they fight Terminal.Gui's alt-screen.
            b.ClearStdoutLoggersForTerminalGui();
            // Let everything reach the provider; visible filtering is owned by F2/Shift+F2.
            b.SetMinimumLevel(LogLevel.Trace);
        }
        else
        {
            b.AddSimpleConsole();
            b.SetMinimumLevel(LogLevel.Warning);
        }
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<MyRunner>();
        if (effectiveUi == UiMode.Tui)
            services.AddOrkeonCliTerminalGui();
    })
    .Build();

// 3. Run. In TUI mode the runner runs on a background task,
//    Application.Run owns the main thread. Pass the runner (not just the func)
//    so the TUI can drive the Esc Esc interrupt and Ctrl+Q confirmation.
var runner = host.Services.GetRequiredService<MyRunner>();
if (effectiveUi == UiMode.Tui)
{
    await using var tuiHost = host.Services.GetRequiredService<TerminalGuiHost>();
    await tuiHost.RunAsync(runner, CancellationToken.None);
}
else
{
    await runner.RunAsync(CancellationToken.None);
}
```

`MyRunner` should derive from `InteractiveRunnerBase` (which implements
`IInteractiveRunner`). The base class exposes `IsCommandRunning` and
`RequestCommandCancellation()` so the TUI can soft-cancel a synchronous command
via `Esc Esc`.

## TTY auto-detection

`TtyDetector.IsInteractiveTty()` returns `false` when:

- `Console.IsInputRedirected` or `Console.IsOutputRedirected` is `true` (pipe, redirect),
- `CI=true` is set in the environment (GitHub Actions, GitLab, etc.),
- `TERM` is unset on Linux.

When `auto` resolves to `Plain`, the runner skips `AddOrkeonCliTerminalGui()`
entirely so the legacy `SystemConsoleAdapter` path runs unchanged.

## Driver selection

`TerminalGuiHost.Initialize()` explicitly picks the driver:

- **Linux/macOS** → `DOTNET` (uses `System.Console`, works in WSL, devcontainers, plain xterm).
- **Windows** → `WINDOWS` (native console API).

Why not the auto-default (`Application.Init("")`)? It picks ANSI on Linux,
which emits no output in WSL/many container terminals — `Application.Run`
blocks forever on an invisible UI (TUI-13).

`Console.TreatControlCAsInput` is set to `true` after `Application.Init` so
Ctrl+C reaches Terminal.Gui's input loop instead of being intercepted by the
.NET runtime as SIGINT.

## Cancellation model

Ctrl+C never cancels and never quits: it copies the selection, and without
one prints `Ctrl+C or Ctrl+Shift+C copies a selection — esc esc interrupts the turn.` The
interrupt is `Esc Esc` on the prompt, and it targets the prompt's turn only:

- **`Esc`** on the prompt clears the draft and arms the pair (also on an empty
  draft). **A second `Esc`** right after it requests cancellation of the turn
  submitted from the prompt, running as a synchronous command or as a
  dispatched ticket, an LLM retry backoff included. Visible feedback appears in
  the REPL pane, and the hint bar shows `esc esc to interrupt` while that turn
  runs. Any other key between the two `Esc` disarms the pair.
  Effective only if every awaited layer in the command's call chain honours
  the `CancellationToken` (Crew → Agent → ToolCall → LLM HTTP). The HTTP
  base correctly propagates; some intermediate orchestration layers may not
  yet — tracked in TUI-20.
- **No key cancels a background crew** (a detached `runCrewAsync`, compaction,
  indexing): it keeps running and stays reachable through `/ps` and
  `/cancel <ticket>`.
- **`Esc`** in the tasks list (`↓` or `F4`) returns to the prompt and cancels
  nothing, a turn running or not. It arms the pair like an `Esc` on the
  prompt, so `Esc Esc` from the list interrupts a running turn.
- **The list's state follows the focus.** `Tab` leaves the list like `Esc`;
  `Ctrl+V` or a right-click leaves it and pastes at the restored caret; a click
  on a row focuses the list on that row (the `❯` cursor is never drawn while
  the keys go to the prompt); focus reaching the prompt any other way leaves
  the list and keeps the current text.
- **The selected task is the recipient** of the next message: the placeholder
  reads `Message @<task>…` and the separator above the prompt ends with
  `── <task> ─`; selecting `main` gives the default recipient back. Sending to a task goes through
  `TuiIntegration.SendToTask` (`(ticket, text) → bool`); while the host leaves it
  null, `Enter` prints that sending to a task is not supported yet and keeps the
  text in the field.
- **Ctrl+Q** without a running command or turn → quit immediately.
- **Ctrl+Q** with a running command or prompt turn → modal dialog "Do you want
  to terminate the application?" via `MessageBox.Query`. Yes → host exits.

The status-bar shortcuts are also wired, but the source of truth for Ctrl+C,
Ctrl+V and Ctrl+Q is a global `Application.KeyDown` handler (`TerminalGuiHost`
installs it; the keys live in `GlobalKeyBindings`) that fires regardless of which
view holds focus.

## Bottom area

From top to bottom, under the transcript:

- the spinner line while a turn runs: glyph and verb in the accent, the
  parenthesis dim;
- a dim `─` rule across the full width; while the recipient is a task other
  than `main`, it ends with `── <task> ─` in the accent (it replaces the former
  session chip);
- the prompt: `❯ ` in the accent and bold, the placeholder dim and italic;
- a second, bare rule;
- the status bar, indented by two columns, its parts joined by a dim ` · `: the
  permission mode (`⏵⏵ bypass permissions on (shift+tab to cycle)`, in Alert for
  `bypassPermissions`, `dontAsk` and `acceptEdits`, dim otherwise), the hints,
  then `↓ tasks` while a task other than `main` exists; from the list it reads
  `↑/↓ select · enter view` alone. The model witness stays right-aligned;
- a blank line, then the tasks list: `main` first, then creation order. Column 0
  holds the accent `❯` while the list has focus; column 2 holds `●` for the
  recipient and `◯` for the others:
  - green while running, red when failed or cancelled, dim otherwise;

  then the name in bold, the activity phrase cut with `…`, and right-aligned, dim,
  `<duration> · ↓ <tokens> tokens` (the task's total tokens, ticking with the
  1 Hz poll). Widths are terminal columns measured over graphemes (an emoji or a
  CJK character counts two); on a narrow pane the phrase gives way first, then
  the name, then the metrics, and a resize rebuilds the rows at the new width.
  The list grows up to 6 rows, then scrolls with the cursor.

The transcript records the list's changes from one poll to the next: a new task
prints `● <name>(<intent>)` then `  ⎿  Backgrounded agent (↓ tasks)`; a task
that ends prints `● Agent "<name>" finished · <duration>`,
`✗ Agent "<name>" failed · <duration>` or `⊘ Agent "<name>" cancelled · <duration>`.
The final state comes from `TuiIntegration.FinishedAgentRows` (the ended tasks
the host retains), so a task that starts and ends between two polls still gets
both lines, in order; without it, a task that leaves the list reads as finished.
A recipient that ends while the prompt has focus prints
`@<task> finished · recipient is main`. A poll whose row provider throws writes
nothing and leaves the rows, the list and the recipient as they were. `Esc Esc` prints `  ⎿  Interrupted · What should Orkeon do instead?`.
With `--glyphs ascii` the glyphs `❯ ● ◯ ✔ ✗ ⊘ ⏵ ⎿ ※ ─ ↑ ↓ …` become
`> * o v x - > | * - ^ v ...`, the hint bar's `↑/↓` and the cut phrase included.

## Model witness

The right-aligned witness of the hint bar names the selected model (the
`selected_model` key of the session state, `SessionStateKeys.SelectedModel`;
until a product writes it, the `Llm:Model` setting), truncated to 24 columns
with `…`. Its colour comes from the calls the host has observed
(`ILlmRetryObserver.OnCallSettled`), never from a probe, and only the displayed
model's calls count:

- **green** — usable: no call yet, or the last call ended with a final 2xx;
- **amber** — a retry backoff of that model is waiting (`Reconnecting to …`);
- **red** — a final non-2xx, a timeout, or no model configured (`no model`).

A change of the selected model resets the colour to green. A call ended by the
caller (`Esc Esc`) leaves the colour unchanged.

## Diagnosing display / keystroke issues

If the TUI starts but doesn't render or a shortcut doesn't fire, set
`TUI_DIAG=1` to print a structured diagnostic trace on stderr:

```bash
TUI_DIAG=1 dotnet run --project src/apps/Orkeon.ConsoleApp
```

You'll see each lifecycle step (`Main entered`, `Host.Build()`,
`AddOrkeonCliTerminalGui`, `Application.Init`, `Application.Run`, status bar
attach, `Ctrl+C captured`, …). Use `TUI_DRIVER=ansi|dotnet|windows` to
override the driver pick.

## Known limitations

- **Tests crash inside xUnit (TUI-12 → TUI-18).** Terminal.Gui v2.0.1's
  `ModuleInitializer` walks every loaded assembly's custom attributes and
  trips on a `MemberNotNullWhenAttribute` reference in
  `Microsoft.TestPlatform.CoreUtilities`. Layout/adapter/host tests are
  marked `[Fact(Skip = "…TUI-12")]` until upstream patches land. In-process
  helper tests (TtyDetector, options, log Format helpers, MoreVerbose /
  LessVerbose, DI registration shape, UiFlagParser) do pass — currently 55
  green, 35 skipped. End-to-end behaviour validated via standalone probes
  during development.
- **Static `Application` API is `[Obsolete]` in v2.1.0.** Terminal.Gui is
  migrating to an instance-based `IApplication` model. We pin to v2.0.1
  (TUI-18 tracks 2.1.x re-eval). The static API still works; we suppress
  `CS0618` in `Orkeon.Cli.TerminalGui.csproj` and have isolated the
  dependency in `Hosting/TerminalGuiHost.cs` and `Layout/UiDispatchers.cs`
  so the migration is a localized refactor.
- **DI lifecycle**: `TerminalGuiHost`'s constructor takes only
  `TerminalGuiOptions`. The status bar (which depends on
  `TerminalGuiLoggerProvider` for the F2 log-level cycle) is built and
  attached by the LoggerProvider's DI factory after both Host and Provider
  exist. This breaks an otherwise cyclic dependency that made
  `Microsoft.Extensions.Hosting` exit silently with code 0 inside `.Build()`
  (TUI-14). Always register Host without explicit `TerminalGuiLoggerProvider`
  ctor injection.
- **A single `Esc Esc` may not stop a `verify` immediately (TUI-20)** —
  `ICrewOrchestrationService.KickoffAsync` and intermediate Agent layers
  don't propagate the `CancellationToken` to every internal `await`.
  HttpLlmProviderBase does, so once the orchestrator decides to exit it
  cancels the live HTTP call quickly. Workaround: `Ctrl+Q`, confirmed, quits.
  Fix is a framework-level audit (out of scope for the TUI).
- **`RunnerExecution.RunInteractiveLoopAsync` writes to `Console.*`
  directly.** It accepts the `--ui` flag transparently but TUI mode would be
  ineffective until that helper is refactored to use `IConsoleAdapter`.
  ConsoleApp's `MainMenuRunner` and `QaRunner` (which inherit
  `InteractiveRunnerBase`) are wired correctly.

The three UI behaviors (auto-TUI on a terminal, forced plain via
`--ui plain`, pipe fallback to plain) are covered by the automated suite:
`dotnet test tests/cli/Orkeon.Cli.TerminalGui.Tests/Orkeon.Cli.TerminalGui.Tests.csproj`.
