test.mockLlm.when("ping").respond("from-test");
let answers = "";
const worker = agentBuilder()
    .name("worker").role("Worker").goal("Ask the model twice")
    .body(async (input, ctx) => {
        const a = await ctx.llm.complete("ping");
        const b = await ctx.llm.complete("other");
        answers = a + "|" + b;
        return answers;
    })
    .build();
const crew = crewBuilder().name("override-crew").withAgent(worker).build();
await crew.run();
test.assertLlmCalled({ times: 2 });
result = { answers: answers };
