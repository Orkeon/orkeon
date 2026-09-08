/// <reference orkeon-script="1.0" />

// The three tool surfaces, then `ctx.llm.act` — the loop that lets the model use them.
//
// This is a PROCEDURAL script: it ends with `await crew.run()`, so the engine runs each
// agent's `.body()` and ignores tasks, process and manager. Never put `withTask` in a file
// that ends this way — the tasks would be silently dropped.
//
// Runs with no API key: the UndefinedLlm echo provider answers in one turn, so `act`
// returns without calling a tool. Point it at a real provider to watch the loop iterate:
//
//   dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/09-tools-and-act.ork.ts

// Surface 2 — a tool you author here, handed to the agent as an instance.
const wordCount = toolBuilder<{ text: string }, { words: number }>()
    .name("word_count")
    .description("Counts the words of a text")
    .withSchema({
        type: "object",
        properties: { text: { type: "string", description: "Text to measure" } },
        required: ["text"]
    })
    .execute((input, ctx) => ({ words: String(input.text).trim().split(/\s+/).length }))
    .build();

const analyst = agentBuilder()
    .name("analyst")
    .role("Release analyst")
    .goal("Summarise a release note and say how long it is")
    // Surface 1 — a built-in, by name, resolved from the host catalogue.
    .tools(["file_read"])
    // Surface 2 — the instance authored above. Both end up in the same catalogue,
    // which is exactly what `act` iterates over.
    .withAutonomousTool(wordCount)
    .body(async (input, ctx) => {
        // Surface 3 — calling a tool imperatively, outside any LLM loop. `/script` is the
        // implicit read-only mount on this file's directory.
        const notes = await tools.fileRead(
            { path: "/script/data/09-tools-and-act/release-notes.md" }, ctx);
        ctx.log.info(`read ${String(notes.content).length} characters`);

        // `act` runs the LLM ⇄ tool-call cycle over the agent's catalogue until the model
        // stops asking for tools, or maxIterations is reached.
        return await ctx.llm.act<string>(
            `Summarise these release notes in one sentence, then call word_count on your
             summary and report the number.\n\n${notes.content}`,
            {
                // Seeded as a real system message, ahead of the whole conversation.
                system: "You are terse. One sentence, then the tool call.",
                // Default is 10. Keep it small when a run must stay cheap.
                maxIterations: 4,
                // Only bites when the host registered a permission gate; "plan" refuses writes.
                permissionMode: "plan",
                // Called with each streamed content delta. Keep it cheap — it runs inline.
                onDelta: (d) => ctx.log.debug(d),
            });
    })
    .build();

const crew = crewBuilder().name("release-desk").withAgent(analyst).build();

// The CLI prints `globalThis.result`, not the return of `crew.run()`. A script that only
// calls `await crew.run()` prints `{ "result": null }` — the work happened, nothing was
// handed back. Assign a PLAIN object: the CrewResult itself is a CLR object the serializer
// refuses, and you get a ToString() fallback instead of your data.
const res = await crew.run();
(globalThis as any).result = {
    output: res.output,
    agents: res.tasks.map((t) => ({ name: t.name, ms: Math.round(t.durationMs) })),
};
