const worker = agentBuilder()
    .name("worker").role("Worker").goal("Park on the host")
    .body(async (input, ctx) => {
        await ctx.llm.complete("park");
        return "unreachable";
    })
    .build();
const crew = crewBuilder().name("parked-crew").withAgent(worker).build();
await crew.run();
