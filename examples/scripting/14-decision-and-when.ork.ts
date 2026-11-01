// A decision task and the tasks that branch on it. `triage` asks the configured decision model
// two questions about what `intake` produced; `refund` runs only when the category is billing,
// `fix` only when it is a bug, and `other` when no sibling matched (`"otherwise"`).
//
// With the decision circuit off (`--no-decision`, no model) `triage` answers from `fallback`.
// A `choice` value, a `score` entry and a `fallback` value must all be strings, and a task that
// gates on a decision has exactly one `.when(...)`: the loader refuses the script otherwise.

const triager = agentBuilder()
    .name("triager")
    .role("Support triage")
    .goal("Read incoming tickets and summarise them")
    .build();

const intake = taskBuilder()
    .name("intake")
    .description("Summarise the customer ticket in two sentences.")
    .expectedOutput("A two-sentence summary of the ticket.")
    .agent(triager)
    .build();

const triage = taskBuilder()
    .name("triage")
    .decision({
        questions: {
            category: { choice: { bug: "a defect in the product", billing: "a question about money" } },
            urgency: { score: ["low", "high"], instructions: "How fast must we answer?" },
        },
        minConfidence: 0.6,
        state: ["intake"],
        fallback: { category: "bug", urgency: "low" },
    })
    .build();

const refund = taskBuilder()
    .name("refund")
    .description("Draft the refund answer.")
    .expectedOutput("A short refund answer for the customer.")
    .agent(triager)
    .when("triage", { category: ["billing"] })
    .build();

const fix = taskBuilder()
    .name("fix")
    .description("Draft the bug acknowledgement.")
    .expectedOutput("A short acknowledgement of the bug report.")
    .agent(triager)
    .when("triage", { category: ["bug"], urgency: ">= 0.7" })
    .build();

const other = taskBuilder()
    .name("other")
    .description("Draft a generic answer.")
    .expectedOutput("A short generic answer for the customer.")
    .agent(triager)
    .when("triage", "otherwise")
    .build();

const crew = crewBuilder()
    .name("desk")
    .withAgent(triager)
    .withTask(intake)
    .withTask(triage)
    .withTask(refund)
    .withTask(fix)
    .withTask(other)
    .build();

await crew.run();
