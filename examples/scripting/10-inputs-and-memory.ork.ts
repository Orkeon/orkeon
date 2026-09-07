/// <reference orkeon-script="1.0" />

// How a script receives data, keeps some between agents, and survives a flaky step.
//
// PROCEDURAL shape (ends with `crew.run()`), so `.body()` runs and `globalThis.inputs` is
// honoured. On the declarative shape — the one that ends with `globalThis.crew = crew` —
// both are ignored instead.
//
//   dotnet run --project src/scripting/Orkeon.Scripting.Cli -- \
//       run examples/scripting/10-inputs-and-memory.ork.ts --inputs '{"topic":"espresso"}'
//
// Runs with no API key. Without --inputs the topic falls back to "coffee".

// `--inputs '<json>'` (or `--inputs-file`) lands here. It is NOT declared in the typings, so
// read it defensively and give every field a default: a script that assumes its caller
// passed something is a script that fails on the first bare `orkeon run`.
const passed = (globalThis as any).inputs ?? {};
const topic: string = passed.topic ?? "coffee";

let attempts = 0;

const researcher = agentBuilder()
    .name("researcher")
    .role("Researcher")
    .goal("Collect a fact and leave it where the next agent will look")
    // Per-agent state. Mutate it only through `state.with`, which serialises the change;
    // assigning to ctx.state directly raises StateMutationOutsideWithError.
    .withState(() => ({ seen: 0 }))
    .body(async (input, ctx) => {
        attempts++;
        // First attempt fails on purpose to exercise the error policy below.
        if (attempts === 1) throw new Error("upstream not ready");

        await ctx.state.with((prev) => ({ seen: prev.seen + 1 }));

        // Crew memory outlives the agent: the reporter below reads this back.
        await ctx.memory.crew.store("topic", topic);
        await ctx.memory.crew.store("fact", `${topic} is brewed under pressure`);
        return `collected ${ctx.state.seen} item(s) about ${topic}`;
    })
    // The error policy is consulted before the run is failed. Return one of the four
    // `ErrorAction` factories — `retry`, `skip`, `fallback`, `fail`. A plain object literal
    // is NOT accepted: the runtime wants an ErrorAction instance and treats anything else
    // as `fail()`, so the retry silently never happens.
    .onError((err) => ErrorAction.retry({ delay: 10, max: 3 }))
    .onAgentStart((ctx) => ctx.log.info("researcher online"))
    .onAgentStop((ctx) => ctx.log.info("researcher offline"))
    .build();

const reporter = agentBuilder()
    .name("reporter")
    .role("Reporter")
    .goal("Report what the researcher left in memory")
    .body(async (input, ctx) => {
        const fact = await ctx.memory.crew.get<string>("fact");
        return fact ?? "(nothing in crew memory)";
    })
    .build();

// Agents run in declaration order on this shape, which is what lets the reporter read what
// the researcher stored.
const crew = crewBuilder().name("field-notes").withAgent(researcher).withAgent(reporter).build();

const res = await crew.run();
(globalThis as any).result = {
    topic: topic,
    attempts: attempts,
    output: res.output,
    agents: res.tasks.map((t) => t.name),
};
