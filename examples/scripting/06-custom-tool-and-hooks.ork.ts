/// <reference orkeon-script="1.0" />

// Custom tool authored with toolBuilder + lifecycle hooks on the agent.
// The generic states what the schema below promises: TypeScript does not read the schema,
// so without it `input` is `unknown` and every field access is an error.
const reverseTool = toolBuilder<{ value: string }, { reversed: string }>()
    .name("reverse")
    .description("Reverses a string")
    .withSchema({
        type: "object",
        properties: { value: { type: "string", description: "Text to reverse" } },
        required: ["value"]
    })
    .execute((input, ctx) => ({ reversed: input.value.split("").reverse().join("") }))
    .build();

const agent = agentBuilder()
    .name("Reverser").role("Reverser").goal("Reverse a phrase")
    .withAutonomousTool(reverseTool)
    .body(async (input, ctx) => await reverseTool.execute({ value: "Orkeon" }, ctx))
    .onAgentStart(ctx => ctx.log.info("Reverser is online"))
    .onAgentStop(ctx => ctx.log.info("Reverser is offline"))
    .build();

const crew = crewBuilder().withAgent(agent).build();
await crew.run();
