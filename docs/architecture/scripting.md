> 🇫🇷 [Version française](../fr/architecture/scripting.md)

# Scripting DSL — architecture

The Orkeon scripting DSL is a TypeScript-syntax language embedded in the runtime,
implemented by the `Orkeon.Scripting` project (and exposed via the `orkeon` CLI).
It is the recommended way to author crews when you don't want to write C#.

## Why a DSL

Authors familiar with frontend tooling get a single-file authoring experience
(`.ork.ts`) without a compilation step on their side: the runtime strips
TypeScript types via esbuild and runs the resulting JavaScript inside Jint with
sandbox limits. The DSL surfaces the full Orkeon surface — agents, crews,
tasks, custom tools, state machines, graphs, events, locks, lifecycle hooks —
through fluent builders and literal declarations.

## Where it lives in Clean Architecture

`Orkeon.Scripting` is a new project that depends on `Orkeon.Domain`,
`Orkeon.Application`, and `Orkeon.Infrastructure`. It does not modify those
layers: it is an opt-in adapter that translates JS-side calls into existing
domain invocations.

```
src/
└── scripting/
    ├── Orkeon.Scripting/         ← this layer (Jint runtime + bindings)
    │   ├── ScriptHost.cs
    │   ├── JsEngineFactory.cs
    │   ├── Builders/             ← JsAgentBuilder, JsCrewBuilder, JsTaskBuilder, JsToolBuilder
    │   ├── Bindings/             ← global registrations (agentBuilder, crewBuilder, …)
    │   ├── Runtime/              ← JsCrew, JsExecutionContext, JsAgentContext, JsLlmFacade, …
    │   ├── Orchestration/        ← JsStateMachine, JsStateGraph
    │   ├── ErrorPolicy/          ← JsErrorAction, ErrorCodeMapper
    │   ├── Telemetry/            ← ScriptingActivitySource
    │   └── Toolchain/            ← EsbuildTranspiler, PassThroughTranspiler
    └── Orkeon.Scripting.Cli/     ← `orkeon run <crew.ork.ts | crew.yaml>`
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
path (`.yaml`/`.yml` **or a directory holding a multi-file crew** → YAML crew
runner, `.ork.ts`/`.js` → Scripting DSL):

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/09-experimental/llm-response-format/crew.yaml
```

For YAML crews the tool delegates to the shared one-shot runner
(`Orkeon.Hosting`'s `RunnerExecution` — the exact code path of `orkeon run`),
so the YAML-only flags apply: `-V/--var KEY=VALUE`, `--initial-context`, plus
the shared `--settings/--mount/--allow-external-mounts/--verbose/--llm-log[-path]`
and the diagnostics/protocol flags (`--validate`, `--list-tools`, `--events jsonl`,
`--stream`, `--client`). The script-only flags (`--inputs`, `--inputs-file`,
`--memory-limit-mb`) are ignored on the YAML path. The YAML runner prints the crew output under a
`=== Crew Output ===` banner instead of a JSON `result`.

## API recap

The declarations are the reference. They ship with the DSL, they are what your editor
reads, and they live under `src/scripting/Orkeon.Scripting/Typings/` — concatenated at build
into the `orkeon.d.ts` the CLI emits.

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
| Lifecycle hooks (`onAgentStart`, `onCrewComplete`, …) | `agent.d.ts`, `crew.d.ts` |
| `onCommand` — answer dispatched CLI commands by name | [cli-ts-commands.md](./cli-ts-commands.md#the-agent-side--oncommand) |
| Built-in `tools.X(...)` namespace | `tools.d.ts` |
| LLM providers (`llm.openai`, `llm.default`, etc.) | `llm.d.ts` |
| RAG (`rag.ingest`, `rag.query`) | `rag.d.ts` |

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

## V1 limits

- `concurrency(N)` capped at 1 (mutex). N-holders semaphore is V1.5.
- Locks have no timeout. `LockTimeoutError` is V1.5.
- Streaming through `ctx.llm.stream` is **per-token** when the provider is an
  `IStreamingLlmProvider` (all 14 shipped providers are); the single full-text
  chunk is only the fallback for a non-streaming custom provider.
- `ctx.llm.embed` returns a stub vector; integration with real embedders is a
  follow-up.
- FSM/Graph hierarchical composability (sub-states, sub-graphs) is V1.5.
- Events are in-memory only (no Redis/NATS persistence).

## Reference

This page says what the DSL is and where it sits; the typings say what it exposes. The crew
surface is `orkeon.d.ts` — built from the `Typings/*.d.ts` above, and emitted next to the CLI
build output. `orkeon-cli.d.ts` is a different file for a different surface: the `*.cmd.ts`
commands documented in [cli-ts-commands.md](./cli-ts-commands.md).
