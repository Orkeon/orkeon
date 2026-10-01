> 🇫🇷 [Version française](../fr/architecture/scripting.md)

# Scripting DSL — architecture

The Orkeon scripting DSL is a TypeScript-syntax language embedded in the runtime,
implemented by the `Orkeon.Scripting` project (and exposed via the `orkeon` CLI).
It is the recommended way to author crews when you don't want to write C#.

## Why a DSL

Authors familiar with frontend tooling get a single-file authoring experience
(`.ork.ts`) without a compilation step on their side: the runtime hands the
file to esbuild — which strips the types and, when the file is on disk, bundles
its relative imports — and runs the resulting JavaScript inside Jint with
sandbox limits. The DSL surfaces the full Orkeon surface — agents, crews,
tasks, custom tools, state machines, graphs, events, locks, lifecycle hooks —
through fluent builders and literal declarations.

## Where it lives in Clean Architecture

`Orkeon.Scripting` depends on `Orkeon.Domain`, `Orkeon.Application`,
`Orkeon.Infrastructure` and `Orkeon.Rag.Abstractions` (the `rag.*` namespace).
It does not modify those layers: it is an opt-in adapter that translates JS-side
calls into existing domain invocations. It registers nothing in DI of its own —
there is no `AddOrkeon*` extension: the CLI, the shared runner and the REPL
(`AddScriptCommands`, see [CLI TypeScript commands](./cli-ts-commands.md))
construct the `ScriptHost` themselves.

```
src/
└── scripting/
    ├── Orkeon.Scripting/         ← this layer (Jint runtime + bindings)
    │   ├── ScriptHost.cs
    │   ├── JsEngineFactory.cs
    │   ├── ScriptingHostPorts.cs ← permission gate + delta sink handed in by the host
    │   ├── Adapters/             ← JsCrewConfigurationAdapter (the declarative handoff → CrewConfiguration)
    │   ├── Builders/             ← JsAgentBuilder, JsCrewBuilder, JsTaskBuilder, JsToolBuilder
    │   ├── Bindings/             ← global registrations (agentBuilder, crewBuilder, llm, tools, rag, …)
    │   ├── Runtime/              ← JsCrew, JsExecutionContext, JsAgentContext, JsLlmFacade, …
    │   ├── Orchestration/        ← JsStateMachine, JsStateGraph
    │   ├── ErrorPolicy/          ← JsErrorAction, ErrorCodeMapper
    │   ├── Exceptions/           ← the typed runtime exceptions (StateMutationOutsideWithException, …)
    │   ├── Internal/             ← JsHostError, JS trampoline factories
    │   ├── Configuration/        ← ScriptingLimitsOptions, ScriptingToolchainOptions
    │   ├── Versioning/           ← the `/// <reference orkeon-script="1.0" />` directive
    │   ├── Telemetry/            ← ScriptingActivitySource
    │   ├── Testing/              ← mock LLM / mock tool doubles (not exposed to scripts)
    │   ├── Toolchain/            ← EsbuildTranspiler, PassThroughTranspiler
    │   └── Typings/              ← the *.d.ts declarations
    └── Orkeon.Scripting.Cli/     ← the `orkeon` tool: `run`, `forge`, `init`, `doctor`, `llm`, `rag`, `usecases`, `email`
```

## Quick start

The runnable tutorials live in [`examples/scripting/`](https://github.com/Orkeon/orkeon/tree/main/examples/scripting)
(hello world through FSM/graph literals and RAG). End-to-end:

```bash
dotnet build src/scripting/Orkeon.Scripting.Cli/Orkeon.Scripting.Cli.csproj
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/01-hello-world.ork.ts
```

The CLI emits the script result as JSON on stdout; exit codes follow the usual
convention (`0` ok, `1` script error, `2` runtime error, `130` cancelled).

The same `run` verb also accepts a **YAML crew** — the target selects the
path (`.yaml`/`.yml` **or a directory holding a multi-file crew** → the shared
crew runner; any other file → Scripting DSL). A script that assigns
`globalThis.crew` is a *declarative* crew definition and goes to the shared
runner too, like YAML; the others run on the scripting host
([the two shapes](../reference/scripting-dsl.md#the-two-shapes)):

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/09-experimental/llm-response-format/crew.yaml
```

