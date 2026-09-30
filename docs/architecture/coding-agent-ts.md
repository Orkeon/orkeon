> 🇫🇷 [Version française](../fr/architecture/coding-agent-ts.md)

# Driving crews from the REPL — the `.cmd.ts` → `crew.ork.ts` bridge

A scripted command is a **control plane**: it parses what you typed and decides what should
happen. A crew is an **engine**: it does the work. The seam between them is the `script-host`
service, and this page documents that seam — the pattern behind an agentic coding assistant
built on Orkeon's scripted stack, where `*.cmd.ts` commands
(`Orkeon.Cli.Commands.Scripting`) drive `crew.ork.ts` crews (`Orkeon.Scripting`) over C#
`ToolBase` tools.

Everything below is shipped and runnable from a clone. The
[`## Run it`](#run-it) section at the end launches it.

## Architecture: control plane vs engine

```
REPL (Orkeon.ConsoleApp --runner=scripted-commands)
  │  parses "/cmd args" + free text
  ▼
*.cmd.ts  (Orkeon.Cli.Commands.Scripting)              ══ CONTROL PLANE ══
  defineCommand (sync) / defineAsyncCommand (async)        never does the work itself
  │                         │                          │
  │ tools.*  (direct ops)   │ services.get("script-host")│ services.get("commands")
  ▼                         ▼  .runCrew / .runCrewAsync   ▼  .request / .post
ops thin              ══ ENGINE: crew.ork.ts ══       living agent .onCommand(env)
/hello /crews         ScriptHost.RunFromFileAsync       (lightweight reply, no ctx)
                      .body() / ctx.llm.act / budget
                                │ tools.<camelCase>(params)
                                ▼
                      C# tools (IBaseTool / ToolBase)
                                │
                      ISessionBufferService · ICategoryMemoryStore · ICostBudgetManager
```

The rule the diagram encodes: the command registry is a control plane, not a concurrency
engine. All TypeScript runs in Jint — single-threaded, with an event loop but no timers — so a command, sync or
async, never does long work itself. It *talks* to a host-side engine through the service
whitelist, and `script-host` **is** that engine for crews. The dispatch side of the same rule
is in [TypeScript CLI commands](cli-ts-commands.md).

## The `script-host` service (cmd → crew bridge)

`ctx.services.get("script-host")` exposes `ScriptHostFacade`:

| Method | Semantics |
|---|---|
| `runCrew(name, input?)` | Loads `<crews-dir>/<name>/crew.ork.ts`, runs it via `ScriptHost.RunFromFileAsync` (honours `.body()` + `ctx.llm`), **waits**, returns `CrewRunOutput` (`{ ok, summary?, artifacts?, error? }`). Short crews: the wait is bounded by `RunCrewTimeout` (below), past which the script receives a `TimeoutException`. |
| `runCrewAsync(name, input?)` | Posts the run on a pool thread, returns a **ticket** immediately. Completion drains to a `defineAsyncCommand`'s `completed(result)` via the same ticket cycle as `commands.post`. Long workflows. |
| `listCrews()` | Discovered crew names. |

An unknown crew is not an exception: `runCrew` returns `ok: false` with
`crew '<name>' not found under: …`. `runCrewAsync` needs the dispatch substrate `AddScriptCommands` registers,
and throws without it.

`ScriptHost` runs the file **procedurally**: a crew that ends with `globalThis.crew = crew`
is not handed to an orchestrator here, it is run the way `await crew.run()` would run it —
bodies run, tasks ignored ([the two shapes](../reference/scripting-dsl.md#the-two-shapes)).
Write the crews this bridge launches in the procedural shape.

A crew is resolved by name against the directories the host was given: `--crews-dir` is
repeatable, each directory is mounted read-only as `/crews`, `/crews-1`, …
(`CliCrewMountBootstrapper`), and the crew called `review` is the file
`<dir>/review/crew.ork.ts`; the first directory that has it wins. `input` reaches the crew as
`globalThis.inputs`, a JS object parsed from JSON before evaluation by the `ScriptHost`
pre-execution hook, and the crew's `globalThis.result` comes back as the `summary` (a string as is, anything
else serialised to JSON).

The same resolution can be configured under `Orkeon:Cli:ScriptHost` (`ScriptHostFacadeOptions`):

| Key | Default | Meaning |
|---|---|---|
| `CrewDirectories` | empty | Virtual directories searched in order; `--crews-dir` appends to it. |
| `CrewFileName` | `crew.ork.ts` | The entry file looked up under `<dir>/<name>/`. |
| `RunCrewTimeout` | `00:10:00` | Bound on one synchronous `runCrew`; zero or negative disables it. |

## An agent loop in a crew — `ctx.llm.act`

An interactive agent is an agent whose **`.body()` is the loop**: it calls
`ctx.llm.act(prompt, opts)`, which runs the LLM ⇄ tool-calling cycle over the agent's own
tool catalogue until the model stops asking for tools or `maxIterations` is reached
(`src/scripting/Orkeon.Scripting/Typings/context.d.ts`, `act<T>` and `ActOptions`). The tools
the model is offered are the built-ins the agent selected with `.tools([...])`. It is launched
as a crew — `runCrewAsync(name, { prompt, permissionMode })`, the crew passing
`inputs.permissionMode` on to `act` — and *not* via `onCommand`, which has no `ctx` and so
cannot reach the LLM. Conversation continuity across runs comes from the
singleton `ISessionBufferService`.

`ActOptions.system` seeds a **real `role:"system"` message** ahead of the user prompt,
persisting across every iteration of the tool loop. Without it, `act()` sends a single user
message — which is how scripted agents ran until this option existed: identity and tool
policy travelled with user-level authority, and the providers' native system handling
(Anthropic top-level `system`, `cache_control`) never fired. A conversation-level system
message wins over `LlmConfig.SystemMessage` on every provider.

## Permissions and budget

The permission gate is a first-class DI service, `IPermissionGate`
(`Orkeon.Application.Interfaces.Security`) implemented by `ModePermissionGate`
(`Orkeon.Infrastructure.Security`), consulted per tool call inside `ctx.llm.act`. Four modes
(`bypassPermissions`, `plan`, `acceptEdits`, `default`), read/write classification from the
tool's own `IBaseTool.Access` declaration (with a curated read-tool table and
`codebase_`/`symbol_`/`index_` prefixes as fallback), fail-closed on unknown tools and unknown
modes. There is no interactive approval yet: a call the mode does not allow is denied, and the
model reads `DENIED: …` as the tool's answer; `Interactive` (default `false`) only changes that
message, the approval flow itself being a follow-up. All of it sits behind
`Orkeon:Security:PermissionGate:Enabled` (default `false`) / `:Interactive`, wired by the REPL
and `RunnerHost` (`AddOrkeonPermissionGate`), no-op when disabled. In the TUI, Shift+Tab cycles
the session's default mode. See [opt-in subsystems](../reference/opt-in-subsystems.md).

Budget is the other bound: `process("autonomous")` plus `.budget({...})`
(`AgentExecutionBudget`, five dimensions).

## The session primitives

Three ports carry what a conversation needs to survive between runs:

- **`ISessionBufferService`** (singleton) — the conversation buffer: messages, metadata,
  head+tail truncation, token estimate. Pivot of the loop and the session commands.
- **`ICategoryMemoryStore`** — typed memory CRUD over four categories
  (user/project/feedback/reference).
- **`ICostBudgetManager`** — cumulative cost/token/call telemetry.

`AddOrkeonSessionTools()` exposes them to scripts as six tools — `session_store`,
`session_snip`, `token_budget`, `memory_store`, `session_cost`, `session_stats` — catalogued
with everything else in [the tool inventory](../tools/inventory.md).

## Tools in commands (`tools.*` in `.cmd.ts`)

`JsEngineFactory.Create()` registers the `tools` namespace, and the CLI factory is
constructed with the built-in tools and the LLM provider, so `tools.<camelCase>(params)`
works in a `.cmd.ts` handler exactly as it does in a crew. That is how a command does a small
piece of work itself instead of paying for a crew — reading a file, formatting a report —
while anything long goes through `script-host`.

## Run it

Two directories, two roles. `--commands-dir` loads the control plane, `--crews-dir` supplies
the engines. Both point at material shipped in this repository, and neither needs an API key:

```bash
# The control plane alone — the demo commands in the repo
dotnet run --project src/apps/Orkeon.ConsoleApp -- \
    --runner=scripted-commands \
    --commands-dir examples/cli-ts-commands

# The bridge — commands that launch a crew by name through script-host
dotnet run --project src/apps/Orkeon.ConsoleApp -- \
    --runner=scripted-commands \
    --commands-dir examples/cli-ts-commands \
    --crews-dir    examples/cli-ts-commands/crews
```

```
scripted> /crews
review

scripted> /review src/Program.cs
src/Program.cs: source file — worth a read

scripted> /review-bg examples/README.md
  ✓ [t1] review-bg → crew:review: done
✓ review: examples/README.md: not a source file — skipped
```

The demo crew calls no model, which is why this runs keyless; point an agent's `.body()` at
`ctx.llm.act` and the same bridge carries a real one. Without the REPL, a crew runs straight
from the CLI:

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/01-hello-world.ork.ts
```

The bridge itself is covered by `ScriptHostFacadeTests` in
`tests/cli/Orkeon.Cli.Commands.Scripting.Tests/`, which exercises crew resolution and the
ticket cycle without a key.

## Where to go next

- [Scripting DSL — architecture](scripting.md): what the `.ork.ts` runtime is and where it sits.
- [TypeScript CLI commands](cli-ts-commands.md): the control plane in full — `defineCommand`,
  argument schemas, dispatch to agents.
- [`examples/cli-ts-commands/`](https://github.com/Orkeon/orkeon/blob/main/examples/cli-ts-commands/README.md):
  the source of the session above.
