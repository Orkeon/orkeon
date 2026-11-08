/// <reference orkeon-script="1.0" />

const writer = agentBuilder()
    .name("writer")
    .role("Writer")
    .goal("Answer the question in one short paragraph")
    .backstory("A concise technical writer.")
    .maxIterations(3)
    .build();

const answer = taskBuilder()
    .name("answer")
    .agent(writer)
    .description("Explain what a crew of agents is.")
    .expectedOutput("One short paragraph")
    .build();

const crew = crewBuilder()
    .name("hello-crew")
    .goal("Answer a question")
    .process("sequential")
    .withAgents([writer])
    .withTasks([answer])
    .build();

globalThis.crew = crew;
