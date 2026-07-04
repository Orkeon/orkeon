/// <reference orkeon-script="1.0" />

// Mixed mode: imperative body that calls the LLM via ctx.llm.
// Falls back to UndefinedLlm (echo) when no provider is configured.
const summarizer = agentBuilder()
    .name("Summarizer").role("Editor").goal("Summarize a text in one sentence")
    .body(async (input, ctx) => {
        const text = "Orkeon enables collaborative AI agent teams.";
        const summary = await ctx.llm.complete(`Summarize in one sentence: ${text}`);
        return { summary };
    })
    .build();

const crew = crewBuilder().withAgent(summarizer).build();
await crew.run();
