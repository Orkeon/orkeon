/// <reference orkeon-script="1.0" />

// Minimal Orkeon script: one agent, one body, one crew run.
const greeter = agentBuilder()
    .name("Greeter").role("Greeter").goal("Greet the user")
    .body((input, ctx) => "Hello from Orkeon Scripting!")
    .build();

const crew = crewBuilder()
    .name("greeting-crew")
    .withAgent(greeter)
    .build();

await crew.run();
