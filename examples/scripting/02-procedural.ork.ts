/// <reference orkeon-script="1.0" />

// Procedural body: no LLM call, fully deterministic.
const calculator = agentBuilder()
    .name("Calculator").role("Computer").goal("Sum a list of numbers")
    .body((input, ctx) => {
        const numbers = [1, 2, 3, 4, 5];
        const total = numbers.reduce((acc, n) => acc + n, 0);
        ctx.log.info(`computed total = ${total}`);
        return total;
    })
    .build();

const crew = crewBuilder().withAgent(calculator).build();
await crew.run();
