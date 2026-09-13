/// <reference orkeon-script="1.0" />

// A script whose FIRST top-level await is not `crew.run()`: it reads its brief from disk,
// then runs the crew on what it read.
//
// The order matters more than it looks. The other files that run a crew start it from the
// script's synchronous prefix; this one starts it from a promise continuation, after the file
// read has settled. Before SCR-25 that second shape never returned — the crew's synchronous drain was
// reached from inside an event-loop job it could not pump, and the script sat on a 30-minute
// ceiling. One thread drains the engine now and the crew loop is JavaScript under it, so a
// crew started after an await behaves exactly like one started before it.
//
// Runs with no API key:
//
//   dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/11-await-before-run.ork.ts

// 1. The await that is NOT crew.run(). `/script` is the implicit read-only mount on this
//    file's directory; outside a body `tools.fileRead` takes no ctx argument.
const brief = await tools.fileRead({ path: "/script/data/11-await-before-run/brief.md" });
const text = brief.content;

// 2. Agents close over what was read. On the procedural shape a body receives no input, so
//    top-level values reach it through the closure — the same way `globalThis.inputs` does
//    in 10-inputs-and-memory.
const openItems = text.split("\n").filter((line) => line.startsWith("- [ ]"));

const triager = agentBuilder()
    .name("triager")
    .role("Triage")
    .goal("Order the open items of the brief by urgency")
    .body(async (input, ctx) => {
        ctx.log.info(`${openItems.length} open item(s) in the brief`);
        return await ctx.llm.complete(
            `Order these items by urgency, most urgent first, one per line:\n${openItems.join("\n")}`);
    })
    .build();

const writer = agentBuilder()
    .name("writer")
    .role("Release writer")
    .goal("Draft the headline of the release note")
    .body(async (input, ctx) => {
        const title = text.split("\n")[0].replace(/^#\s*/, "");
        return await ctx.llm.complete(`Write a one-line headline for a release named "${title}".`);
    })
    .build();

const crew = crewBuilder().name("sprint-desk").withAgent(triager).withAgent(writer).build();

// 3. crew.run() as the script's SECOND await.
const res = await crew.run();
globalThis.result = {
    briefChars: text.length,
    openItems: openItems.length,
    agents: res.tasks.map((t) => t.name),
    output: res.output,
};
