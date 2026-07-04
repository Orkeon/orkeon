/// <reference orkeon-script="1.0" />

// ctx.spawn lets an agent grow the crew at runtime.
const orchestrator = agentBuilder()
    .name("Orchestrator").role("Coordinator").goal("Spawn a worker agent on demand")
    .body((input, ctx) => {
        const worker = ctx.spawn(
            agentBuilder().name("Worker").role("Worker").goal("Do work")
                .body(() => "worker-done"));
        return `spawned ${worker.name}`;
    })
    .build();

const crew = crewBuilder().withAgent(orchestrator).build();
await crew.run();
