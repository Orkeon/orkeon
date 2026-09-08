> 🇫🇷 [Version française](../fr/reference/scripting-dsl.md)

# Scripting DSL reference (`.ork.ts`)

Every builder, every method, and the one column that exists nowhere else: **which of the two
script shapes actually honours it**.

The signatures live in `src/scripting/Orkeon.Scripting/Typings/*.d.ts`, concatenated at build
into the `orkeon.d.ts` your editor reads. This page does not repeat them. It answers the
question the declarations cannot: *does the engine that will run my file read this at all?*

For the narrative version — what to write, in what order, and why — see
[Write a crew in TypeScript](../guides/write-a-crew-in-typescript.md).

## The two shapes

A `.ork.ts` file is dispatched to one of **two different engines**, chosen by how the file
ends. `RunCommand.DeclaresCrewHandoffAsync` reads the source looking for an assignment to
`globalThis.crew`, and routes on what it finds.

| | **Procedural** | **Declarative** |
|---|---|---|
| the file ends with | `await crew.run()` | `globalThis.crew = crew` |
| engine | `ScriptHost.RunFromFileAsync` → `JsCrew.RunAsync` | shared runner → `JsCrewConfigurationAdapter` → `ICrewOrchestrationService` |
| runs agent `.body()` | yes, one per agent, in declaration order | **no — ignored** |
| honours `withTask` / `process` / `manager` | **no — ignored** | yes |
| `orkeon run --validate` | fails (*did not assign globalThis.crew*) | works |

They are opposites, not variants. `JsCrew.RunAsync` iterates `_agents` and never looks at
tasks; `Process` reaches only a telemetry tag. The adapter, symmetrically, contains **zero
occurrences** of `body` and of `Budget`.

**The rule:** write `.body()` and you are procedural; write `withTask` and you are
declarative. Never both in one file. A crew with tasks that ends with `await crew.run()`
runs its agents and silently ignores every task you wrote.

## What each shape reads

Measured against `JsCrewConfigurationAdapter.cs` and `JsCrew.cs`, not inferred.

### `agentBuilder()`

| Method | Procedural | Declarative |
|---|---|---|
| `name` `role` `goal` `backstory` | ✅ | ✅ (`role` falls back to `name`) |
| `llm` | ✅ | ✅ |
| `tools([...])` built-ins by name | ✅ | ✅ |
| `withAutonomousTool(s)` instances | ✅ | ✅ |
| `maxIterations` `verbose` `allowDelegation` | ✅ | ✅ |
| `body` | ✅ **the whole point** | ❌ never invoked |
| `withState` → `ctx.state` | ✅ | ❌ |
| `onError` | ✅ | ❌ |
| `onAgentStart` / `onAgentStop` | ✅ | ❌ |
| `concurrency` | only `1` — see below | ❌ |
| `onCommand` | neither: it is the CLI dispatch seam, not a run-time hook | |

### `crewBuilder()`

| Method | Procedural | Declarative |
|---|---|---|
| `name` `goal` `verbose` | ✅ | ✅ |
| `withAgent(s)` | ✅ | ✅ |
| `withTask(s)` | ❌ ignored | ✅ **the whole point** |
| `process` | ❌ telemetry tag only | ✅ |
| `manager` | ❌ | ✅ |
| `memory` | ❌ | ✅ |
| `budget` | ✅ | ❌ ignored |
| `graph` | ✅ | ❌ |
| `onCrewStart` / `onCrewComplete` / `onCrewError` | ✅ | ❌ |

### `taskBuilder()`

Declarative only — the procedural engine never reads tasks. `name`, `description`, `agent`,
`expectedOutput`, `withContext(s)` (this is what builds the DAG), `expect`, `tools`,
`withTaskTool`, `humanInput`, `asyncExecution`, `deliverable`.

### `toolBuilder()`

Both shapes. `name`, `description`, `withSchema`, `execute`, `access`, `build`.

The schema is **not** read by the type system: `toolBuilder()` with no type argument gives
`execute` an `unknown` input, and every field access on it is an error. State the shape the
schema promises:

```ts
const wordCount = toolBuilder<{ text: string }, { words: number }>()
    .name("word_count")
    .withSchema({ type: "object", properties: { text: { type: "string" } }, required: ["text"] })
    .execute((input) => ({ words: input.text.trim().split(/\s+/).length }))
    .build();
```

## The globals

Three names the runner exchanges with the script, declared in `globals.d.ts`.

| Global | Direction | Meaning |
|---|---|---|
| `crew` | script → runner | The declarative handoff. Assigning it *is* what selects the declarative engine. |
| `inputs` | runner → script | What `--inputs` / `--inputs-file` parsed. Procedural shape only. |
| `result` | script → runner | What the run reports. Defaults to the last expression's value. Serialised to JSON, so assign a plain projection — a `CrewResult` holds host objects and does not survive the trip. |

## `ctx` — the agent context

Procedural shape only; nothing in the declarative path constructs one.

`ctx.state` carries the one legal mutator, `with()`. It **replaces** the state with what the
callback returns, under the agent's state mutex — so carry the fields you are not changing:

```ts
await ctx.state.with(prev => ({ ...prev, count: prev.count + 1 }));
```

Assigning directly (`ctx.state.count = 1`) throws `StateMutationOutsideWithException`. The
state is a JS `Proxy` whose `set` trap exists to make that loud rather than lost.

`ctx.signal` is a **.NET `CancellationToken` projected through interop**, not a DOM
`AbortSignal` — there is no DOM in Jint. It carries `IsCancellationRequested` and
`CanBeCanceled`, CLR-cased; `aborted`, `addEventListener` and `throwIfAborted` are all
`undefined`. Pass it along (`crew.run({ signal: ctx.signal })`) rather than polling it.

