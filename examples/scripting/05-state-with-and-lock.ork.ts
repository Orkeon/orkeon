/// <reference orkeon-script="1.0" />

// state.with serializes mutations; ctx.lock guards a critical section.
const counter = agentBuilder()
    .name("Counter").role("Aggregator").goal("Count safely under contention")
    .withState(() => ({ count: 0 }))
    .body(async (input, ctx) => {
        await ctx.lock("increment", async () => {
            await ctx.state.with(prev => ({ count: prev.count + 1 }));
            await ctx.state.with(prev => ({ count: prev.count + 1 }));
        });
        return ctx.state.count;
    })
    .build();

const crew = crewBuilder().withAgent(counter).build();
await crew.run();
