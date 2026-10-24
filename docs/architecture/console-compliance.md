> 🇫🇷 [Version française](../fr/architecture/console-compliance.md)

# Console compliance

Framework code never touches `System.Console`. The process console is global state: a write to it
cannot be captured by a test running beside another one, a `Console.SetOut` swap races with every
other test of the process, and on `orkeon mcp serve` a stray line on stdout corrupts the JSON-RPC
channel. Output goes through an injected `ILogger`; reads and interactive writes go through an
injected `TextWriter`/`TextReader` or a console abstraction that the executable's entry point
builds and hands on.

## The analyzer: `ORKCON001`

`ConsoleUsageAnalyzer` ships in `src/analyzers/Orkeon.Compliance.Vfs/`, beside the [VFS rules](./vfs-compliance.md),
and runs on every project under `src/`. It reports any reference to a member of `System.Console`
(call, property, event, method group), judged on the **resolved symbol**:
`using static System.Console; WriteLine(...)` is reported, and a property named `Console` whose type
is `IConsoleAdapter` is not. Text inside a string literal (the program `ProcessIsolationSandbox`
writes for a child process) is not a reference and is not reported.

`.editorconfig` sets it to `error` under `[src/**.cs]`. Tests (`/tests/`) and examples
(`/examples/`) are exempt.

## Opt-out: `[SuppressConsoleCompliance]`

The attribute is public in `Orkeon.Domain` (namespace `Orkeon.Compliance.Vfs`); a project that does
not reference `Orkeon.Domain` declares an internal copy, matched by name. Its reason starts with one
of two categories, checked by `SuppressionReasonCategoryTests`:

| Category | Meaning | Where |
|---|---|---|
| `CONSOLE-ADAPTER` | An implementation of a console abstraction over the process console | `SystemConsoleAdapter`, `LineEditingConsoleAdapter`, `SystemLineEditorConsole` (`Orkeon.Cli.Abstractions`), `SystemRunnerConsole` (`Orkeon.Hosting`), `SystemTerminalProbe` (`Orkeon.Cli.TerminalGui`) |
| `CONSOLE-SINK` | The one place of an executable that hands the real streams on | `orkeon` (`Program.UseUtf8Console`), `orkeon-repl` (`Program.Main`), `orkeon-host` (`HostConsole`, `StartupFailureReporting.cs`), `orkeon-studio-config` and `orkeon-studio-run` (`Program.Main`), Orkeon Studio (`App()`) |

Nothing else carries it: a new executable gets one sink, a new console abstraction one adapter.

## Where the streams come from

| Layer | Abstraction | System implementation |
|---|---|---|
| REPL commands (`Orkeon.Cli.Abstractions`) | `IConsoleAdapter` | `SystemConsoleAdapter`, `LineEditingConsoleAdapter` |
| Runners and the `orkeon` CLI (`Orkeon.Hosting`) | `IRunnerConsole`: `Output`, `ErrorOutput`, `Input`, `IsInputRedirected`, `OnInterrupt(handler)` | `SystemRunnerConsole.Instance`, carried by `RunnerOptionsBase.RunnerConsole` |
| Terminal UI (`Orkeon.Cli.TerminalGui`) | `ITerminalProbe`: redirection probes, `OutputIsUtf8`, `TreatControlCAsInput`, `Output`, `Diagnostics` | `SystemTerminalProbe.Instance`, carried by `TerminalGuiOptions.Terminal` |
| Library adapters | `TextReader`/`TextWriter` parameters | `ConsoleHumanInputProvider(logger, input, output)`, `McpServer.RunStdioAsync(reader, writer, ct)` |

`IRunnerConsole.OnInterrupt` replaces the `Console.CancelKeyPress` handlers each verb used to
register; the registration is disposed when the verb ends.

## Operator warnings: the `Orkeon.Operator` category

The runner host's startup warnings (tokens unavailable, LLM not configured, unresolved API key
reference, MCP server unavailable) used to be written both to the logger and to `Console.Error`,
because a host may keep its logger below `Error`. They now go only through the logger category
`Orkeon.Operator`, served by its own provider whose minimum level is forced to `Warning` whatever
`-v` says, and which writes one line to `IRunnerConsole.ErrorOutput`. `RunnerHost` adds it for the
`orkeon` CLI and `orkeon-host`, after the caller's logging (so after a `ClearProviders`);
`orkeon-repl` adds it in `ReplStartup`.
