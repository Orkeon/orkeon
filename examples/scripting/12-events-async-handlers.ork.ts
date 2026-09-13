/// <reference orkeon-script="1.0" />

// Topic handlers that suspend, and two crews run one after the other.
//
// Two shapes that used to hang, side by side. A topic handler that awaits the LLM: the
// publish that fed it was reached after an await in the publishing body, so it ran from
// inside an event-loop job and drained the handler there, where nothing could pump. And a
// second `crew.run()` after a first one settled: the same call, from inside the first run's
// continuation. Both settle now that one thread drains the engine and every callback into
// the script — the delivery loop, the crew loop — is JavaScript under it (SCR-25).
//
// Runs with no API key:
//
//   dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/12-events-async-handlers.ork.ts

type Finding = { from: string; fact: string };
const digest: string[] = [];

// A reporter checks its finding with the LLM (an await), THEN publishes it. On a sequential
// topic `await publish()` resolves only once every handler has run — including the one
// below that awaits the LLM itself.
const reporter = (name: string, finding: string) => agentBuilder()
    .name(name)
    .role("Field reporter")
    .goal(`Report one finding: ${finding}`)
    .body(async (input, ctx) => {
        const checked = await ctx.llm.complete(`Restate this finding as one fact: ${finding}`);
        await ctx.events.topic<Finding>("findings").publish({ from: name, fact: checked });
        return `reported ${name}`;
    })
    .build();

const fieldTeam = crewBuilder()
    .name("field-team")
    // Subscribe from the crew hook, not from an agent body: the hook's ctx lives for the
    // whole run, whereas an agent's ctx is released when its body returns — a handler that
    // closed over it would be talking to a finished agent by the time the next one publishes.
    .onCrewStart((ctx) => {
        ctx.events.topic<Finding>("findings").subscribe(async (ev) => {
            // Awaiting inside a handler: the publisher waits for this line to settle.
            const line = await ctx.llm.complete(`Turn this into a digest line: ${ev.value.fact}`);
            digest.push(`[${ev.value.from}] ${line}`);
        });
    })
    .withAgent(reporter("north", "the roaster reaches temperature in 90 seconds"))
    .withAgent(reporter("south", "the scale reads 2 g high above 500 g"))
    .build();

const desk = crewBuilder()
    .name("desk")
    .withAgent(agentBuilder()
        .name("editor")
        .role("Editor")
        .goal("Close the digest")
        .body(async (input, ctx) =>
            await ctx.llm.complete(`Write a two-line summary of:\n${digest.join("\n")}`))
        .build())
    .build();

// Two runs in sequence. The second starts from the first one's continuation — the shape a
// script takes as soon as it has more than one crew.
const field = await fieldTeam.run();
const closed = await desk.run();

globalThis.result = {
    reporters: field.tasks.map((t) => t.name),
    digest,
    summary: closed.output,
};
