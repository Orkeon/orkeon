let stage = "never-started";
const worker = agentBuilder()
    .name("worker").role("Worker").goal("Do async work")
    .body(async (input, ctx) => {
        stage = "started";
        await ctx.llm.complete("ping");
        stage = "finished";
        return "ok";
    })
    .build();
const crew = crewBuilder().name("contract-crew").withAgent(worker).build();
const res = await crew.run();
result = {
    stage: stage,
    output: res ? res.output : "(no result object)",
    taskCount: res && res.tasks ? res.tasks.length : -1,
};