For YAML crews and declarative scripts the tool delegates to the shared
one-shot runner (`Orkeon.Hosting`'s `RunnerExecution`), so the YAML-only flags
apply: `-V/--var KEY=VALUE`, `--initial-context`, plus the shared
`--settings/--mount/--mount-id/--allow-external-mounts/--verbose/--llm-log[-path]`
and the diagnostics/protocol flags (`--validate`, `--list-tools`, `--events jsonl`,
`--stream`, `--client`). The procedural-script flags (`--inputs`, `--inputs-file`,
`--memory-limit-mb`) are ignored on that path — a declarative script says so on
stderr. The shared runner prints the crew output under a `=== Crew Output ===`
banner instead of a JSON `result`. Every option: [the CLI reference](../reference/cli.md#orkeon-run).

## API recap

The declarations are the reference. They ship with the DSL, they are what your editor
reads, and they live under `src/scripting/Orkeon.Scripting/Typings/` — concatenated at build
into one `orkeon.d.ts` that the `orkeon` tool carries and writes next to your project with
`orkeon typings` ([Reference](#reference)).

Two pages sit on top of them: [Write a crew in TypeScript](../guides/write-a-crew-in-typescript.md)
for the narrative, and the [Scripting DSL reference](../reference/scripting-dsl.md) for the
method-by-method table — including the column the declarations cannot carry, **which of the
two script shapes actually honours each method**.

| Concept | Where to look |
|---------|---------------|
| `agentBuilder()` / `crewBuilder()` / `taskBuilder()` / `toolBuilder()` | `agent.d.ts`, `crew.d.ts`, `task.d.ts`, `tool.d.ts` |
| `ExecutionContext` and `AgentContext` (`ctx.llm`, `ctx.memory`, A2A, locks, spawn) | `context.d.ts` |
| `ctx.llm.act` — the LLM ⇄ tool-call loop, and its `ActOptions` | `context.d.ts` |
| Events (`ctx.events.queue` / `ctx.events.topic`) | `events.d.ts` |
| `stateMachine` / `stateGraph` literal forms | `fsm.d.ts`, `graph.d.ts` |
| `onError`, `ErrorAction`, error codes | `agent.d.ts`, `errors.d.ts` |
| The globals the runner exchanges with the script (`crew`, `inputs`, `result`) | `globals.d.ts` |
| The version directive `/// <reference orkeon-script="1.0" />` | `orkeon-script.d.ts` |
| Lifecycle hooks (`onAgentStart`, `onCrewComplete`, …) | `agent.d.ts`, `crew.d.ts` |
| `onCommand` — answer dispatched CLI commands by name | [cli-ts-commands.md](./cli-ts-commands.md#the-agent-side--oncommand) |
| Built-in `tools.X(...)` namespace | `tools.d.ts` |
| The agent's model settings (`llm.default_`, `llm.model(...)`) | `llm.d.ts` |
| RAG (`rag.ingest`, `rag.query`, `rag.retrieve`) | `rag.d.ts` |

## Coexistence with YAML

The YAML configuration path remains supported and unchanged. Scripts and YAML
crews can share the same hosting application: the DSL is one of several
authoring surfaces, not a replacement. The published `orkeon` tool now runs
**both** surfaces directly (`orkeon run crew.yaml` and `orkeon run crew.ork.ts`),
so an external consumer who depends only on the published packages no longer has
to compile a bespoke runner to execute YAML crews.

## Blocking calls from scripts

Jint executes a script on a single thread: a host call that waits for its
result blocks the whole script — and the REPL hosting it — until it returns.
Two CLI bridges deliberately keep that synchronous contract (audited as
ANT-007/ANT-010, "assumed blocking by design"):

- `ctx.services.get("script-host").runCrew(name, input?)` — runs a crew and
  waits for its output. **Short crews only.** The wait is bounded by a
  configurable timeout (`ScriptHostFacadeOptions.RunCrewTimeout`, config
  section `Orkeon:Cli:ScriptHost`, default **10 minutes**): on expiry the
  script receives a clear `TimeoutException`, the abandoned run is cancelled
  cooperatively, and the REPL thread is always released (a pure-JS busy loop
  that ignores cancellation is eventually reaped by the Jint sandbox's own
  `ExecutionTimeout`).
- `ctx.services.get("commands").request(agent, intent, payload)` — blocks until
  the agent replies; same guidance applies
  (see [cli-ts-commands.md](./cli-ts-commands.md#synchronous-dispatch--request)).

The nominal path for long work is the ticket cycle: `runCrewAsync(name, input?)`
(backed by `CommandDispatchService.postWork`, same mechanism as `commands.post`)
returns a ticket immediately and delivers the crew summary to a
`defineAsyncCommand`'s `completed(result)` callback.

## Threading model

A Jint engine is not bound to a thread, but it is **single-drainer**: one thread at a time
runs its event-loop jobs, and a drain started from inside a job cannot pump — it would wait
on jobs that only the thread it is blocking could run. The family of defects SCR-25 removed
(a crew run that hung after any top-level `await`, a state graph with suspending nodes that
timed out from a body, concurrent `ctx.state.with` calls that crashed the engine, a topic
handler or an FSM hook that suspends, an `onDelta` callback on a pool thread, `runStream`
not iterable) all came from CLR code re-entering the engine from the wrong side: after an
`await`, or by draining from inside a job. The runtime now follows one rule, and the
reproducers in `tests/scripting/Orkeon.Scripting.Tests/Runtime/EngineThreadingContractTests.cs`
pin it:

- **Every loop that calls back into script code lives in JavaScript.** The crew run
  (`crew.run`, `crew.runAgent`, `crew.runStream`), the state graph (`run`, `runStream`), the
  state machine (`send`), `ctx.state.with`, topic delivery (`publish`, the event's `lock`)
  and the script-facing side of `ctx.llm.act` are async functions — async generators for the
  streams, so `for await` works on them — built once from a JavaScript factory. The CLR
  supplies only synchronous helpers (snapshot the agents, open an attempt, record a result)
  and `Task`s the loop awaits (a semaphore acquisition, a retry delay, the run's cancellation).
  Every line of those loops runs as a promise reaction on whichever thread is draining.
- **A CLR callback never re-enters the engine after an `await`.** A delegate exposed to the
  script may return a value synchronously, a `Task` whose result is a CLR value (Jint settles
  it on the loop), or a JS promise obtained synchronously. It never calls `Invoke`,
  `Evaluate`, `FromObject` or `SetValue` from a continuation, and never drains from inside a
  job.
- **A CLR helper reports failure as a JavaScript throw.** `JsHostError` wraps the exception in
  an `Error` that carries it on `clr` (its type name on `clrType`), so the script's `catch`
  and `finally` run, the loop releases what it holds, and the CLR side recovers the typed
  exception from the rejected value.
- **The CLR drives the engine at three root pumps only**, each with the engine at rest, under
  the per-engine gate, on one thread for the whole evaluation — the synchronous prefix and
  every job after it: `ScriptHost` for the script itself, `JsCrew.RunAsync` for the
  `globalThis.crew` handoff and for C# hosts, `JsTool.CallAsync` for a script tool the
  orchestrator calls. From C#, `JsCrew.run`, `runAgent` and `runStream` are the JS functions
  themselves (`JsValue`); `RunAsync` is the CLR entry, and it refuses to start from inside a
  CLR callback the script invoked — from a script, call `crew.run()`.
- **Cancellation is the only bound on a wait; `ExecutionTimeout` bounds execution.** No
  promise timeout remains in the runtime: a root pump drains until its promise settles or its
  token fires. The sandbox's `ExecutionTimeout` (wall-clock, `Orkeon:Scripting:Limits`) bounds
  the evaluation itself and now spans a whole CLR-driven run, as it already spanned a whole
  script (so does `MemoryLimitBytes`). A cancelled `RunAsync` leaves the loop a short grace to
  unwind — its `finally` blocks, `onCrewError` — before it abandons the run and releases what
  the CLR owns. A run a body opens without a `signal` of its own — `crew.runAgent`, a
  sub-crew's `run` — is a child of the run that opened it: cancelled with it, unwound inside
  its unwind. The parent is the most recently opened attempt still open on the engine, exact
  under sequential nesting and best-effort under runs interleaved on one event loop;
  `{ signal: ctx.signal }` is the explicit form.

## Configuration and toolchain

The sandbox limits are read from `Orkeon:Scripting:Limits` (`ScriptingLimitsOptions`) by the
`orkeon run` script path, the shared runner and the forge:

| Key | Default | Meaning |
|---|---|---|
| `MemoryLimitBytes` | `104857600` (100 MB) | Jint memory ceiling. `orkeon run --memory-limit-mb` overrides it for a procedural script; `0` or less disables it. |
| `RecursionLimit` | `64` | Maximum JavaScript call depth. |
| `ExecutionTimeout` | `00:00:30` | Wall-clock bound of one evaluation — a whole script, or a whole CLR-driven crew run. |

The `*.cmd.ts` commands of the REPL use their own profile, `Orkeon:Cli:ScriptCommands:Limits`
([CLI TypeScript commands](./cli-ts-commands.md#appsettingsjson)).

The esbuild toolchain is read from `Orkeon:Scripting:Toolchain` (`ScriptingToolchainOptions`),
by every host that transpiles: `orkeon run`, the shared runner, `orkeon doctor`, `orkeon forge`
and the REPL's `*.cmd.ts` commands:

| Key | Default | Meaning |
|---|---|---|
| `EsbuildPath` | — | Absolute path to the esbuild binary; first in the lookup order below. |
| `EsbuildTimeout` | `00:00:30` | Wall-clock bound of one esbuild transpilation or bundle. |

esbuild is looked up, in order: `Orkeon:Scripting:Toolchain:EsbuildPath`, the
`ORKEON_ESBUILD_PATH` environment variable, an `esbuild-bin/esbuild[.exe]` next to the running
binary (what the release archives ship), the repository's `tools/scripting-esbuild/node_modules/`
(installed by the `Orkeon.Scripting` build with `npm ci`, skipped with
`-p:SkipScriptingNpmInstall=true`; searched up to eight parent folders from the working directory
and from the binary), then `PATH`. When nothing is found the error lists every place it tried;
`orkeon doctor` reports it as the `esbuild` check.

**The `orkeon` dotnet tool does not ship esbuild.** Its package carries no `esbuild-bin/`, so
after `dotnet tool install -g Orkeon.Scripting.Cli` a `.ork.ts` script runs only once esbuild is
on the machine: `npm install -g esbuild` (it lands on `PATH`), or a binary you point
`ORKEON_ESBUILD_PATH` or `EsbuildPath` at. Nothing is downloaded on first run. YAML crews never
need it.

A script may open with `/// <reference orkeon-script="1.0" />`. The directive is optional;
when present, a version the runtime does not support (only `1.0` today) fails the run with
`ScriptVersionMismatchError` before anything executes.

## V1 limits

- `concurrency(N)` capped at 1 (mutex). N-holders semaphore is V1.5.
- Locks have no timeout. `LockTimeoutError` is V1.5.
- Streaming through `ctx.llm.stream` is **per-token** when the provider is an
  `IStreamingLlmProvider` (all 16 shipped providers are); the single full-text
  chunk is only the fallback for a non-streaming custom provider.
- `ctx.llm.embed` returns a stub vector; integration with real embedders is a
  follow-up.
- FSM/Graph hierarchical composability (sub-states, sub-graphs) is V1.5.
- Events are in-memory only (no Redis/NATS persistence).

## Reference

This page says what the DSL is and where it sits; the typings say what it exposes. The crew
surface is `orkeon.d.ts` — the `Typings/*.d.ts` above concatenated (`errors.d.ts` first, the
others alphabetically) by the `Orkeon.Scripting` build, which embeds it in the assembly and also
writes it to `src/scripting/Orkeon.Scripting/bin/<configuration>/net10.0/dist/orkeon.d.ts`.
`orkeon-cli.d.ts` is a different file for a different surface: the `*.cmd.ts` commands
documented in [cli-ts-commands.md](./cli-ts-commands.md).

Both ship inside the `orkeon` tool. `orkeon typings` writes them into `./.orkeon/` (or
`--out <dir>`), overwriting what is there — run it again after updating the tool, so the
editor reads the surface of the runtime that will execute the scripts
([CLI reference](../reference/cli.md#orkeon-typings)).
