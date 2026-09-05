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
    ctx.log.info(`Hello, ${args.who}!`);
    return ctx.continue();
  },
});
EOF

dotnet run --project src/apps/Orkeon.ConsoleApp -- \
    --runner=scripted-commands \
    --commands-dir ./commands
```

At the prompt:

```
scripted> help            # 'hello' shows up
scripted> hello           # → Hello, world!
scripted> hello --who=Ada # → Hello, Ada!
scripted> help-cmd hello  # signature with typed args
scripted> exit
```

## CLI flags

| Flag                              | Effect                                                                              |
|-----------------------------------|-------------------------------------------------------------------------------------|
| `--runner=scripted-commands`      | Boot directly into the scripted-commands REPL instead of the main menu.             |
| `--commands-dir <path>`           | Add a directory to scan (repeatable; mounted as `/cli-commands[-N]` in the VFS).    |
| `--no-script-commands`            | Disable discovery entirely. Registry is empty.                                      |
| `--strict-commands`               | Equivalent to `FailFastOnInvalidScript=true + ContinueOnConflict=false` (CI).       |
| `--settings <path>`               | Explicit appsettings path (repeatable — later files override earlier ones).         |
| `--mount <phys:virt:rights>`      | Add a VFS mount (repeatable) — e.g. to make a commands directory reachable.         |
| `--crews-dir <path>`              | Add a crew-resolution directory (repeatable) — what makes `script-host` / `runCrewAsync("name")` resolvable. |

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
conversational agent.

## `defineCommand` reference

```typescript
defineCommand({
  name: "deploy",                 // ^[a-z][a-z0-9-]*$, must not collide with help/exit/clear
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

The full `.d.ts` is shipped as an embedded resource inside
`Orkeon.Cli.Commands.Scripting.dll` (Phase 5 will publish it to disk automatically; for
now copy `src/cli/Orkeon.Cli.Commands.Scripting/Typings/orkeon-cli.d.ts` next to your
scripts for IDE autocompletion).

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
const cfg = ctx.services.get<IConfiguration>("configuration");
const tools = ctx.services.get<IBaseTool[]>("tools");
```

The host whitelist drives what's reachable. Default keys: `fs`, `configuration`,
`tools`, plus optional `llm`, `logger`, `commands` (the dispatch façade — see
below) and `script-host` (crew launching from a command — see
[scripting](./scripting.md)). Hosts add their own by passing a
`Action<ScriptServiceWhitelist>` to `AddScriptCommands`.

## Dispatching commands to agents

A command can address a **live agent by name** and let *its* response decide when
the command is finished. This is the `commands` façade, reached via
`ctx.services.get("commands")`. Under the hood it rides the existing
`IAgentChannel`: the façade resolves the agent name → `AgentId`, correlates the
request/response host-side, and tracks every dispatch in a queryable registry.

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
  completed(result, ctx) {                           // replayed at the next pump
    ctx.writeLine("✓ " + result.agent + ": " + result.payload);
  },
});
```

- `post(agent, intent, payload)` returns a **ticket** (string) right away and runs
  the request on a background task.
- `completed(result, ctx)` is **not** called from the background thread (Jint is
  single-threaded). It is replayed on the engine thread at the next "pump" —
  typically the next command invocation on the same script. A terse host line is
  also printed immediately at completion so you see something without waiting.
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
facade.list({ state: "running" });   // CommandInstanceView[]
facade.get("t3");                    // one view, or undefined
facade.cancel("t3");                 // request cancellation; returns boolean
```

And as **built-in commands** at the REPL (fast, stay responsive while async work
runs in the background):

| Command                    | Effect                                                        |
|----------------------------|---------------------------------------------------------------|
| `ps [--state=…]`           | List instances. State: `running` (default), `done`, `failed`, `cancelled`, `rejected`, `all`. |
| `inspect --ticket=<t>`     | Full detail of one instance (state, agent, elapsed, result, progress). |
| `result --ticket=<t>`      | Print the result payload (poll). Reports "still running" if not done. |
| `cancel --ticket=<t>`      | Request cancellation of an in-flight ticket.                  |

A `CommandInstanceView` carries: `ticket`, `name`, `kind` (`sync`/`async`),
`targetAgent`, `intent`, `correlationId`, `state`, `startedAt`, `completedAt?`,
`elapsedMs`, `result?`, `error?`, `progress?`. Terminal entries are retained for a
while (bounded) so `result`/`inspect` can read a recent ticket, then evicted.

### Wiring (host side)

`AddScriptCommands` registers the dispatch substrate as a singleton (its own
in-memory channel — the CLI dispatch bus — plus the name directory and instance
registry) and adds `commands` to the default whitelist. Agents are connected with
`AgentCommandRegistrar.Register(agent, engine, engineLock, service.Channel, service.Directory)`,
which registers the channel handler and the name→id mapping. See
the maintainers' design archive (feature `cli-ts-commands`, COMMAND-DISPATCH-DESIGN §11) for the full
file map.

### End-to-end example

`examples/cli-ts-commands/` ships `dispatch.cmd.ts` (the `ask` / `ask-bg`
commands) and `echo-agent.ork.ts` (the `onCommand` agent).

```
scripted> ask hello            # → HELLO        (sync, blocks)
scripted> ask-bg hello         # launched (ticket t1)   (async, returns now)
scripted> ps                   # t1 askbg echo running …
scripted> result --ticket=t1   # [t1] HELLO
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
- **One engine per script, kept warm.** Invocations of the same command share
  Jint state; invocations across commands are isolated.
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
| `hello` doesn't appear in `help`         | Script outside the configured `Directories`, or evaluation error logged Error| Check logs from `Orkeon.Cli.Commands.Scripting.ScriptCommandLoader`.     |
| `esbuild not found` on startup           | Tool not installed                                                           | `npm i -g esbuild`, or copy into `tools/scripting-esbuild/`.    |
| `defineCommand is not defined`           | Script evaluated before bindings (bug)                                       | File an issue with the script path.                              |
| Prompt doesn't render in the REPL pane   | Adapter not Terminal.Gui or prefix heuristic missed                          | Ensure the script writes prompts with a `> ` suffix.            |
| `Error: --target value 'staging' is not in choices [dev, prod]` | Typo or stale `choices`                          | Use `help-cmd <name>` to see the live signature.                 |
