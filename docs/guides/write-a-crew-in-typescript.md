> 🇫🇷 [Version française](../fr/guides/write-a-crew-in-typescript.md)

# Write a crew in TypeScript

Orkeon crews are usually shown in YAML. They can also be **written in TypeScript**, in a file
called `something.ork.ts`, and that surface is larger than the YAML one: agents can carry real
code, tools can be written inline, and a body can drive its own LLM ⇄ tool loop.

This guide takes you from one agent to a three-agent crew with a dependency graph and a file
on the way out. Every command below runs from a clone with **no API key** — the runtime falls
back to an echo provider when none is configured.

If you want signatures rather than narrative, go to the
[Scripting DSL reference](../reference/scripting-dsl.md).

## Thirty seconds

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- \
    run examples/scripting/01-hello-world.ork.ts
```

That file is fourteen lines:

```ts
const greeter = agentBuilder()
    .name("Greeter").role("Greeter").goal("Greet the user")
    .body((input, ctx) => "Hello from Orkeon Scripting!")
    .build();

const crew = crewBuilder().name("greeting-crew").withAgent(greeter).build();

await crew.run();
```

Three things are already true here and worth naming. `agentBuilder()`, `crewBuilder()` and
friends are **globals** — there is nothing to import. `role` and `goal` are not labels, they
are the prompt an LLM-backed agent receives. And the last line, `await crew.run()`, is not a
detail of style: it selects the engine.

## The thing to understand first: there are two shapes

A `.ork.ts` file is handed to one of **two different engines**, and the file chooses which by
how it ends. The runner reads your source looking for an assignment to `globalThis.crew`.

| | **Procedural** | **Declarative** |
|---|---|---|
| the file ends with | `await crew.run()` | `globalThis.crew = crew` |
| runs agent `.body()` | yes, one per agent | **no** |
| honours `withTask`, `process`, `manager` | **no** | yes |
| `--validate` without running | fails | works |

These are opposites, not two spellings of the same thing. The procedural engine iterates the
agents and never looks at tasks. The declarative adapter never invokes a `.body()`.

**The rule:** if you write `.body()`, you are procedural. If you write `withTask`, you are
declarative. Never both in one file.

The failure this prevents is silent. A crew with three carefully written tasks that ends with
`await crew.run()` will run, print a result, and **ignore every task** — no warning, no error.
That is the single most expensive mistake in this DSL, which is why it is the first thing on
this page rather than a footnote.

## Shape A — the declarative crew

This is the shape for "several agents, several steps, one of them depends on another". The
worked example is
[`examples/scripting/crew-review-desk/`](https://github.com/Orkeon/orkeon/blob/main/examples/scripting/crew-review-desk/README.md):
three agents review a diff and leave a markdown report.

### Agents

```ts
const scanner = agentBuilder()
    .name("scanner")
    .role("Change scanner")
    .goal("Establish what a change touches, factually and without judgement")
    .backstory(`Reads diffs for a living. Reports scope and refuses to speculate about
intent. Everything downstream depends on this being boring and correct.`)
    .tools(["file_read", "directory_read"])
    .withAutonomousTools(pickTools("diff_stats", "touched_files"))
    .maxIterations(6)
    .build();
```

`role`, `goal` and `backstory` **are the prompt**. Vague ones produce vague agents.

### Tools, three surfaces

An agent reaches tools three ways, and they are not interchangeable:

1. **Built-ins by name** — `.tools(["file_read", "directory_read"])`. Resolved from the host
   catalogue, and the resolution is *strict*: a name that is not registered fails the run
   rather than quietly leaving the agent one tool short.
2. **TypeScript tools as instances** — `.withAutonomousTools([...])`, built with
   `toolBuilder()`. They travel with the script, so they need no host registration.
3. **Imperatively, from a body** — `tools.fileRead({ path })`, camelCased, procedural shape
   only.

Writing a tool takes a schema and a handler. Note the type argument: TypeScript does not read
your JSON schema, so state the shape it promises or `input` is `unknown`.

```ts
const diffStats = toolBuilder<{ diff: string }, { added: number; removed: number }>()
    .name("diff_stats")
    .description("Counts added and removed lines in a unified diff")
    .withSchema({
        type: "object",
        properties: { diff: { type: "string", description: "Unified diff text" } },
        required: ["diff"],
    })
    .execute((input) => {
        const lines = input.diff.split("\n");
        const added = lines.filter((l) => l.startsWith("+") && !l.startsWith("+++")).length;
        const removed = lines.filter((l) => l.startsWith("-") && !l.startsWith("---")).length;
        return { added, removed };
    })
    .build();