`ctx.llm` — `generate`, `chat`, `stream`, `embed`, `act`. `act` is the LLM ⇄ tool-call loop:
it runs the agent's own tool catalogue until the model stops asking or `maxIterations` is
reached. `ActOptions.system` seeds a real `role:"system"` message that persists across every
iteration; without it `act()` sends a single user message.

`ctx.memory`, `ctx.events`, `ctx.lock`, `ctx.spawn`, `ctx.delegate`, `ctx.send`/`receive`/
`broadcast`, `ctx.log`. See `context.d.ts`.

## `stateMachine` and `stateGraph`

Both are literal-driven and both were **declared wrongly until 2026-09-07**; if you are
working from an older `orkeon.d.ts`, this is the section to read.

`stateMachine` takes `{ name, initial, states }`. Transitions live **inside each state**,
keyed by event name, and the destination field is `target`:

```ts
const fsm = stateMachine({
    name: "order", initial: "pending",
    states: {
        pending:  { transitions: { approve: { target: "approved" } }, onEntry: (c) => {} },
        approved: { transitions: { ship: { target: "shipped" } } },
        shipped:  {},
    },
});
await fsm.send("approve");   // resolves to the NEW state name
```

`send()` returns the state it ended in — which is the *current* state, unchanged, when the
event is unknown to it or a guard refused. An unknown event is not an error. There is no
`run()` method and no `circuitBreaker` option.

`stateGraph` takes `{ name, nodes, edges, graphConfig? }`. `edges` is an **object keyed by
source node**, not a list of pairs, and `START`/`END` are plain strings (`"__START__"`):

```ts
const g = stateGraph<{ done: boolean }>({
    name: "research",
    nodes: { gather: (s) => ({ ...s, done: true }) },
    edges: { [START]: "gather", gather: END },
    graphConfig: { circuitBreakerPreset: "Strict" },
});
```

`TState` cannot be inferred from the node bodies — a node both takes and returns the state,
so inference is circular — and falls back to `Record<string, unknown>`. Annotate the call
(`stateGraph<OrderState>({...})`) to get a node that returns the wrong shape reported.

## Known gaps between the typings and the runtime

The declarations and the C# runtime are written in two languages and nothing tied them
together until [`scripts/check-scripting-typings.sh`](https://github.com/Orkeon/orkeon/blob/main/scripts/check-scripting-typings.sh)
did. What follows is what remains after that gate went green.

| Gap | Behaviour |
|---|---|
| `budget()` in the declarative shape | Silently ignored — the adapter never reads it. Use `process("autonomous")` in the procedural shape. |
| `.body()` in the declarative shape | Silently ignored. The most expensive confusion in the DSL, and the reason for the shape table above. |
| `globalThis.inputs` in the declarative shape | Never planted; `--inputs` has no effect on that path. |
| A plain `{ provider, model }` object passed to `llm()` | Dropped. `ExtractLlmConfig` returns `null` for anything that is not a `JsLlmConfig`, so build one with `llm.openai({...})`, `llm.default()`, etc. |
| `ctx.llm.embed` | Returns a stub vector. Real embedders are a follow-up. |
| `concurrency(n)` with `n > 1` | Rejected at build with a clear message — V1 is a mutex, the N-holder semaphore is V1.5. Loud, not silent. |
| Locks have no timeout | `LockTimeoutError` is V1.5. |
| Events | In-memory only; no Redis/NATS persistence. |
| FSM/Graph composition | Sub-states and sub-graphs are V1.5. |

### Conditional inclusion

There is no builder method for it. `when(predicate)` used to be declared on all three
builders and was honoured by neither engine — `JsCrewBuilder.when` and `JsTaskBuilder.when`
discarded the argument outright, and the predicate `JsAgentBuilder` stored was never read,
so `.when(() => false)` included the agent anyway. It was removed rather than implemented:
a method that silently does the opposite of what it says is worse than no method.

Guard the call instead:

```ts
const b = crewBuilder().name("nightly");
if (shouldAudit) b.withAgent(auditor);
```

## Editor setup

The typings ship as `orkeon.d.ts` next to the CLI build output and inside the `Orkeon`
package at `content/typings/orkeon.d.ts`. Point your editor at it and copy
[`tools/scripting-typecheck/tsconfig.base.json`](https://github.com/Orkeon/orkeon/blob/main/tools/scripting-typecheck/tsconfig.base.json),
which is the configuration the repository's own gate uses.

Three of its options are load-bearing:

- **`moduleDetection: "force"`** — without it, top-level `await crew.run()` is rejected
  (TS1375) and a top-level `const crew` collides with the `crew` global (TS2451). Two errors
  that have nothing to do with your code.
- **`target`/`lib` `ES2022`** — what Jint supports. Asking for more promises APIs that are
  not there; the DOM in particular is absent, which is why `ctx.signal` is not an
  `AbortSignal`.
- **`types: []`** — there is no Node in a `.ork.ts`. `process`, `require` and `Buffer` do not
  exist at run time and should not exist at authoring time either.

## Where to go next

- [Write a crew in TypeScript](../guides/write-a-crew-in-typescript.md) — the guide.
- [Scripting DSL — architecture](../architecture/scripting.md) — what the runtime is and where it sits.
- [TypeScript CLI commands](../architecture/cli-ts-commands.md) — the `.cmd.ts` control plane.
- [Driving crews from the REPL](../architecture/coding-agent-ts.md) — the `.cmd.ts` → `crew.ork.ts` bridge.
