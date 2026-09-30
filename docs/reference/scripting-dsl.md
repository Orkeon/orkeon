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
ends. `orkeon run` reads the source (`RunCommand.DeclaresCrewHandoffAsync`) looking for an
assignment to `globalThis.crew` — `(globalThis as any).crew = …` counts too — and routes on
what it finds.

| | **Procedural** | **Declarative** |
|---|---|---|
| the file ends with | `await crew.run()` | `globalThis.crew = crew` |
| engine | `ScriptHost.RunFromFileAsync` — the script's own `await crew.run()` | shared runner → `JsCrewConfigurationAdapter` → `ICrewOrchestrationService` |
| runs agent `.body()` | yes, one per agent, in declaration order | **no — ignored** |
| honours `withTask` / `process` / `manager` | **no — ignored** | yes |
| `orkeon run --validate` | fails (*did not assign globalThis.crew*) — **after running the script**: loading it evaluates it, `await crew.run()` included | works — the script is evaluated, the crew is not run |

They are opposites, not variants. The procedural run loop (JavaScript since SCR-25,
`JsCrew.Run.cs`, run under the script's own pump) walks the agents and never looks at tasks;
`Process` reaches only a telemetry tag. The adapter, symmetrically, never invokes a `body`:
the only places it names one are the warnings it emits for the declarations it drops.

**The rule:** write `.body()` and you are procedural; write `withTask` and you are
declarative. Never both in one file. A crew with tasks that ends with `await crew.run()`
runs its agents and ignores every task you wrote — logging a warning that says exactly that,
which is the only reason the mistake is now cheap to find.

It is `orkeon run` that routes a `globalThis.crew` script to the declarative engine. A host that runs the file through
`ScriptHost` — the REPL's `script-host` service, a C# host calling `RunFromFileAsync` — has no
orchestrator: it runs the exported crew **procedurally**, bodies and all, tasks ignored.

## What each shape reads

Measured against `JsCrewConfigurationAdapter.cs`, `JsCrew.cs` and `JsExecutionContext.cs`, not
inferred.

### `agentBuilder()`

| Method | Procedural | Declarative |
|---|---|---|
| `name` | ✅ | ✅ |
| `role` `goal` `backstory` | stored, not sent: `ctx.llm` sends your prompt alone; `role` only serves `crew.findByRole` | ✅ **the agent's prompt** — `build()` requires `role` |
| `llm` | ❌ `ctx.llm` uses the host's configured provider | ✅ an `llm.<provider>({...})` / `llm.default` value; a plain string or object is dropped |
| `tools([...])` built-ins by name | ✅ what `ctx.llm.act` may call — an unknown name is skipped silently | ✅ strict: an unknown name fails the run |
| `withAutonomousTool(s)` instances | callable from a body (`tool.execute(input)`); **not** offered to `ctx.llm.act` | ✅ registered and resolved by name |
| `maxIterations` `verbose` `allowDelegation` | ❌ (`act` has its own `maxIterations`) | ✅ |
| `withResponseFormat(type)` / `withResponseSchema(name, schema, strict?)` | ❌ | ✅ — runtime only, not in the typings |
| `body` | ✅ **the whole point** | ❌ never invoked |
| `withState` → `ctx.state` | ✅ | ❌ |
| `onError` | ✅ | ❌ |
| `onAgentStart` / `onAgentStop` | fire when the agent **joins** a crew (`crewBuilder().build()`, `crew.add`, `ctx.spawn`) and **leaves** it (`crew.remove`) — not at run start and end | the same: `onAgentStart` fires at `build()`, during evaluation, although the run warns that both are ignored |
| `concurrency` | only `1` — see below | ❌ |
| `onCommand` | neither: it is the CLI dispatch seam, not a run-time hook | |

### `crewBuilder()`

| Method | Procedural | Declarative |
|---|---|---|
| `name` | ✅ | ✅ |
| `goal` `verbose` | ❌ stored, never read | ✅ |
| `withAgent(s)` — a built agent | ✅ | ✅ |
| `withTask(s)` — a built task | ❌ ignored | ✅ **the whole point** |
| `process` | ❌ telemetry tag only | ✅ (`"graph"` selects the domain's retry-and-route strategy, not a script-drawn topology — see below) |
| `manager` | ❌ | ✅ |
| `memory` | ❌ | ✅ |
| `budget` | ✅ | ❌ ignored |
| `onCrewStart` / `onCrewComplete` / `onCrewError` | ✅ | ❌ |

Everything marked ❌ on the declarative side is now **announced**: the run logs one warning
per dropped declaration, naming the method and the agent it was written on. It is still
dropped — the two shapes are two engines — but a crew no longer runs a body that was never
invoked and says nothing about it.

#### `process("graph")` is not `stateGraph`

They are different facilities with a shared word. `process("graph")` runs the crew on the
**domain's** graph strategy: a fixed `agent_execute → route_decision` loop with a circuit
breaker, tuned by `GraphConfig`. `stateGraph({ nodes, edges })` is the topology **you**
draw, and it runs when you call `.run()` on it — from an agent `.body()`, so procedurally.

`crewBuilder().graph(g)` used to blur the two: it accepted a `stateGraph`, stored it in a
field no engine ever read, and `process("graph")` refused to build without it. The mode was
gated behind a method that discarded its argument. The method is gone; `process("graph")`
now builds on its own.

### `taskBuilder()`

Declarative only — the procedural engine never reads tasks. Carried to the crew:
`description`, `agent` (a built agent — the string form the typings allow is not accepted),
`expectedOutput`, `withContext(s)` (this is what builds the DAG), `tools`, `humanInput`,
`asyncExecution`, `deliverable`, and the runtime-only `withResponseFormat(type)` /
`withResponseSchema(name, schema, strict?)`. Accepted but not acted on: `name` (a task
configuration has no name), `expect` (recorded in the task context when there is no
`deliverable`, never validated) and `withTaskTool` (**dropped without a warning**).

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
| `inputs` | runner → script | What `--inputs` / `--inputs-file` parsed — or the `input` of the REPL's `script-host.runCrew(name, input)`. Procedural shape only. |
| `result` | script → runner | What a procedural run reports when the script's completion value is `undefined` — which is always the case once the file has a top-level `await`, because it then runs wrapped in an async function. Assign it as `globalThis.result = …`: a top-level `const result` is local to that wrapper. Serialised to JSON, so assign a plain projection — a `CrewResult` holds host objects and does not survive the trip. |

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

`ctx.llm` — `complete(prompt)`, `chat(messages)`, `stream(prompt)` (async-iterable; `usage`
and `reasoningChunks` are read after the loop), `extract(prompt, schema)`,
`decide(prompt, choices)`, `embed(text)`, `act(prompt, opts)`, and `interrupt()` /
`isInterrupted`. Every call goes to the host's configured provider; without one, a
`<undefined-llm:…>` echo answers, which is what lets the examples run keyless. Of the
per-call options, the runtime reads `responseFormat` (`"json_object"`, `"text"`…) and
`llm: { model }` (a per-call model override); the declared `provider`, `model`, `temperature`,
`maxTokens` and `signal` are not read.

`act` is the LLM ⇄ tool-call loop: it offers the model the built-ins the agent selected with
`.tools([...])` — not its `withAutonomousTool` instances — until the model stops asking or
`maxIterations` (default 10, `0` = unlimited) is reached. `ActOptions.system` seeds a real
`role:"system"` message that persists across every iteration; without it `act()` sends a
single user message. `permissionMode` (`default`, `acceptEdits`, `bypassPermissions`, `plan`)
is checked against the host's permission gate on each tool call, when the host registered one;
`onDelta` receives the streamed text of each assistant turn.

`ctx.memory` (`crew`, and `agent` in an agent body), `ctx.events`, `ctx.lock(name, fn)`,
`ctx.spawn`, `ctx.delegate`, `ctx.send`/`receive`/`broadcast`, `ctx.log`. See `context.d.ts`.

## The namespaces

| Global | What it holds |
|---|---|
| `llm` | Provider configurations for `agentBuilder().llm(...)` (declarative shape): `llm.openai`, `anthropic`, `ollama`, `azureOpenai`, `grok`, `minimax`, `openrouter`, `mammouth` — each `(opts?) => LlmConfig`, with `with(overrides)` — and `llm.default` (typed `llm.default_`) — a value, not a function: the provider named by `Orkeon:DefaultLlmProvider`, else the host's configured one, else the `<undefined-llm>` echo. |
| `tools` | The host's built-in tools, called from a body: `tools.fileRead({ path })`, the snake_case name camelCased. `tools.d.ts` declares `fileRead`, `fileWrite`, `directoryRead`, `webScrape`, `httpApi`, `searchTool`, `databaseQuery`, `delegateWork`, `askQuestion` and the thirteen `email*` tools; any other registered tool is reachable the same way. |
| `rag` | `rag.ingest({ collection, sources, chunkingStrategy?, reindex? })`, `rag.query(question, { collection, profile?, topN? })` (a grounded answer with citations) and `rag.retrieve(...)` (the same passages, no generation). Needs a host that registered the RAG subsystem (`AddOrkeonRag`). |
| `ErrorAction` | The factories an `onError` handler returns: `fail()`, `skip()`, `fallback(value)`, `retry({ delay?, max? })`. Anything else is treated as `fail()`. |
| `stateMachine`, `stateGraph`, `START`, `END` | Below. |

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

`send(event, payload?)` returns the state it ended in — which is the *current* state,
unchanged, when the event is unknown to it or a guard refused. An unknown event is not an
error. A transition may carry a `guard` (return `false` to veto it); a state may carry
`onEntry` and `onExit`, each handed `{ state, payload }`. The machine exposes `name` and
`current`. There is no `run()` method and no `circuitBreaker` option.

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

An edge is a node name, `END`, or a function of the state returning one (a conditional edge).
`graphConfig` bounds each `run(state)`: `circuitBreakerPreset` (`Strict`, `Default`,
`Permissive`) or the individual `maxTransitions`, `maxStateVisits`, `maxRetryCycles` and
`maxTotalDurationSeconds` — exceeding a count throws, running out of time cancels the run. `runStream(state)` yields each hop
(`{ fromNode, toNode, state }`) for `for await`.

`TState` cannot be inferred from the node bodies — a node both takes and returns the state,
so inference is circular — and falls back to `Record<string, unknown>`. Annotate the call
(`stateGraph<OrderState>({...})`) to get a node that returns the wrong shape reported.

## Known gaps between the typings and the runtime

The declarations and the C# runtime are written in two languages and nothing tied them
together until [`scripts/check-scripting-typings.sh`](https://github.com/Orkeon/orkeon/blob/main/scripts/check-scripting-typings.sh)
did. What follows is what remains after that gate went green.

| Gap | Behaviour |
|---|---|
| `budget()` in the declarative shape | Ignored, and said so: the run logs a warning naming the method. The budget bounds the procedural shape only (keys `toolCalls`, `tokens`, `delegationDepth`, `spawnedAgents`, `wallTime`). |
| `.body()` in the declarative shape | Ignored, and said so: the run logs a warning naming the agent. The most expensive confusion in the DSL, and the reason for the shape table above. |
| `globalThis.inputs` in the declarative shape | Never planted. Passing `--inputs`, `--inputs-file` or `--memory-limit-mb` to a declarative script now prints a warning on stderr instead of dropping the flag in silence. |
| A plain `{ provider, model }` object or a string passed to `llm()` | Dropped. `ExtractLlmConfig` returns `null` for anything that is not a `JsLlmConfig`, so build one with `llm.openai({...})`, `llm.default`, etc. — `llm.default` is a value; `llm.default()` throws. |
| `withTaskTool` | Accepted and dropped by both shapes, without a warning. |
| The classes of `errors.d.ts` | Declared, not registered: `err instanceof ReceiveTimeoutError` type-checks and throws a `ReferenceError`. A host failure reaches the script as a plain `Error` carrying the CLR exception on `clr` and its type name on `clrType`. |
| `ErrorContext` (the `onError` argument) | The runtime passes `{ code, message, exception, attempt, agent: { id, name } }` (and the context as a second argument); the declared `error` and `agentName` are `undefined`. `code` is one of `rate_limit`, `network`, `timeout`, `receive_timeout`, `state_mutation`, `agent_not_in_crew`, `validation`, `unknown` — never the declared `auth`, `budget` or `tool`. |
| `ctx.delegate(agent, input)`, `ctx.send(agent, msg)`, `crew.remove(agent)`, `crew.has(agent)` | Take the agent object; the agent-name string the typings allow is refused. `delegate` runs the target's body with no `ctx`. |
| `withAgent(b => …)`, `withTask(b => …)` | The builder-callback forms are not supported: pass a built agent or task. |
| `Agent.role` | `undefined` at run time: an agent exposes `name` and `id`. |
| `crew.runStream()` | Yields `agent.start` and `agent.stop` only, of the seven declared event types; `CrewRunOptions.inputs` is not read. |
| `ChatResponse.toolCalls` | Never set: `chat()` resolves to `{ content, tokensUsed, model }`. |
| Events | `PublishedEvent.publisher` is `undefined`; `EventTopicOptions.maxHandlers` is not read (the subscriber count is used). |
| `ctx.log.info(message, ...args)` | The extra arguments are dropped. |
| Runtime-only members | Not declared, but there: `crew.findById(id)`, `crew.agents`, `ctx.crew` (`name`, `findByName`, `findById`, `findByRole`, `has`, `lock(name, fn)`), `ctx.receive({ timeout })`. |
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

The typings are `src/scripting/Orkeon.Scripting/Typings/*.d.ts`; building `Orkeon.Scripting`
concatenates them into `bin/<configuration>/net10.0/dist/orkeon.d.ts` under that project — no
package or release archive carries the file yet. Point your editor at either and copy
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