```

### Tasks, and the DAG

Tasks carry the work. `withContext` is what builds the dependency graph: a task that declares
another as context runs *after* it and receives its output.

```ts
const review = taskBuilder()
    .name("review")
    .agent(reviewer)
    .description("Using the scope report, review the same diff. Judge each flag.")
    .expectedOutput("A list of findings, each with a verdict and a one-line justification")
    .withContext(scan)          // <- runs after `scan`, and is handed its output
    .build();
```

`expectedOutput` is not decoration either — it is the contract shown to the model.

### Deliverables

A task can write its output to a file:

```ts
.deliverable({ path: "/output/review.md", source: "final_message", format: "markdown" })
```

`/output` is a virtual mount. Without it the run still succeeds and the file simply has
nowhere to land.

### The handoff

```ts
const crew = crewBuilder()
    .name("review-desk")
    .process("sequential")
    .withAgents([scanner, reviewer, reporter])
    .withTasks([scan, review, report])
    .build();

globalThis.crew = crew;      // <- NOT `await crew.run()`
```

Now the definition can be checked without running anything — no key, no model, about two
seconds:

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- \
    run examples/scripting/crew-review-desk/main.ork.ts --validate
```

```
VALIDATION OK: …/crew-review-desk/main.ork.ts (agents=3, tasks=3, tools resolved=5)
```

That check is only available on this shape, and it is a good reason to prefer it: it catches
an unregistered tool name or a task pointing at an undeclared agent before you spend a token.

## Shape B — the procedural script

Here the agent's `.body()` **is** the program. Use it when the logic is yours and the LLM is a
subroutine, or when there is no LLM at all.

```ts
const calculator = agentBuilder()
    .name("Calculator").role("Computer").goal("Sum a list of numbers")
    .body((input, ctx) => {
        const total = [1, 2, 3, 4, 5].reduce((a, n) => a + n, 0);
        ctx.log.info(`computed total = ${total}`);
        return total;
    })
    .build();

await crewBuilder().withAgent(calculator).build().run();
```

### `ctx.llm.act` — an agent loop in nine lines

`act` runs the LLM ⇄ tool-call cycle over the agent's own catalogue until the model stops
asking for tools or `maxIterations` is reached:

```ts
.body(async (input, ctx) => {
    return await ctx.llm.act("Summarise the release note in /script/notes.md", {
        system: "You are terse. Two sentences, no preamble.",
        maxIterations: 5,
        onDelta: (d) => ctx.log.info(d),
    });
})
```

`system` seeds a real `role:"system"` message that persists across every iteration. Without
it, `act()` sends a single user message — identity and tool policy travelling with user-level
authority, and the providers' native system handling never firing.

### State

An agent can carry state, and there is exactly one legal way to change it:

```ts
.withState(() => ({ count: 0 }))
.body(async (input, ctx) => {
    await ctx.state.with(prev => ({ ...prev, count: prev.count + 1 }));
    return ctx.state.count;
})
```

`with()` **replaces** the state with what the callback returns, under a mutex — so spread the
fields you are not changing. Assigning directly (`ctx.state.count = 1`) throws: the state is a
proxy whose `set` trap exists to make that loud rather than lost.

### Inputs, memory, errors

```ts
const topic = (globalThis.inputs?.topic as string) ?? "espresso";
```

