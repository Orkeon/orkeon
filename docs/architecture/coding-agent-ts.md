> 🇫🇷 [Version française](../fr/architecture/coding-agent-ts.md)

# Orkeon Coding Agent (TypeScript)

The coding agent is an agentic coding assistant (à la Claude Code) built **on Orkeon's
scripted stack** — the `*.cmd.ts` slash-command registry (`Orkeon.Cli.Commands.Scripting`), the
`crew.ork.ts` crew runtime (`Orkeon.Scripting`), and the C# `ToolBase` tools. It is the
subject of `experiments/07-orkeon-coding-agent-ts/` (spec + plan + results).

> The `experiments/` pointers on this page reference the maintainers' **private `experiments`
> submodule** — it is not available in a public clone of the repository. The architecture
> documented here is public; the experiment's sources and scripts are not.

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
/cost /diff …         ScriptHost.RunFromFileAsync       (lightweight reply, no ctx)
                      .body() / ctx.llm.act / budget
                                │ tools.<camelCase>(params)
                                ▼
                      C# tools (IBaseTool / ToolBase)
                                │
                      ISessionBufferService · ICategoryMemoryStore · ICostBudgetManager
```

The runtime rule (`COMMAND-DISPATCH-DESIGN.md §2`): the command registry is a **control
plane**, not a concurrency engine. All TS runs in Jint (single-threaded, no event loop). A
command — sync or async — never does long work itself; it *talks* to a host-side engine via
the service whitelist. `script-host` **is** that engine for crews.

## The `script-host` service (cmd → crew bridge)

`ctx.services.get("script-host")` exposes `ScriptHostFacade`:

| Method | Semantics |
|---|---|
| `runCrew(name, input?)` | Loads `crews/<name>/crew.ork.ts`, runs it via `ScriptHost.RunFromFileAsync` (honours `.body()` + `ctx.llm`), **waits**, returns `CrewRunOutput`. Short crews. |
| `runCrewAsync(name, input?)` | Posts the run on a pool thread, returns a **ticket** immediately. Completion drains to a `defineAsyncCommand`'s `completed(result)` via the same ticket cycle as `commands.post`. Long workflows. |
| `listCrews()` | Discovered crew names. |

`input` is handed to the crew engine as `globalThis.inputs` (a JS object parsed from JSON
before evaluation — the `ScriptHost` pre-execution hook). Crews read `globalThis.inputs`.

## The interactive loop (`main-loop`)

The loop is an agent whose **`.body()` is the loop**: it calls `ctx.llm.act(prompt, opts)`, which
runs the LLM ⇄ tool-calling cycle over the agent's tool catalogue. It is launched as a crew
(`runCrewAsync("main-loop", { prompt, permissionMode })`) — *not* via `onCommand`, which has
no `ctx`. Conversation continuity across runs comes from the singleton `ISessionBufferService`.

`ActOptions.system` seeds a **real `role:"system"` message** ahead of the user prompt
(persisting across every iteration of the tool loop). Without it, `act()` sends a single
user message — which is how scripted agents ran until this option existed: identity and
tool policy travelled with user-level authority, and the providers' native system handling
(Anthropic top-level `system`, `cache_control`) never fired. A conversation-level system
message wins over `LlmConfig.SystemMessage` on every provider.

Permission gate: a first-class DI service, `IPermissionGate`/`ModePermissionGate`
(`Orkeon.Infrastructure.Security`), consulted per tool call inside `ctx.llm.act`. Four
modes (`bypassPermissions`, `plan`, `acceptEdits`, `default`), read/write classification
from the tool's own `IBaseTool.Access` declaration (with a curated read-tool table and
`codebase_`/`symbol_`/`index_` prefixes as fallback), fail-closed on unknown tools, and
an interactive approval channel — all behind `Orkeon:Security:PermissionGate:Enabled` /
`:Interactive` (wired by the REPL and `RunnerHost`; no-op when disabled). Budget:
`process("autonomous")` + `.budget({...})` (`AgentExecutionBudget`, 5 dimensions).

## The session primitives (Phase 2 / 6)

- **`ISessionBufferService`** (singleton) — the conversation buffer: messages, metadata,
  head+tail truncation, token estimate. Pivot of the loop and the session commands.
- **`ICategoryMemoryStore`** — typed memory CRUD over four categories
  (user/project/feedback/reference).
- **`ICostBudgetManager`** — cumulative cost/token/call telemetry (existing; now DI-wired).

Six create-tools expose these to scripts: `session_store`, `session_snip`, `token_budget`,
`memory_store`, `session_cost`, `session_stats`. Register them with `AddOrkeonSessionTools()`.

## Tools in commands (`tools.*` in `.cmd.ts`)

`JsEngineFactory.Create()` already registers the `tools` namespace; the CLI factory is now
constructed with the built-in tools (and LLM provider), so `tools.<camelCase>(params)` works
in `.cmd.ts` handlers and in crews alike. This is how `/cost`, `/diff`, `/memory`, … call
tools directly.

## Running it (maintainers only)

The launch scripts live in the private `experiments` submodule, so this section applies to
maintainers with the submodule checked out:

```bash
# REPL (loads the 57 commands)
DEEPSEEK_API_KEY=sk-... bash experiments/07-orkeon-coding-agent-ts/run-repl.sh

# A single crew standalone (honours .body() + ctx.llm)
bash experiments/07-orkeon-coding-agent-ts/run-crew.sh crews/git-commit/crew.ork.ts
```

The REPL needs an LLM key for the crews/loop. The 57 commands and the crew launching are
exercised by automated tests (the TypeScript `command-surface.test.ts` suite, `ScriptHostFacadeTests`) without a
key. The acceptance matrix lives in `experiments/07-orkeon-coding-agent-ts/RESULTS.md`
(private `experiments` submodule).
