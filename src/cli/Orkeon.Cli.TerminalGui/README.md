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
  (`ClaimVerifierRunner`, `MainMenuRunner`, `QaRunner`) get the new UI for free.
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
| `Ctrl+C` | Cancel current command (1× soft request, 2× within 2s force-quit) |
| `Ctrl+Q` | Quit (with confirmation dialog if a command is running) |

The active log level is shown in the Logs pane title in real time
(`Logs — Level: Information`).

## Copy / paste — including Docker and SSH

Clipboard traffic goes through `Layout.TuiClipboard`, a hybrid built for hosts
where Terminal.Gui's OS clipboard integration is unavailable (a Docker container
or an SSH session has no xclip/X11 and no `powershell.exe`):

- **Copy** (`Ctrl+C` on a selection, `Ctrl+A` select-all, or mouse drag-select —
  auto-copies on release) fans out to the OS clipboard when supported, to the
  **hosting terminal via OSC 52** (Windows Terminal, iTerm2, kitty… set the
  *host* clipboard even across docker/ssh), and to an in-process cache.
- **Paste** (`Ctrl+V` or right click) reads the OS clipboard when available,
  else the in-process cache (so copy → paste round-trips inside the TUI).
- **Paste from the host OS** when the container cannot read the host clipboard:
  use the **terminal's own paste** — `Ctrl+Shift+V` (or the terminal's menu) —
  which arrives as bracketed paste and is inserted natively by Terminal.Gui.
- **Select with the terminal instead of the TUI**: hold **Shift** while
  dragging — the terminal bypasses mouse tracking and its native
  `Ctrl+Shift+C` copies without involving the app at all.

> Windows Terminal note: plain `Ctrl+V` is a Windows Terminal *paste* binding —
> it never reaches the app; the pasted text arrives as bracketed-paste input,
> which lands in the focused field. Both routes end up doing the right thing.

Color scheme: `Orkeon.Cli.TerminalGui.Layout.SchemeFactory` builds explicit
white-on-black schemes. Without this Terminal.Gui paints gray-on-gray
(unreadable until you select with the mouse).

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
//    so the TUI can drive Ctrl+C cancellation and Ctrl+Q confirmation.
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
`RequestCommandCancellation()` so the TUI can soft-cancel via Ctrl+C.

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

The TUI exposes a two-stage cancellation pattern (same convention as
`kubectl`, `npm`, `curl`, etc.):

- **1×Ctrl+C** during a command → request cancellation. Visible feedback
  appears in the REPL pane: `⏹  Cancellation requested...`.
  Effective only if every awaited layer in the command's call chain honours
  the `CancellationToken` (Crew → Agent → ToolCall → LLM HTTP). The HTTP
  base correctly propagates; some intermediate orchestration layers may not
  yet — tracked in TUI-20.
- **2×Ctrl+C within 2s** → force-quit the entire TUI immediately. The
  in-flight task is abandoned (orphan; finishes in background); the host
  exits within 2s grace period.
- **Ctrl+Q** without a running command → quit immediately.
- **Ctrl+Q** with a running command → modal dialog "Do you want to terminate
  the application?" via `MessageBox.Query`. Yes → host exits.

The status-bar Shortcut for Ctrl+C is also wired, but the source of truth is
a global `Application.KeyDown` handler in `TerminalGuiHost` that fires
regardless of which view holds focus.

## Diagnosing display / keystroke issues

If the TUI starts but doesn't render or a shortcut doesn't fire, set
`TUI_DIAG=1` to print a structured diagnostic trace on stderr:

```bash
TUI_DIAG=1 SKIP_BUILD=1 bash run-interactive.sh -s deepseek.local
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
- **Single Ctrl+C may not stop a `verify` immediately (TUI-20)** —
  `ICrewOrchestrationService.KickoffAsync` and intermediate Agent layers
  don't propagate the `CancellationToken` to every internal `await`.
  HttpLlmProviderBase does, so once the orchestrator decides to exit it
  cancels the live HTTP call quickly. Workaround: 2×Ctrl+C force-quits.
  Fix is a framework-level audit (out of scope for the TUI).
- **`RunnerExecution.RunInteractiveLoopAsync` writes to `Console.*`
  directly.** It accepts the `--ui` flag transparently but TUI mode would be
  ineffective until that helper is refactored to use `IConsoleAdapter`.
  ConsoleApp's `MainMenuRunner` and `QaRunner` (which inherit
  `InteractiveRunnerBase`) are wired correctly.

Without the submodule, the same three behaviors (auto-TUI on a terminal, forced plain via
`--ui plain`, pipe fallback to plain) are covered by the automated suite:
`dotnet test tests/cli/Orkeon.Cli.TerminalGui.Tests/Orkeon.Cli.TerminalGui.Tests.csproj`.