```bash
… run examples/scripting/10-inputs-and-memory.ork.ts --inputs '{"topic":"orkeon"}'
```

`ctx.memory.crew` and `ctx.memory.agent` are scoped key/value stores. `onError` returns an
action built from a factory — **not** a string:

```ts
.onError((err) => ErrorAction.retry({ delay: 10, max: 3 }))
```

`ErrorAction.fail()`, `.skip()`, `.fallback(value)`, `.retry({...})`. Returning anything the
runtime does not recognise becomes `fail()`.

## Editor setup

The typings ship as `orkeon.d.ts` — next to the CLI build output, and inside the `Orkeon`
package at `content/typings/orkeon.d.ts`. Point your editor at it, and copy
[`tools/scripting-typecheck/tsconfig.base.json`](https://github.com/Orkeon/orkeon/blob/main/tools/scripting-typecheck/tsconfig.base.json).

Three of its options are load-bearing, and skipping them produces errors that have nothing to
do with your code:

- **`moduleDetection: "force"`** — otherwise top-level `await crew.run()` is rejected (TS1375)
  and a top-level `const crew` collides with the `crew` global (TS2451).
- **`target`/`lib` `ES2022`** — what the engine supports.
- **`types: []`** — there is no Node here. `process`, `require` and `Buffer` do not exist.

## What the runtime actually is

`.ork.ts` is transpiled by **esbuild** and executed by **Jint**. That has consequences worth
knowing before you are surprised by them:

- **It is TypeScript syntax, not the TypeScript compiler.** Types are stripped, never checked.
  Nothing at run time will tell you a type was wrong — which is why the tsconfig above matters.
- **There is no Node and no DOM.** No `fs`, no `fetch`, no `process`. File access goes through
  tools; `ctx.signal` is a .NET cancellation token, not an `AbortSignal`.
- **One thread, no event loop.** A host call that waits blocks the whole script.
- **`import` works between your own files**, resolved relative to the script.

## Ten errors and what they mean

| Message | Cause |
|---|---|
| `did not assign globalThis.crew` | `--validate`, or a runner that wants the declarative shape, on a file ending in `await crew.run()`. |
| The crew runs but every task is ignored | The reverse: `withTask` in a file ending with `await crew.run()`. |
| `Crew configuration references unknown tool(s): x` | A name in `.tools([...])` the host catalogue does not have. The message lists every name that *is* available. |
| `FSM transition 'a.b' targets undeclared state 'c'` | A `target` that is not a key of `states`. |
| `stateGraph literal must declare an edge from START` | `edges` written as an array. It is an object keyed by source node. |
| `stateGraph has no path from START to END` | No static route; add one or use a conditional edge. |
| `StateMutationOutsideWithException` | `ctx.state.x = …` instead of `ctx.state.with(...)`. |
| `.concurrency must be positive` / `V1 supports .concurrency(1) only` | The N-holder semaphore is V1.5. |
| `RecursiveAgentInvocationException` | `ctx.spawn` of an agent with the caller's own name. |
| A retry that never happens | `onError` returning a string instead of `ErrorAction.retry({...})`. |

## Deliberately out of scope

`budget()` and `globalThis.inputs` do nothing on the declarative path; `.body()` does nothing
there either. `.when(predicate)` does nothing **anywhere** — guard the `withAgent`/`withTask`
call with an `if` instead. `ctx.llm.embed` returns a stub vector. The full list, with what
each one actually does, is in
[Known gaps between the typings and the runtime](../reference/scripting-dsl.md#known-gaps-between-the-typings-and-the-runtime).

## Where to go next

- [Scripting DSL reference](../reference/scripting-dsl.md) — every method, and which shape honours it.
- [Scripting DSL — architecture](../architecture/scripting.md) — what the runtime is and where it sits.
- [Driving crews from the REPL](../architecture/coding-agent-ts.md) — running a crew from a `.cmd.ts` command.
- [`examples/scripting/`](https://github.com/Orkeon/orkeon/blob/main/examples/scripting/README.md) — every snippet above, runnable.
