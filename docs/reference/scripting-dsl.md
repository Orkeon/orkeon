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
| honours `withTask` / `process` / `manager` | **no — ignored** | yes — `manager` where the process has one (`hierarchical`, `consensual`), refused elsewhere |
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
| `llm` | the provider only: an agent configured with `llm.profile("…")` has `ctx.llm` talk to that profile's provider, on the profile's model; otherwise `ctx.llm` uses the host's provider on its configured model (per call: `{ llm: { model } }`) | ✅ an `LlmConfig` — `llm.default_`, `llm.model("…")`, `llm.profile("…")`, `.with({...})` — sets the agent's provider (one of the host's profiles), model, temperature and token cap. A string or a plain object is refused by `.llm(...)` |
| `tools([...])` built-ins by name | ✅ what `ctx.llm.act` may call — a name the host does not offer rejects `act` with an `UnknownToolError`, before any model call | ✅ strict: an unknown name fails the run |
| `withAutonomousTool(s)` instances | callable from a body (`tool.execute(input)`) and offered to `ctx.llm.act`, which runs the tool's `execute` in the script | ✅ registered and resolved by name |
| `effort(level)` — `"low"`, `"medium"`, `"high"`, `"xhigh"` or `"max"`: turns the provider's thinking on at that effort | ✅ sent on the agent's `ctx.llm` calls, unless a call sets its own `{ effort }` | ✅ |
| `cache({ systemPrompt, tools, ttl })` — YAML `cache:`: asks the provider's prompt cache to keep the system prompt and/or the tool definitions (`ttl` `"5m"` or `"1h"`; `cache({})` turns caching off, the host default `Llm:Cache` included) | ✅ sent on the agent's `ctx.llm` calls, unless a call sets its own `{ cache }` | ✅ |
| `maxIterations` `verbose` `allowDelegation` | ❌ (`act` has its own `maxIterations`) | ✅ — `maxIterations` defaults to 20, as in YAML and C# |
| `maxRpm` — YAML `maxRpm:`: at most N model requests per minute for this agent, one more waiting its turn (none by default; 0 or less refused at `build()`) | ❌ (warned: `ctx.llm` calls are no agent turns — the host's `RateLimiting` still caps them) | ✅ |
| `maxExecutionTime(seconds)` — YAML `maxExecutionTime:`: the seconds a task may take on this agent (none by default; 0, negative, decimal or above 4294967 refused at `build()`) | ❌ (warned: the procedural engine runs no task) | ✅ |
| `workingRoot(path)` — YAML `working_root:`: the virtual directory its file and shell tools stay inside ([agent working root](../architecture/vfs-compliance.md#agent-working-root-vs-mount); none by default; an empty path refused at `build()`, a root under no mount, relative or holding `..` refused when the agent's run starts) | ✅ every tool the body runs — through `ctx.llm.act`, `ctx.tools` or `tools` — stays inside it; a child started by `spawn_agent` or `ctx.spawnBackground` without a root of its own runs under it, and so does each later `crew.send` to it | ✅ |
| `withResponseFormat(type)` / `withResponseSchema(name, schema, strict?)` | ❌ (per call: `{ responseFormat }`) | ✅ |
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
| `goal` | ❌ stored, never read | ✅ |
| `verbose` | ✅ | ✅ |
| `withAgent(s)` — a built agent; anything else is refused | ✅ | ✅ |
| `withTask(s)` — a built task; anything else is refused | ❌ ignored | ✅ **the whole point** |
| `process` | ❌ telemetry tag only | ✅ (`"graph"` selects the domain's retry-and-route strategy, not a script-drawn topology — see below) |
| `manager` | ❌ | ✅ — `process("hierarchical")`: the manager assigns and reviews on that agent's `.llm(...)` (`llm.profile("claude")` puts it on that profile); `process("consensual")`: the arbiter of the `ManagerDecision` fallback; the four other processes refuse it when the run adapts the crew, as does an agent of another crew |
| `memory` | ❌ | ✅ |
| `planning` — YAML `planning: true`: a step-by-step plan per task, read in the task's prompt (off by default; on the host's default profile) | ❌ (warned) | ✅ |
| `maxRpm` — YAML `maxRpm:`: at most N model requests per minute for the crew, all its agents and its manager together (none by default; 0 or less refused at `build()`) | ❌ (warned) | ✅ |
| `budget` | ✅ | ✅ with `process("autonomous")` — the crew's budget, as YAML `budget:`; refused with another process, as is a limit that is not 1 or more, when the run adapts the crew |
| `concurrency(n)` — the most children (`ctx.spawnBackground`, `spawn_agent`) the crew runs at once (default 4; `n` must be 1 or more) | ✅ | ❌ (not warned) |
| `maxInstances(n)` — the most agent instances (`crew.instances()`) the crew keeps (default 32; the oldest ended one goes first) | ✅ | ❌ (not warned) |
| `onCrewStart` / `onCrewComplete` / `onCrewError` | ✅ | ❌ |

Everything marked ❌ on the declarative side, `concurrency` and `maxInstances` aside, is now **announced**: the run logs one warning
per dropped declaration, naming the method and the agent it was written on. It is still
dropped — the two shapes are two engines — but a crew no longer runs a body that was never
invoked and says nothing about it.

**What `await crew.run()` resolves to** — a `CrewResult`, exactly what `crew.d.ts` declares:
`output`, the text of the last agent output that was not `null` (`""` when no agent returned
anything), and `tasks`, one `{ name, output, durationMs }` per agent run, in the order they ran,
`output` being what the body returned. Nothing more: the `artifacts` map the typings used to
declare was never filled, and `crew.run<T>()` typed an `output` the runtime always serves as a
string — both are gone (GAP-27), and `TypingsRuntimeParityTests` compares the two result shapes
type by type.

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
`description`, `agent` (a built agent), `expectedOutput`, `withContext(s)` (this is what builds
the DAG), `tools` (added to the agent's own tools for that task only, never replacing them), `humanInput`, `asyncExecution`, `deliverable`, `withResponseFormat(type)`,
`withResponseSchema(name, schema, strict?)` and `withProfile(name)` (YAML `llm_override: { profile }`:
this task alone runs on one of the host's profiles, on that profile's own model, and `"default"`
brings it back to the host's default; a name the host does not offer fails the load, listing the
known ones). `name` is the task's key: `when(...)` and a decision's `state` refer to a task by it, and a
decision task requires it. Accepted but not acted on: `expect` (recorded in the
task context when there is no `deliverable`, never validated). `withTaskTool` is gone: nothing ever
read it, and `tools` covers it.

`decision(spec)` makes the task a decision task: `spec` is an object literal `{ questions, minConfidence, state, longContext, fallback }`, the shape of the YAML `decision:` block. It needs no `agent`, and a task that has both is refused at load. `when(decisionTask, clauses)` runs the task only when the decision task named answered as asked: `clauses` is a clause object (`{ category: ["bug"], urgency: ">= 0.7", "category.bug": ">= 0.25" }`) or the string `"otherwise"`; `when()` throws an `InvalidScriptException` only for an empty task name or for `clauses` that is neither an object nor a string. A string other than `"otherwise"`, an unknown question or option, or a malformed clause passes the builder and is refused at load. Two decision tasks with the same `name` in one script are refused at load (`Task '<name>' declares a decision twice.`). `state` lists the tasks whose output is the decision's state (default: each dependency that ran); with the circuit off, or by `--no-decision`, the task answers from `fallback` (see [Decision models](../architecture/decision-models.md#the-decision-circuit-and-the-fallback)). Decision tasks and conditions run in the Sequential, Parallel and Graph processes only. A second `.when()` on one task throws an `InvalidScriptException` naming the task. A `state` that is not an array, a `fallback` that is not an object, and a `choice` value, `score` entry or `fallback` value that is not a string are refused at load, naming the task and the key. Two tasks with the same `name` are refused at load. A task without `.name()` is keyed by its full description; truncation is display only. See [Decision models](../architecture/decision-models.md).

`asyncExecution()` follows the YAML rule: under `.process("sequential")` the task runs alongside
the tasks after it, and a task that lists it in `withContext` waits for it; under
`.process("parallel")` it has no effect of its own; under the four other modes the crew is refused
when the run adapts it, naming the task (see
[Asynchronous tasks](../orchestration/process-types.md#asynchronous-tasks-asyncexecution)).

A task's `agent` and the tasks of its `withContext(s)` are the crew's own (`withAgent(s)`,
`withTask(s)`): one the crew does not hold is refused when the run adapts the crew, naming the
task, the call and what the crew has — the task used to run on another agent, or without waiting for
what it cited.

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
`<undefined-llm:…>` echo answers, which is what lets the examples run keyless. A call takes three
options, all patching the host provider's configuration for that call only:
`responseFormat` (`"json_object"`, `"json_schema"`, `"text"`), `llm: { model }` (a per-call
model) and `effort` (`"low"`, `"medium"`, `"high"`, `"xhigh"` or `"max"`: the provider's thinking, on at that effort;
any other value is refused with `effort must be low|medium|high|xhigh|max`) and `cache` (the prompt-cache request, as `agentBuilder().cache(...)` takes it). `act` takes more: see
[Options of `act`](../architecture/scripting.md#options-of-act) (`tools`, `effort`, `onToolCall`,
`onToolResult`, `waitFor`). There is no per-call provider, temperature or token cap, and no `signal` — the call
already observes the context's.

`act` is the LLM ⇄ tool-call loop: it offers the model the built-ins the agent selected with
`.tools([...])` and its `withAutonomousTool` instances — whose `execute` runs in the script,
on the engine's own thread — until the model stops asking or `maxIterations` (default 10,
`0` = unlimited) is reached. It resolves to `{ output, iterations }` (plus `interrupted` or
`exhausted` when the loop stopped early). Every tool call of a turn runs, in order, and the
turn is replayed in the provider's own tool protocol — the assistant turn with its calls, then
one `tool` result per call, a permission refusal or a tool error included — whether the
provider answers in the OpenAI shape (most providers, Ollama included) or with the Anthropic
Messages body. A result is cut once, at the framework's bound
(`AgentDefaults.ResolveMaxToolResultLength`: 4 000 characters by default; per-tool overrides: `file_read` 32 000, `node_read` 256 000, `shell_command` 10 000): by the
host's tool invocation pipeline when it has one, by `act` otherwise. A turn the provider failed
resolves `output` to `<undefined-llm:<the provider's error>>`, as `complete` does. A name in `.tools([...])` that no tool of the host
answers to (compared case-insensitively) is not skipped: `act` rejects with an
`UnknownToolError` (`agentName`, `toolNames`, `availableTools`) before any model call — the
procedural twin of the load failure the declarative shape gets for the same name; an `onError`
handler reads it as `validation`. `ActOptions.system` seeds a real `role:"system"` message that
persists across every iteration; without it `act()` sends a single user message.
`permissionMode` (`default`, `acceptEdits`, `bypassPermissions`, `plan`, `dontAsk`) is checked against the
host's permission gate on each tool call, when the host registered one: a call the mode asks
about waits for the operator when the host has an approval channel (`orkeon-repl` has one), and
is refused otherwise; the time the gate takes to decide is not counted in `ExecutionTimeout`, nor
in the agent's wall-time budget (`.budget({ wallTime })`). `ctx.llm.permissionGate.enabled` says
whether the host registered a gate; without one, a `permissionMode` checks nothing and the call
logs one warning saying so — declare a script
tool's `.access("read")` for it to pass `plan`; `onDelta` receives the streamed text of each
assistant turn.

`ctx.delegate(agentOrName, input)` is `crew.runAgent`: the target runs with its own context,
under its semaphore and its `onError` policy. Neither gives the run a budget unless asked:
`ctx.delegate(agent, input, { budget: { toolCalls: 20, tokens: 50000, wallTime: 300 } })` (and the
same `budget` in `crew.runAgent`'s options) bounds that run with the keys and values of
`crewBuilder().budget(...)` (`toolCalls`, `tokens`, `contextTokens`, `wallTime` in seconds or as `"5m"`,
`delegationDepth`, `spawnedAgents`). `tokens` is a running sum of the prompt and completion of every
call, the context counted again on each. `contextTokens` counts the context once: each call's
completion plus the uncached part of its prompt when the provider reports cache hits, else what the
prompt grew by since the same conversation's previous call (each `ctx.llm.act` its own), so a turn of n calls on a context of c tokens spends about c plus
its completions, not n × c; set alone it leaves `tokens` uncapped, and a declarative autonomous crew
refuses it. Once either is reached, `ctx.llm.act` makes one last call that may
call no tool and resolves with `budgetExhausted: true` ([the token budget of act](../architecture/scripting.md#the-token-budget-of-act)).
A spent dimension ends the run with `BudgetExhaustedError`,
the error a crew's budget gives, and a child it spawns (`spawn_agent`, `ctx.spawnBackground`) derives
its budget from the run's. An unknown key, a count that is not a whole number of 1 or more, or an
unreadable `wallTime` is refused when the run opens, where the procedural `crewBuilder().budget(...)` skips it.
`charge: "crew"`, beside `budget`, counts what the run spends (tool calls, tokens, spawns; its wall
clock runs with the caller's) against the budget of the run it is called from as well — the crew's,
from an agent body of `crew.run` — so runs started at once never spend more than the crew allows.
A call the run's own budget refuses is not charged, and the children it spawns are charged too,
each allowed no more than what both budgets have left. Without `budget` the run spends straight
from the caller's, and it is refused when the caller runs under no budget. `ctx.delegate` charges
its own context's budget. `crew.runAgent` cannot name the body it is called from: it charges the
one budget every open agent body runs under, and is refused when bodies under different budgets
are open (a fan-out beside a budgeted delegated run); call `ctx.delegate` from the body instead.
`crew.run` and `crew.runStream` ignore both keys. `ctx.send(agentOrName, message)`, `receive({
timeout })` and `broadcast(message)` exchange messages; `send` and `broadcast` are synchronous.
`ctx.crew` is a read-only view of the crew (`name`, `findByName`, `findById`, `findByRole`,
`has`, `lock(name, fn)`). `ctx.log.info(message, ...args)` joins the extra arguments with a
space, objects as JSON. Also `ctx.memory` (`crew`, and `agent` in an agent body),
`ctx.events`, `ctx.lock(name, fn)`, `ctx.spawn`. `ctx.spawnBackground(agent, input)` starts a crew agent as a child and returns its ticket; `ctx.instanceId` is the id of the agent instance a body runs as; `crew.instances()`, `crew.send(id, message)` and `crew.stop(id)` list, message and stop the kept instances ([Agent instances](../architecture/scripting.md#agent-instances-crewinstances-crewsend-crewstop)). See `context.d.ts`.

A host failure reaches the script as an instance of the class `errors.d.ts` declares —
`try { await q.pop({ timeout: 100 }) } catch (e) { if (e instanceof ReceiveTimeoutError) … }`
— with that class's fields (`agentName`, `timeoutMs`…), plus `clrType` (the .NET type name) and
`clr` (the exception). A failure with no declared class is a plain `Error` with the same two
properties. An `onError` handler receives `{ code, message, exception, attempt, agent: { id,
name } }` and the context; `code` is one of `rate_limit`, `network`, `timeout`,
`receive_timeout`, `state_mutation`, `agent_not_in_crew`, `validation`, `unknown`.

## The namespaces

| Global | What it holds |
|---|---|
| `llm` | The model settings `agentBuilder().llm(...)` takes (declarative shape). `llm.default_` — a value, not a function — is the host's provider on its configured model (an empty `model` when the host configures none: the agent then runs on that provider's own default, never on OpenAI's), or the `<undefined-llm>` echo when the host has none; `llm.model(name, overrides?)` is the same provider on another model; `llm.profile(name, overrides?)` is one of the host's named profiles (`Llm:Profiles:<name>`) on its own model — an agent configured with it runs its turns and its `ctx.llm` calls on that provider, and a name the host does not offer throws, listing the known ones (`"default"` is `llm.default_`). `with({ model, temperature, maxTokens, responseFormat, effort, cache })` returns a copy, keeps the profile, and refuses any other key. There is no per-vendor factory: a script picks among the providers the host configured, by profile name. |
| `tools` | The host's built-in tools, called from a body: `tools.fileRead({ path })`, the snake_case name camelCased. `tools.generated.d.ts` declares the input and the result of every built-in tool a run registers, written from its request and response records (the tool's own snake_case keys: `tools.fileRead({ path, max_length })`), so a misspelled parameter fails the type check; `tools.d.ts` declares the e-mail and drive families by hand. A plugin or MCP tool is reachable the same way, untyped. |
| `rag` | `rag.ingest({ collection, sources, chunkingStrategy?, reindex?, prune? })`, `rag.query(question, { collection, profile?, topN? })` (a grounded answer with citations) and `rag.retrieve(...)` (the same passages, no generation). Needs a host that registered the RAG subsystem (`AddOrkeonRag`); bound in `orkeon run`, the REPL and the daemon host alike. |
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

## Design limits

The declarations and the C# runtime are written in two languages. Two gates tie them together:
[`scripts/check-scripting-typings.sh`](https://github.com/Orkeon/orkeon/blob/main/scripts/check-scripting-typings.sh)
typechecks every `.ork.ts` under `examples/` against the typings, and a parity test
(`TypingsRuntimeParityTests`) compares, member by member, each declared interface with the
runtime type behind it. No gap between the two remains; what follows are limits of the design,
each of them stated rather than silent.

| Limit | Behaviour |
|---|---|
| `budget()` in the declarative shape | The crew's budget when the process is `"autonomous"`, the one process that spends it (keys `toolCalls`, `tokens`, `delegationDepth`, `spawnedAgents`, `wallTime`; `contextTokens` is refused there, the strategy counting each finished task's tokens, not each call's prompt). With another process the crew is refused when the run adapts it, the error naming the process. |
| `.body()` in the declarative shape | Ignored, and said so: the run logs a warning naming the agent. The most expensive confusion in the DSL, and the reason for the shape table above. |
| `globalThis.inputs` in the declarative shape | Never planted. Passing `--inputs`, `--inputs-file` or `--memory-limit-mb` to a declarative script prints a warning on stderr instead of dropping the flag in silence. |
| `ctx.llm.embed` | Returns a stub vector. Real embedders are a follow-up. |
| `concurrency(n)` with `n > 1` | Rejected at build with a clear message — V1 is a mutex, the N-holder semaphore is V1.5. Loud, not silent. |
| Locks have no timeout | `LockTimeoutError` is V1.5. |
| Events | In-memory only; no Redis/NATS persistence. |
| FSM/Graph composition | Sub-states and sub-graphs are V1.5. |

### Conditional inclusion

The only conditional construct is `when(decisionTask, clauses)` on a task builder, which runs the
task on a decision task's answers at runtime (see `decision(spec)` above). There is no predicate
that includes or excludes an agent or a task when the script is built: `when(predicate)` used to be
declared on all three builders and was honoured by neither engine, so it was removed rather than
implemented, a method that silently does the opposite of what it says being worse than no method.

Guard the call instead:

```ts
const b = crewBuilder().name("nightly");
if (shouldAudit) b.withAgent(auditor);
```

## Editor setup

Run `orkeon typings` in your project: it writes `orkeon.d.ts` (this DSL) and `orkeon-cli.d.ts`
(the REPL's `*.cmd.ts` commands) into `./.orkeon/` — `--out <dir>` picks another folder — and
overwrites them, so run it again after updating the tool. Each script then opens with
`/// <reference path="./.orkeon/orkeon.d.ts" />` (the path relative to the script). The files
come from the tool itself, so they describe the runtime that will execute your scripts. In a
clone, the sources are `src/scripting/Orkeon.Scripting/Typings/*.d.ts`, concatenated by the
build into `bin/<configuration>/net10.0/dist/orkeon.d.ts` under that project.

To **pin** the typings of a release, or to **diff** two of them, take them from the Release
instead of from whichever tool is installed: each GitHub Release attaches
`orkeon-typings-<version>.tar.gz`, the same two files `orkeon typings` writes, taken from that
release's own build. It is listed in the release's `SHA256SUMS` and attested like the other
archives (`gh attestation verify`, see [Verify what you install](../guides/verify-what-you-install.md)):

```bash
VER=<version>    # the tag without its v
curl -fsSL -O "https://github.com/Orkeon/orkeon/releases/download/v$VER/orkeon-typings-$VER.tar.gz"
gh attestation verify "orkeon-typings-$VER.tar.gz" --repo Orkeon/orkeon
mkdir -p .orkeon && tar -xzf "orkeon-typings-$VER.tar.gz" --strip-components=1 -C .orkeon
# between two versions, extract each into its own folder and:  diff -r typings-A typings-B
```

A Release cut before this asset existed does not carry it; for those, `orkeon typings` is the
source. Then copy
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
