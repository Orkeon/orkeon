> 🇫🇷 [Version française](../fr/architecture/cli-ts-commands.md)

# CLI TypeScript Commands

> User-facing reference for the `Orkeon.Cli.Commands.Scripting` subsystem. For the
> architecture spec and design rationale, see
> the maintainers' design archive (feature `cli-ts-commands`, SPEC).

## What it is

A way to add **interactive REPL commands** to an Orkeon CLI runner by dropping
`*.cmd.ts` files into a folder — no .NET recompile, no host restart beyond
the runner itself. Each script declares one or more commands via the global
`defineCommand({...})` helper; the loader picks them up at startup, validates
them, and exposes them alongside the built-in `help`/`exit`/`clear`.

Commands can also **dispatch work to live agents** — synchronously (block and
return the reply) or asynchronously (fire a ticket, react when the agent
responds). See [Dispatching commands to agents](#dispatching-commands-to-agents).

## Quick start

```bash
mkdir -p ./commands
cat > ./commands/hello.cmd.ts <<'EOF'
defineCommand({
  name: "hello",
  description: "Say hello to the world (or to someone in particular).",
  args: {
    who: { type: "string", default: "world" },
  },
  async handler(args, ctx) {
    return ctx.continue(`Hello, ${args.who}!`);
  },
});
EOF

dotnet run --project src/apps/Orkeon.ConsoleApp -- \
    --runner=scripted-commands \
    --commands-dir ./commands
```

At the prompt:

```
scripted> /help            # 'hello' shows up
scripted> /hello           # → Hello, world!
scripted> /hello --who=Ada # → Hello, Ada!
scripted> /help-cmd hello  # signature with typed args
scripted> /exit
```

In the scripted REPL only a line that starts with `/` is a command. `@path` references a file
or folder (Tab completes command names and paths), and anything else is free text — handed to
the fallback command (below), or refused when there is none. The message a command returns
through `ctx.continue(...)` is what the REPL prints; `ctx.log` goes to the logs pane, which the
TUI hides at startup.

## CLI flags

| Flag                              | Effect                                                                              |
|-----------------------------------|-------------------------------------------------------------------------------------|
| `--runner=scripted-commands`      | Boot directly into the scripted-commands REPL instead of the main menu (whose `scripted` entry opens the same REPL). |
| `--commands-dir <path>`           | Add a directory to scan (repeatable; mounted read-only as `/cli-commands`, then `/cli-commands-1`, … in the VFS). |
| `--no-script-commands`            | Disable discovery entirely: no `*.cmd.ts` is loaded, and only `help-cmd` and the default commands remain. |
| `--strict-commands`               | Equivalent to `FailFastOnInvalidScript=true + ContinueOnConflict=false` (CI).       |
| `--settings <path>`               | Explicit appsettings path (repeatable — later files override earlier ones).         |
| `--mount <phys:virt:rights>`      | Add a VFS mount (repeatable) — e.g. to make a commands directory reachable. Placed and guarded as for `orkeon run` (GAP-49): on a root the settings declare it replaces that root's entries for the session, on a new root it is appended; a mount the REPL cannot honour — `/sandbox`, `/credentials`, a root it mounts itself (`/cli-commands…`, `/crews…` while a `--commands-dir` or `--crews-dir` puts them there), a root twice on the command line, a missing folder — is refused before the console opens, exit `1`, wherever it was declared. Without `--commands-dir`, `--mount <folder>:/cli-commands:ro` still reaches a commands directory. |
| `--allow-external-mounts`         | A `--mount` outside the working directory is readable (or `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`), as for `orkeon run`; without it, the REPL refuses to start (exit code `1`) before the console opens. Every settings entry in force (one chosen by id included) and the `--commands-dir` and `--crews-dir` folders — entries of the REPL, like the script of `orkeon run` — are readable wherever they are, without it. |
| `--crews-dir <path>`              | Add a crew-resolution directory (repeatable; mounted read-only as `/crews`, `/crews-1`, …) — what makes `script-host` / `runCrewAsync("name")` resolvable. |
| `--llm-log` / `--llm-log-path <dir>` | The LLM exchange log of `orkeon run`: JSONL under `./llm-logs`, or `<dir>`, which implies `--llm-log` (the last `--llm-log-path` wins). A folder outside the working directory needs `--allow-external-mounts`; without it, the REPL refuses to start (exit code `1`). |

Every value flag accepts `--flag value` and `--flag=value`. `--commands-dir`, `--settings`,
`--mount` and `--crews-dir` add up when repeated; `--allow-external-mounts` also takes `=true` or
`=false`. `orkeon-repl` also takes `--ui` and `--repl-wrap`
([CLI reference](../reference/cli.md#orkeon-repl--the-separate-interactive-console)).

## appsettings.json

Same options under `Orkeon:Cli:ScriptCommands`:

```json
{
  "Orkeon": {
    "Cli": {
      "ScriptCommands": {
        "Enabled": true,
        "Directories": ["/cli-commands"],
        "FailFastOnInvalidScript": false,
        "EsbuildTranspile": true,
        "MaxScripts": 200,
        "ContinueOnConflict": true,
        "FallbackCommandName": "assistant",
        "Limits": {
          "MemoryLimitBytes": 67108864,
          "RecursionLimit": 100,
          "ExecutionTimeout": "00:05:00"
        }
      }
    }
  }
}
```

Directories are **virtual paths** resolved through `IFileSystemService` — set up
a `Orkeon:FileSystem:Mounts` entry if the directory isn't already mounted.
`FallbackCommandName` (default `assistant`) routes any REPL line **not** prefixed
with `/` to that scripted command — the switch that turns the REPL into a
conversational agent. No shipped example defines an `assistant` command: without one,
free text is answered as an unknown command.

## `defineCommand` reference

```typescript
defineCommand({
  name: "deploy",                 // ^[a-z][a-z0-9-]*$, never help / ? / h
  aliases: ["d", "ship"],         // optional, must not duplicate `name`
  group: "release",               // optional, ^[a-z][a-z0-9-]*$; default: the folder of the file
  description: "Deploy a crew.",  // single line, ≤ 200 chars

  args: {
    target: { type: "string", required: true, choices: ["dev", "prod"] },
    crew:   { type: "string", required: true },
    dryRun: { type: "boolean", default: false },
  },

  async handler(args, ctx) {
    ctx.log.info(`Deploying ${args.crew} to ${args.target}`);
    return ctx.continue();
  },
});
```

`group` (also accepted by `defineAsyncCommand`) is the `[<group>]` block `/help` lists the command
under. When absent it is the name of the folder holding the `*.cmd.ts`
(`/cli-commands/session/clear.cmd.ts` is in `session`), taken below the directory of
`Orkeon:Cli:ScriptCommands:Directories` that found the file: a file directly in that directory has no
group, however deep the directory sits in its mount (`/workspace/commands/a.cmd.ts`, found under
`/workspace/commands`, has none), and a folder name that is not a valid group name gives none. `/help` prints one block per
group in registration order, `(no group)` last; the runner's own commands (`ps`, `inspect`,
`result`, `cancel`, `help-cmd`) sit in `runner`, and `help-cmd <name>` names the group on its
first line. An invalid `group` is refused like an invalid name.

`help`, `?` and `h` are reserved. A script may deliberately take `exit`, `quit`, `q`, `clear`
or `cls`: scripted commands are resolved before the defaults, and the loader logs the
shadowing.

When `args` is omitted, the handler receives `{ raw: string[] }` instead — the
signature `(args, ctx) => ...` stays stable across typed/untyped commands.

### Supported arg types

| `type`      | Notes                                                                        |
|-------------|------------------------------------------------------------------------------|
| `"string"`  | Optional `choices: readonly string[]`, optional `default`.                    |
| `"number"`  | Optional `min`, `max`, `default`.                                            |
| `"boolean"` | Bare `--flag` ⇒ `true`. Optional `default`.                                  |
| `"string[]"`| Greedy: positional consumes the tail, flag form consumes until next `--key`. |

Validation errors surface as `Error: ...` printed by the runner; the handler is
**not** invoked.

## `ctx` reference (the runtime context)

```typescript
interface CommandRuntimeContext {
  readonly command: { readonly name: string; readonly rawInput: string };

  readonly log: {
    debug(msg: string, data?: object): void;
    info(msg: string, data?: object): void;
    warn(msg: string, data?: object): void;
    error(msg: string, data?: object): void;
  };

  write(text: string): void;
  writeLine(text: string): void;
  clear(): void;

  prompt(spec: PromptSpec): Promise<string | boolean>;
  progress(spec: { total?: number; label: string }): ProgressHandle;
  table<T extends object>(rows: readonly T[], columns?: readonly (keyof T)[]): void;

  readonly signal: CommandSignal;       // CancellationToken — see "Cancellation"
  readonly services: ServiceLocator;    // whitelisted

  continue(message?: string): CommandActionResult;
  exit(farewell?: string): CommandActionResult;
}
```

`ctx.command.rawInput` is the line as typed (`/deploy prod --force`); in an async command's
`completed`, it is the line that launched that ticket: only the launching command runs the
completions of its own instances, even when it was typed again since. `ctx.progress`
returns a handle with `advance(label?)`, `set(value, label?)` and `done(label?)`. A global
`console.log/info/debug/warn/error` forwards to `ctx.log`.

The full `.d.ts` ships inside the `orkeon` tool: `orkeon typings` writes it to
`.orkeon/orkeon-cli.d.ts` (next to `orkeon.d.ts`) in the current directory — the path the
examples' `/// <reference path=…>` line expects; `--out <dir>` writes it elsewhere
([CLI reference](../reference/cli.md#orkeon-typings)). The repository's typings gate,
`scripts/check-scripting-typings.sh`, typechecks every shipped `*.cmd.ts` against it. A command
file is also a full crew-DSL engine: `agentBuilder`, `crewBuilder`, `llm`, `tools.*` and `rag`
are all there, and top-level `await` works.

### Prompts

```typescript
const target = await ctx.prompt({ type: "select", message: "Target?", choices: ["dev","prod"] });
const ok     = await ctx.prompt({ type: "confirm", message: "Proceed?", default: false });
const name   = await ctx.prompt({ type: "text", message: "Name?" });
const pwd    = await ctx.prompt({ type: "password", message: "Password:" });
```

### Cancellation

`ctx.signal` is the raw .NET `CancellationToken` exposed by Jint. Properties
are **PascalCase** because of reflection:

```typescript
while (!ctx.signal.IsCancellationRequested) {
  // long-running work
}
ctx.signal.ThrowIfCancellationRequested();
```

The TypeScript alias `CommandSignal` declared in `.d.ts` is a documentation
convenience; the runtime members are PascalCase.

### Services

```typescript
const fs = ctx.services.get("fs");       // unknown: the typings declare no service types
const tools = ctx.services.get("tools");
if (ctx.services.has("llm")) { /* … */ }
```

The host whitelist drives what's reachable. Default keys: `fs`, `tools` and `runtime` (the
version of the runtime, below), plus the
optional `llm`, `logger`, `commands` (the dispatch façade — see below), `script-host`
(crew launching from a command, and `script-host.listCrews()`, the discovered crew names — see
[Driving crews from the REPL](./coding-agent-ts.md)) and, in `orkeon-repl`, `permissions` (the
session's tool-call rules, below), `approvals` (the tool approvals waiting, below) and `session`
(save the persisted session, load a saved one, below), and `usage` (the session's LLM usage by model
and by command, below) where the host keeps a cost manager or command instances; the typings return
`CommandsFacade`, `ScriptHostFacade`, `PermissionRulesFacade`, `PendingApprovals`, `SessionFacade`,
`RuntimeInfo` and `SessionUsage` for those seven without a type argument. `configuration` is deliberately
**not** exposed — the configuration root carries API keys — so the typings do not declare it,
and asking for a key outside the whitelist throws. Hosts add their own by passing an `Action<ScriptServiceWhitelist>` to
`AddScriptCommands` (`Add(name, factory)`, `AddOptional(name, factory)`).

## Dispatching commands to agents

A command can address a **live agent by name** and let *its* response decide when
the command is finished. This is the `commands` façade, reached via
`ctx.services.get("commands")`. Under the hood it rides an `IAgentChannel` of its own —
an in-memory channel `AddScriptCommands` creates: the façade resolves the agent name →
`AgentId`, correlates the request/response host-side, and tracks every dispatch in a
queryable registry.

> **Key rule:** the command finishes when the **agent responds**, not when the
> handler returns. Routing is point-to-point — one command targets exactly one
> agent, which is its sole finisher (no fan-out, no join).

### The agent side — `onCommand`

An agent declares that it answers dispatched commands with `onCommand` (in the
crew DSL — see [scripting.md](./scripting.md)). The value it returns is the
response that terminates the command:

```typescript
const echo = agentBuilder()
  .name("echo").role("Echo").goal("Echo a payload back")
  .onCommand("run", (env) => env.payload.toUpperCase())     // answers intent "run"
  .onCommand((env) => ({ success: true, payload: "ack" }))  // catch-all (any intent)
  .build();
```

The handler receives an **envelope** `{ intent, payload, from, correlationId }`
and returns either a payload string or `{ success?, payload?, error? }`. The
agent must be **activated** on the dispatch bus (`AgentCommandRegistrar`) so a
`.cmd.ts` command can reach it by name. Handler JS runs under the agent's engine
lock, so it is safe even when a background async dispatch invokes it.

### Synchronous dispatch — `request`

A normal `defineCommand` that awaits the agent. `request` blocks until the agent
responds and returns the `CommandResponse` (so `await` on it resolves to the
value — a sync command is meant to block). Its `streamed` is true when any turn of
the request was written to the console in the foreground
(`Orkeon:Cli:ConsoleStreaming:Enabled`); background and labelled output report false.
Print `payload` only when it is false, or the answer shows twice (#228):

```typescript
defineCommand({
  name: "ask",
  description: "Ask the 'echo' agent and wait.",
  args: { text: { type: "string", required: true } },
  handler(args, ctx) {
    const res = ctx.services.get("commands").request("echo", "run", args.text);
    if (!res.success) return ctx.continue("agent error: " + res.error);
    // The console already showed a streamed answer: print it only when it did not.
    return res.streamed ? ctx.continue() : ctx.continue("→ " + res.payload);
  },
});
```

`request(agent, intent, payload)` returns `{ agent, intent, success, payload, streamed, error? }`.

### Asynchronous dispatch — `defineAsyncCommand`

Use `defineAsyncCommand` when the work should detach: `dispatch` fires and returns
the prompt immediately; the optional `completed` is replayed later when the agent
responds.

```typescript
defineAsyncCommand({
  name: "ask-bg",
  description: "Ask the 'echo' agent in the background.",
  maxConcurrent: 3,                                  // admission quota (see below)
  args: { text: { type: "string", required: true } },
  dispatch(args, ctx) {                              // does NOT block
    const ticket = ctx.services.get("commands").post("echo", "run", args.text);
    ctx.log.info("launched (ticket " + ticket + ")");
    return { ticket };
  },
  completed(result, ctx) {                           // run when the agent answers
    ctx.writeLine("✓ " + result.agent + ": " + result.payload);
  },
});
```

- `post(agent, intent, payload)` returns a **ticket** (string) right away and runs
  the request on a background task.
- `completed(result, ctx)` is **not** called from the background thread (Jint is
  single-threaded). As soon as the work settles, the host prints a terse line
  (`✓ [t1] ask-bg → echo: done`) and runs `completed` on the script's engine, under
  its lock — no need to type another command. The commands of one file share an
  engine, but each runs only the completions of its own instances (the runs launched
  under its name): a sibling command typed meanwhile never runs another command's
  `completed`.
- For a value you want to read on demand, poll with `result --ticket=…` (below).
- **Chaining from `completed`.** `completed` may call `runCrewAsync(name, input)`: it
  returns a ticket at once, and the second crew's completion drains to its own
  command (the same command name), whose `completed` runs in turn. The chain is
  bounded by `MaxChainDepth` (default 8, `ScriptCommandsConfiguration.MaxChainDepth`);
  the depth counts how many `completed` hops deep an instance was launched (`inspect`
  shows it). A `commands.post` counts the same way (the parent's depth plus one; it takes
  no options). A deeper `runCrewAsync` or `post` is refused with
  `ChainDepthExceededException`, as a JS throw whose message names the depth, the limit
  and the command: `chain depth 8 reached (MaxChainDepth 8, /dev-batches)`.
- **A driver that loops: `{ chain: "continue" }`.** A command that relaunches its own
  work after each completed cycle (a plan run batch after batch) passes
  `runCrewAsync(name, input, { chain: "continue" })`: the new instance takes the depth of
  the instance whose `completed` is running instead of that depth plus one, so the loop
  command → crew → the same command's `completed` → crew runs as long as it needs. The
  parent instance is always one of the same command: a command runs only the `completed`
  of its own instances, and what that `completed` launches is keyed to it. Outside a
  `completed` (a `dispatch`, a handler) the option changes nothing: the launch takes the
  depth it takes without it (a first hop is depth 1), and a `post` always climbs. Any
  other `chain` value is a script error.
- **A crew that must not run twice: `{ exclusive: true }`.** A crew that must not run
  twice at a time (a review, the writer of a shared store) is launched with
  `runCrewAsync(name, input, { exclusive: true })`: while another exclusive launch of the
  same crew is not terminal, the call throws before any LLM call, naming the running
  instance — `crew 'code-review' already running (ticket t2)`. The check and the launch
  are one step, so two lines typed in the same tick, or a `completed` chain, cannot both
  pass. The crew is free again as soon as its instance completes, fails or is cancelled
  (a `completed` that relaunches it is admitted; a cancelled run stops at its next
  cooperative checkpoint, so its last step may overlap the next launch). A launch without the option is neither
  refused nor counted; a non-boolean `exclusive` is a script error. Unlike
  `maxConcurrent` below, it bounds the crew, not the command: an alias of the command
  that launches nothing answers during the run. Both options combine
  (`{ chain: "continue", exclusive: true }`).

### Admission quota — `maxConcurrent`

Declared on `defineAsyncCommand`, it bounds the number of **in-flight instances of
that command**. Omitted ⇒ unbounded (∞). Acquisition is non-blocking: when the
quota is full a new invocation is **rejected immediately** (the `dispatch` never
runs) with a `Rejected: quota of N instance(s) of '<cmd>' reached.` message. The
slot is released when the agent responds. `maxConcurrent` only has a real effect
for async commands — a sync command already holds the engine and is serialised.

### Introspecting in-flight commands

The dispatch registry is exposed two ways. From a script:

```typescript
const facade = ctx.services.get("commands");
facade.list({ state: "running" });   // CommandInstanceView[]; also { name }, { agent }, or "running"
facade.get("t3");                    // one view, or undefined
facade.cancel("t3");                 // request cancellation; returns boolean
```

And as **built-in commands** at the REPL (fast, stay responsive while async work
runs in the background):

| Command                    | Effect                                                        |
|----------------------------|---------------------------------------------------------------|
| `ps [--state=…]`           | List instances. State: `running` (default), `done`, `failed`, `cancelled`, `rejected`, `all`. |
| `inspect --ticket=<t>`     | Full detail of one instance (state, agent, chain depth, elapsed, result, progress). A bare ticket works too: `/inspect t3`. |
| `result --ticket=<t>`      | Print the result payload (poll). Reports "still running" if not done. |
| `cancel --ticket=<t>`      | Request cancellation of an in-flight ticket.                  |

A `CommandInstanceView` carries: `ticket`, `name`, `kind` (`sync`/`async`),
`targetAgent`, `intent`, `correlationId`, `state`, `startedAt`, `completedAt?`,
`elapsedMs`, `tokens`, `result?`, `error?`, `progress?`. Tickets read `t1`, `t2`, …; the
200 most recent terminal entries are kept so `result`/`inspect` can read a recent ticket,
then evicted. The four built-ins are registered only when at least one `*.cmd.ts` was
found.

### Wiring (host side)

`AddScriptCommands` registers the dispatch substrate as a singleton (its own
in-memory channel — the CLI dispatch bus — plus the name directory and instance
registry), which is what makes the optional `commands` key resolve. Agents are connected with
`AgentCommandRegistrar.Register(agent, engine, engineLock, service.Channel, service.Directory, logger?)`,
which registers the channel handler and the name→id mapping. The host does that itself:
`orkeon-repl` loads `*.cmd.ts` files only and activates no agent, so until a host registers
one, `request` and `post` throw `commands: unknown agent 'echo'. Registered: (none).`

### End-to-end example

[`examples/cli-ts-commands/`](https://github.com/Orkeon/orkeon/blob/main/examples/cli-ts-commands/README.md)
ships `dispatch.cmd.ts` (the `ask` / `ask-bg` commands) and `echo-agent.ork.ts` (the
`onCommand` agent). With the agent registered by the host:

```
scripted> /ask hello            # → HELLO        (sync, blocks)
scripted> /ask-bg hello         # returns now; then  ✓ [t1] ask-bg → echo: done
scripted> /ps --state=all       # t1 ask-bg echo done …
scripted> /result --ticket=t1   # [t1] HELLO
```

## Building a REPL host in C#

`orkeon-repl` is one host built from four projects; a host of your own composes the same
pieces.

| Project | What it gives a host |
|---|---|
| `Orkeon.Cli.Abstractions` | The contracts: `IInteractiveCommand` (`Name`, `Aliases`, `Description`, `ExecuteAsync(CommandContext, ct)` returning `CommandResult.Continue(...)` / `Exit(...)`), `IInteractiveCommandRegistry` (`Commands`, optional `Fallback`), `IConsoleAdapter` with `SystemConsoleAdapter` and the line-editing `LineEditingConsoleAdapter`, and `InteractiveRunnerBase` — the loop itself. |
| `Orkeon.Cli` | `AddOrkeonCli()`: the three default commands — `help` (`?`, `h`), `exit` (`quit`, `q`), `clear` (`cls`) — and the `DefaultCommandRegistry` holding them, as singletons. |
| `Orkeon.Cli.Commands.Scripting` | `AddScriptCommands(configuration?, configure?, configureWhitelist?)`: the `*.cmd.ts` loader and its `ScriptCommandRegistry`, the dispatch substrate (`commands`), `script-host`, and `ScriptHost`. `AddLlmConsoleStreaming(configuration)` renders streamed `act()` output in the console when `Orkeon:Cli:ConsoleStreaming:Enabled` is `true`. |
| `Orkeon.Cli.TerminalGui` | `AddOrkeonCliTerminalGui(options)`: the Terminal.Gui v2 split-pane console (below). |

A runner derives from `InteractiveRunnerBase`, whose constructor takes the default registry,
the host's own registry, the console adapter, a logger and the service provider; it supplies a
`Banner` and a `Prompt`, and may override `SlashCommandsOnly` (only `/`-prefixed lines are
commands), `ResolveFallback()` (the command that receives anything else), `OnStartAsync`,
`OnExitAsync` and `OnUnknownCommandAsync`. A line resolves against the host's registry first,
then the defaults — which is why a script may shadow `exit` or `clear`. The registry
`AddScriptCommands` registers loads nothing when it is resolved: call its `EnsureLoadedAsync`
from `OnStartAsync`, as `orkeon-repl`'s `ScriptedCommandsRunner` does.

```csharp
services.AddOrkeonCli();                                   // help / exit / clear + DefaultCommandRegistry
services.AddScriptCommands(configuration);                 // *.cmd.ts, commands, script-host
services.AddSingleton<IConsoleAdapter, SystemConsoleAdapter>();
services.AddSingleton<MyRunner>();                         // : InteractiveRunnerBase
services.AddOrkeonCliTerminalGui(new TerminalGuiOptions()); // last: replaces the adapter and the loggers

// then
await using var tui = provider.GetRequiredService<TerminalGuiHost>();
await tui.RunAsync(provider.GetRequiredService<MyRunner>(), CancellationToken.None);
```

### The split-pane console — `Orkeon.Cli.TerminalGui`

`AddOrkeonCliTerminalGui` swaps the console for a Terminal.Gui v2 screen: the REPL fills it, and
a logs drawer (`Ctrl+G`) takes the log lines that would otherwise interleave with the prompt.
It registers `TerminalGuiHost`, replaces `IConsoleAdapter` with the adapter over the REPL pane
(Tab completion is wired when the host registers an `IReplInputAssist`), and replaces **every**
`ILoggerProvider` with `TerminalGuiLoggerProvider` — a console logger would write into the
screen Terminal.Gui owns. The provider is also published process-wide
(`AmbientLoggerProvider`), so a host built inside a command logs into the same pane. Call it
after the other registrations, and call `ClearStdoutLoggersForTerminalGui()` in the logging
setup; a second call is ignored. `TerminalGuiHost.RunAsync(runner, ct)` runs the REPL on a
background task while Terminal.Gui owns the main thread, and returns when the REPL exits or the
user quits.

`TerminalGuiOptions` (an `init`-only record — pass a built instance): `InitialSplitRatio`
(0.5), `DefaultMinimumLogLevel` (`Information`), `LogsBufferCapacity` (5000 entries, an entry on several lines counting once),
`LogsPaneTitle` (`Logs`), `LogsVisibleAtStartup` (`false`), `ReplWordWrap` (`true`), `ReplTranscriptCapacity` (10000 lines),
`BannerEnabled` (`true`), `Banner`, `Glyphs` (`Auto`), `SpinnerVerbs`. The keys: `Ctrl+G` logs
drawer, `Ctrl+R` REPL pane, `Ctrl+L` / `Ctrl+K` clear the logs / the REPL, `Ctrl+F` find in the logs,
`Ctrl+↑` / `Ctrl+↓` resize, `F2` / `Shift+F2` more / less log detail, `F3` wrap, `↓` (from the
prompt's last row) or `F4` tasks list, `Ctrl+C` copy the selection, `Ctrl+V` and right-click paste into the input, `Ctrl+Q` quit
(it asks first while a turn runs). `orkeon-repl` picks this console with `--ui` ([CLI reference](../reference/cli.md#orkeon-repl--the-separate-interactive-console)).

**Keys on the prompt.** `Esc` clears the draft; a second `Esc` right after it (any other key in
between disarms the pair, and it arms on an empty draft too) interrupts the turn submitted from the
prompt, whether it runs as a synchronous command or as a dispatched ticket, a retry backoff
(`Reconnecting to …`) included. The hint bar shows `esc esc to interrupt` for as long as that turn
runs. Only the prompt's turn is interrupted: a crew started by anything else (a detached
`runCrewAsync`, compaction, indexing) keeps running, and stays reachable through `/ps` and
`/cancel <ticket>`; no key cancels it. `Ctrl+C` never cancels and never quits: with a selection it
copies it (the transcript, or the input, where a mouse drag selects too); without one it prints a
one-line hint (`Ctrl+C or Ctrl+Shift+C copies a selection — esc esc interrupts the turn.`). `Ctrl+V` pastes the
clipboard into the input whichever view has focus, in the runner and in the REPL modes, and stands
down while a modal dialog (find, quit confirmation) has focus. In the tasks list (`↓` or `F4`), `Esc`
returns to the prompt and cancels nothing, a turn running or not; it arms the pair like an `Esc` on
the prompt (`F4` and `Tab` do not), so `Esc Esc` from the list interrupts a running turn. `Ctrl+Q` quits at once when
nothing runs, and asks for confirmation while a turn runs, as it does for a synchronous command.

**Prompt, history and tasks list.** The prompt reads a key in one of three states, never twice:
the field, the history and the tasks list. In the field, `↑` on the first row (a visual row: a long
line wraps, and `↑`/`↓` first move through its rows) enters the history, keeping the typed text as
the draft and showing the newest entry, caret at the end; with no history it moves the caret as
usual. `↓` on the last row focuses the tasks list when it holds a task other than `main` (the hint
bar then reads `↓ tasks`), and does nothing otherwise. `Home` / `End` reach the start / end of the
line, `Ctrl+Home` / `Ctrl+End` those of the text. In the history, `↑` shows the previous entry
(nothing on the oldest), `↓` the next one, and from the newest gives the draft back and leaves the
history; any edit leaves it and keeps the text; `Enter` sends the shown entry and drops the draft.
Blank entries and a repeat of the newest one are not recorded. In the list (the hint bar reads
`↑/↓ select · enter view`), `↓` moves down without wrapping, `↑` moves up and, on the first row,
returns to the field; `Esc` returns to the field from any row, `F4` too, and the field gets its text
and caret back; `Home` / `End` reach the first / last row; `Enter` prints the selected task's detail
on the displayed screen and sends nothing; a printable key returns to the field with that character typed.
`Tab` returns to the field like `Esc`; `Ctrl+V` or a right-click returns to it and pastes at the
restored caret. The state follows the focus: a click on a row focuses the list on that row (a
double-click also prints its detail on the displayed screen), and focus reaching the field any other way leaves the list
and keeps the current text; the `❯` cursor is never drawn while the keys go to the prompt.
The selected row is the recipient of the next message, and stays so back in the field: the
placeholder reads `Message @<task>…` and the rule above the prompt ends with `── <task> ─`; selecting `main` gives
the default recipient back. The transcript keeps **one screen per thread**, `main` and each task:
the selection displays that thread's screen, so moving it with `↑` / `↓` switches the screen
shown, and `main` shows the main one (see "The bottom area"). The selection follows its task when the list re-sorts, and the field
takes the focus back, with `main` as recipient, when the list empties. Sending to a task needs the
host's `TuiIntegration.SendToTask` (`(ticket, text) → bool`); while no host provides it, `Enter`
with a task recipient prints that sending to a task is not supported yet, and the text stays in the
field, neither sent nor recorded. These keys stand down while a modal dialog has focus.

**The bottom area.** Under the transcript come the spinner line (glyph and verb in the accent, the
parenthesis dim), a dim `─` rule across the width, ending with `── <task> ─` in the accent while the
recipient is a task other than `main`, the prompt (`❯ ` accent and bold, the placeholder dim and
italic), a bare rule, the status bar, a blank line and the tasks list. The status bar is indented
by two columns and joins its parts with a dim ` · `: the permission mode (`⏵⏵ bypass permissions on
(shift+tab to cycle)`, yellow for `bypassPermissions`, `dontAsk` and `acceptEdits`, dim otherwise),
the hints, and `↓ tasks` while a task other than `main` exists; from the list it reads
`↑/↓ select · enter view` alone. The list shows `main` first, then the live tasks in creation order, then the ended tasks in
creation order, and `main` stays listed while any task, live or ended, is listed: the
accent `❯` in column 0 while the list has focus, `●` for the recipient and `◯` for the others
(green while running, red when failed or cancelled, dim otherwise; an ended row is dim and its
activity phrase reads the outcome, `✔ finished`, `✗ failed` or `⊘ cancelled`, its metrics the final
duration), the name in bold, the activity
phrase cut with `…`, and right-aligned and dim `<duration> · ↓ <tokens> tokens`, the task's total,
updated by the 1 Hz poll. Widths are terminal columns measured over graphemes (an emoji or a CJK
character counts two); on a narrow pane the phrase gives way first, then the name, then the
metrics, and a resize rebuilds the rows at the new width. It grows up to 6 rows, then scrolls with
the cursor. **One screen per thread.** Each of `main` and the tasks has a screen of its own, of
`ReplTranscriptCapacity` lines, and only the recipient's is displayed. A line a task writes (streamed
text, a tool-call question) lands on that task's screen, opened at its first line,
and carries no `[t1]` ticket prefix in the split-pane console (the plain console keeps it); a crew
label (`[<taskId>] `) stays, telling the agents of one crew apart inside the screen. The `main`
screen holds the prompt's turns and the lifecycle lines, never a task's output. A task's end line
(`  ✓ [t1] ask-bg → echo: done`) and what its command's `completed` writes go to `main`, the end line
keeping its ticket, which names the task there. Copy, the mouse
selection and word wrap (one state for every screen) act on the displayed screen; `Esc Esc` still
interrupts the prompt's turn whatever screen is displayed, its `Interrupted` line landing on `main`. An
attention line, such as a tool-call question, shows on the displayed screen whichever task asks and is
recorded once in the asking task's screen. In the split-pane console, ended tasks stay listed until
the host forgets them, with their screen: a task that ends keeps its row, its screen and, as the recipient, the recipient;
row and screen leave together when `TuiIntegration.FinishedAgentRows` no longer returns the task. A
poll whose ended feed fails keeps the ended rows of the last successful read: a fault is not the
host forgetting a task. Between two polls, a new task prints `● <name>(<intent>)` then
`⎿  Backgrounded agent (↓ tasks)` in the transcript, and a task that ended
`● Agent "<name>" finished · <duration>`, `✗ Agent "<name>" failed · <duration>` or
`⊘ Agent "<name>" cancelled · <duration>`: the final state comes from the host's
`TuiIntegration.FinishedAgentRows` (the ended tasks it retains), so a task that starts and ends
between two polls still gets both lines, in order; without it, a task that leaves the list reads as
finished. These lines go to the `main` screen. The recipient stays on a task that ends; the line
`@<task> finished · recipient is main` is printed, and the recipient goes back to `main`,
only when the host forgets the recipient task, while the prompt has focus; a poll whose row provider throws writes nothing and leaves the rows, the list and the recipient as they were.
`Esc Esc` prints `⎿  Interrupted · What should Orkeon do instead?`. Colours come from semantic roles (Normal, Dim, Accent, Ok, Alert, Error) on the
terminal's own background, in 24-bit when `COLORTERM` is `truecolor` or `24bit` and in 16 named
colours otherwise. `--glyphs ascii` turns
`❯ ● ◯ ✔ ✗ ⊘ ⏵ ⎿ ※ ─ ↑ ↓ …` into `> * o v x - > | * - ^ v ...`, the hint bar's `↑/↓` and the cut
phrase included.

**The model witness.** The right-aligned witness of the hint bar names the selected model (24
columns at most, longer names end with `…`) and is coloured by what the console has observed of that
model's calls, never by a probe: green while the model is usable, on a final 2xx included; amber
during a retry backoff of that model; red after a final non-2xx, a timeout, or when no model is
configured (the label then reads `no model`). Only the displayed model's calls colour it, and a
change of model resets it to green. A call ended by the caller (`Esc Esc`) is not a failure and
leaves the colour unchanged.

**Session state shared with the commands.** The console reads and writes two keys of the session
state (`ISessionBufferService`, which a command reaches with `session_store`'s `get_state` and
`set_state`): `default_permission_mode` (`SessionStateKeys.DefaultPermissionMode`,
`Orkeon.Constants.Cli`), the permission mode it shows and cycles on `Shift+Tab` (`default` →
`acceptEdits` → `plan` → `bypassPermissions`), and `selected_model`
(`SessionStateKeys.SelectedModel`), the model the witness names. A product's `/mode` command writes
the first key, so the console shows its choice: the keys are a contract and keep their names.
Orkeon Code's `/model` does not write `selected_model` yet; until it does, the witness falls back to
the `Llm:Model` setting.

**Tool-call approvals.** With the permission gate on, `orkeon-repl` registers an approval channel
(`AddConsoleToolApproval`): a call the mode asks about is put to the operator, whose next line
answers it. From a crew launched with `runCrewAsync`, the read loop hands that line over
(`IReplLineInterceptor`) before resolving any command; from a crew launched with `runCrew`, which
holds the loop, the waiting call reads the answer itself, and the time taken to answer does not
count in `RunCrewTimeout`. A command passes the session's mode to `act` as `permissionMode`; it
does not handle the question itself. See
[the CLI reference](../reference/cli.md#orkeon-repl--the-separate-interactive-console).

**Session rules.** The gate reads the session's allow and deny rules before the mode
(`IToolPermissionRules`, kept by the REPL). A command sets them through
`ctx.services.get("permissions")`: `allow(tool, argument?, pattern?)`, `deny(...)`, `clear()`,
`list()`. A rule covers every call of a tool, or the calls whose argument matches a pattern whole,
`*` standing for any run of characters. A deny rule refuses a call in every mode; an allow rule lets
it run without asking in every mode but `plan`, which stays read-only, so `dontAsk` runs what a rule
allows and refuses the rest. The rules last until the REPL exits. `enabled` (read-only) says
whether the host registered the gate (`Orkeon:Security:PermissionGate:Enabled = true`): false, the
rules and the modes check nothing and every tool call runs, so a command that shows a mode can say
so. A crew reads the same fact as `ctx.llm.permissionGate.enabled`.

```typescript
const permissions = ctx.services.get("permissions");
permissions.allow("shell_command", "command", "npm test*");
permissions.deny("shell_command", "command", "git push*");
```

**Pending approvals.** A command reads the tool approvals waiting for the operator through
`ctx.services.get("approvals")`, present when the host registers the approval channel
(`IToolApprovalWatch`). It is read-only: `list()` returns the questions waiting at that moment, the
one the next answer settles first, each a plain object `{ taskId, command, tool, input }`: the
ticket of the background task that asks (`t1`) and the name of its command, both null outside a
task, the tool's name, and the call's input as the status line shows it, its JSON arguments cut at
60 characters (`PendingToolApprovalInput`). The list is empty once the operator has answered, so a
`/tasks` command can tell a task blocked on the operator from one that runs.

```typescript
const approvals = ctx.services.has("approvals") ? ctx.services.get("approvals").list() : [];
for (const a of approvals) ctx.writeLine(`[${a.taskId}] ${a.command} waits for approval of ${a.tool} ${a.input}`);
```

**Saving and loading the session.** `orkeon-repl` persists its session per project
(`Orkeon:Cli:Session:Persist`, on unless the settings turn it off): it is saved after each turn and
at exit to `<SessionsDirectory>/<project>/last.json`, and reloaded at start. While it is persisted,
`ctx.services.get("session")` serves two calls. `save()` writes that file now, in the same shape,
and resolves to its virtual path; `save(path)` writes the session in the same shape to another
virtual file instead, which `load` accepts back, and resolves to that path (a blank path rejects).
`load(path)` reads a file the REPL saved, at a virtual path, and swaps the conversation buffer and
the state bag (the two things `/clear` resets), with the session's id and title, for its own; it
resolves to the number of messages loaded, and the next turn continues that conversation. The running
command instances (`commands.list`) are untouched, and the session goes on saving to its own file:
the loaded session overwrites `last.json` after the next turn, so archive the current one with
`save(path)` before a `load`, or it is lost. The file is judged whole before anything changes: a
missing file, an unreadable one, or one without the shape the REPL writes (`SessionId`, `Messages`
with a `Role` and a `Content` each, `State`) rejects the promise, and the session stays as it was.
Both go through the virtual file system: a path outside a mount, or without the read (or write)
right, is refused the same way. A `load` while another command's turn runs is not supported: the
last writer of the buffer wins.

```typescript
const session = ctx.services.get("session");
const archived = await session.save("/workspace/saved/before-review.json"); // keep the current session first
const count = await session.load("/workspace/saved/review.json");
ctx.writeLine(`archived ${archived}, loaded ${count} messages`);
```

**Runtime version.** `ctx.services.get("runtime")`, always present, answers the runtime that runs the
command as a plain object `{ version, commit, channel }`: the release with its build metadata removed
(`1.0.0-rc.6`), as `orkeon doctor` prints it; the commit sha that metadata carries (the part after
`+`), null when there is none; and the channel the runtime was installed through, read from the
installation's `INSTALL-CHANNEL` marker as [`orkeon doctor`](../reference/cli.md#orkeon-doctor) reads
it (`deb`, `tarball`, `source`, `dotnet-tool`…), `unknown` when nothing names one. `orkeon-repl`
registers its own (`ScriptRuntimeInfo`); a host that registers none answers its entry assembly's
version and the channel `unknown`.

```typescript
const runtime = ctx.services.get("runtime");
ctx.writeLine(`Orkeon ${runtime.commit ? `${runtime.version}+${runtime.commit}` : runtime.version} (${runtime.channel})`);
```

**Session usage.** A command reads the session's LLM usage through `ctx.services.get("usage")`,
present when the host keeps a cost manager (`ICostBudgetManager`) or command instances (the REPL
keeps both). It is read-only: `report()` returns the figures of that moment, `{ promptTokens,
completionTokens, cacheHitTokens, cacheWriteTokens, reasoningTokens, totalTokens, calls,
unpricedCalls, cost, byModel, byCommand }`. The cache counts are a part of the prompt, the reasoning a
part of the completion (reported by Z.AI and OpenRouter, 0 elsewhere). The totals count every call
the cost manager recorded (`CostReport.TotalUsage`); `byModel` has one row per model named in the
provider's answer, with its `calls`, so a call whose provider named no model is in the totals and in
no row: the totals may exceed the sum of `byModel`. `byCommand` has one row per command instance the
session keeps (a crew launched with `runCrewAsync` included, the newest first), from what
`InstanceAttributingUsageSink` credited it, with its `ticket`, `name`, `state` and `calls`. A `cost`
is the vendor's charge, else the price the registry knows for the model (`Orkeon:CostTracking`), and
null when neither is known for any of its calls: a `/usage` command can tell a free call from an
unpriced one. Every row and the totals carry `unpricedCalls`, the calls nobody priced, which `cost`
counts as 0: while it is not 0, the `cost` is a lower bound.

```typescript
const usage = ctx.services.get("usage").report();
for (const m of usage.byModel) ctx.writeLine(`${m.model}: ${m.promptTokens} in (${m.cacheHitTokens} cached), ${m.completionTokens} out, ${m.cost ?? "n/a"}`);
for (const c of usage.byCommand) ctx.writeLine(`[${c.ticket}] ${c.name}: ${c.promptTokens + c.completionTokens} tokens, ${c.cost ?? "n/a"}`);
```

## TypeScript support matrix (esbuild → Jint)

| Feature                                         | Support |
|-------------------------------------------------|---------|
| `interface`, `type`, `enum` (non-const)         | ✅      |
| `import`/`export` (relative paths)              | ✅      |
| `async`/`await`, `Promise`, `Promise.all`       | ✅      |
| Destructuring, spread, defaults, rest           | ✅      |
| Classes, getters/setters, inheritance           | ✅      |
| Template literals                               | ✅      |
| `Map`/`Set`/`WeakMap`/`WeakSet`                 | ✅      |
| `JSON.parse`/`JSON.stringify`                   | ✅      |
| Regex (no lookbehind)                           | ✅      |
| npm modules (`fs`, `path`, `node:*`)            | ❌ Use `ctx.services.get("fs")`. |
| `fetch`, `setTimeout`, `setInterval`            | ❌/⚠️    |
| Decorators (`@experimental`)                    | ❌ esbuild stops at stage-3. |
| `tsconfig` paths/aliases                        | ⚠️ relative imports only. |

## Constraints (worth knowing)

- **Discovery at startup only.** Edit a script, restart the runner. No hot
  reload (deliberate — spec §14).
- **One engine per script file, kept warm.** Every command a file declares shares
  that file's Jint state across invocations; commands from different files are
  isolated.
- **Jint is not thread-safe.** The runner serialises invocations; a
  `SemaphoreSlim` guards each engine defensively.
- **Sandbox limits (CLI profile)**: 64 MB memory, 100 max recursion, 5 min
  execution. Override via `Orkeon:Cli:ScriptCommands:Limits`.
- **Conflict policy**: first script wins by ordinal path order; the second is
  logged at Warning. Toggle `ContinueOnConflict=false` to fail fast.
- **VFS only.** Scripts read disk through `ctx.services.get("fs")` — no
  direct `System.IO`.

## Troubleshooting

| Symptom                                  | Likely cause                                                                | Action                                                          |
|------------------------------------------|------------------------------------------------------------------------------|-----------------------------------------------------------------|
| `hello` doesn't appear in `/help`        | Script outside the configured `Directories`, or evaluation error logged Error| Check logs from `Orkeon.Cli.Commands.Scripting.Loading.ScriptCommandLoader`. |
| `Esbuild binary not found. Tried (in order): …` | No esbuild on the lookup path                                         | Set `Orkeon:Scripting:Toolchain:EsbuildPath` or `ORKEON_ESBUILD_PATH`, run `npm ci` in `tools/scripting-esbuild/`, or put `esbuild` on `PATH` — `npm install -g esbuild` does; the dotnet tool does not ship it ([lookup order](./scripting.md#configuration-and-toolchain)). |
| `defineCommand is not defined`           | Script evaluated before bindings (bug)                                       | File an issue with the script path.                              |
| Prompt doesn't render in the REPL pane   | Adapter not Terminal.Gui or prefix heuristic missed                          | Ensure the script writes prompts with a `> ` suffix.            |
| `Error: Argument '--target' value 'staging' is not in choices [dev, prod].` | Typo or stale `choices`         | Use `/help-cmd <name>` to see the live signature.                |
| A line runs nothing / `Unknown command`  | Typed without the leading `/`, and no fallback command                        | Prefix commands with `/`, or define the `FallbackCommandName` command. |
