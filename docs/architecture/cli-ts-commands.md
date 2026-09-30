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
| `--mount <phys:virt:rights>`      | Add a VFS mount (repeatable) — e.g. to make a commands directory reachable.         |
| `--crews-dir <path>`              | Add a crew-resolution directory (repeatable; mounted read-only as `/crews`, `/crews-1`, …) — what makes `script-host` / `runCrewAsync("name")` resolvable. |

Every value flag accepts `--flag value` and `--flag=value`. `orkeon-repl` also takes `--ui` and
`--repl-wrap` ([CLI reference](../reference/cli.md#orkeon-repl--the-separate-interactive-console)).

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
        "MaxScripts": 50,
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

`ctx.command.rawInput` currently holds the command name, not the line as typed. `ctx.progress`
returns a handle with `advance(label?)`, `set(value, label?)` and `done(label?)`. A global
`console.log/info/debug/warn/error` forwards to `ctx.log`.

The full `.d.ts` is shipped as an embedded resource inside
`Orkeon.Cli.Commands.Scripting.dll`; nothing writes it to disk yet. For IDE autocompletion,
copy `src/cli/Orkeon.Cli.Commands.Scripting/Typings/orkeon-cli.d.ts` to `.orkeon/orkeon-cli.d.ts`
next to your scripts — the path the examples' `/// <reference path=…>` line expects. A command
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
const fs = ctx.services.get<IFileSystemService>("fs");
const tools = ctx.services.get<IBaseTool[]>("tools");
if (ctx.services.has("llm")) { /* … */ }
```

The host whitelist drives what's reachable. Default keys: `fs` and `tools`, plus the
optional `llm`, `logger`, `commands` (the dispatch façade — see below) and `script-host`
(crew launching from a command — see
[Driving crews from the REPL](./coding-agent-ts.md)). `configuration` is deliberately **not**
exposed — the configuration root carries API keys — and asking for a key outside the
whitelist throws. Hosts add their own by passing an `Action<ScriptServiceWhitelist>` to
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
value — a sync command is meant to block):

```typescript
defineCommand({
  name: "ask",
  description: "Ask the 'echo' agent and wait.",
  args: { text: { type: "string", required: true } },
  handler(args, ctx) {
    const res = ctx.services.get("commands").request("echo", "run", args.text);
    return res.success ? ctx.continue("→ " + res.payload)
                       : ctx.continue("agent error: " + res.error);
  },
});
```

`request(agent, intent, payload)` returns `{ agent, intent, success, payload, error? }`.

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
  its lock — no need to type another command.
- For a value you want to read on demand, poll with `result --ticket=…` (below).

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
| `inspect --ticket=<t>`     | Full detail of one instance (state, agent, elapsed, result, progress). A bare ticket works too: `/inspect t3`. |
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
(0.5), `DefaultMinimumLogLevel` (`Information`), `LogsBufferCapacity` (5000),
`LogsPaneTitle` (`Logs`), `LogsVisibleAtStartup` (`false`), `ReplWordWrap` (`true`),
`BannerEnabled` (`true`), `Banner`, `Glyphs` (`Auto`), `SpinnerVerbs`. The keys: `Ctrl+G` logs
drawer, `Ctrl+R` REPL pane, `Ctrl+L` / `Ctrl+K` clear the logs / the REPL, `Ctrl+F` find in the logs,
`Ctrl+↑` / `Ctrl+↓` resize, `F2` / `Shift+F2` more / less log detail, `F3` wrap, `F4` agents
pane, `Ctrl+C` cancel the running command (twice within two seconds to force-quit), `Ctrl+Q`
quit. `orkeon-repl` picks this console with `--ui` ([CLI reference](../reference/cli.md#orkeon-repl--the-separate-interactive-console)).

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
| `Esbuild binary not found. Tried (in order): …` | No esbuild on the lookup path                                         | Set `ORKEON_ESBUILD_PATH`, run `npm ci` in `tools/scripting-esbuild/`, or put `esbuild` on `PATH` ([lookup order](./scripting.md#configuration-and-toolchain)). |
| `defineCommand is not defined`           | Script evaluated before bindings (bug)                                       | File an issue with the script path.                              |
| Prompt doesn't render in the REPL pane   | Adapter not Terminal.Gui or prefix heuristic missed                          | Ensure the script writes prompts with a `> ` suffix.            |
| `Error: Argument '--target' value 'staging' is not in choices [dev, prod].` | Typo or stale `choices`         | Use `/help-cmd <name>` to see the live signature.                |
| A line runs nothing / `Unknown command`  | Typed without the leading `/`, and no fallback command                        | Prefix commands with `/`, or define the `FallbackCommandName` command. |
