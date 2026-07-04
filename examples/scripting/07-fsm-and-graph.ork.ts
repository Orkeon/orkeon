/// <reference orkeon-script="1.0" />

// stateMachine declares an FSM in literal form; stateGraph does the same for
// LangGraph-style conditional flows. Both are exposed through global functions.

const orderFsm = stateMachine({
    name: "order-fsm",
    initial: "pending",
    states: {
        pending:  { transitions: { approve: { target: "approved" }, reject: { target: "rejected" } } },
        approved: { transitions: { ship: { target: "shipped" } } },
        shipped:  {},
        rejected: {},
    },
});

const researchGraph = stateGraph({
    name: "research",
    nodes: {
        gather: (s) => ({ ...s, gathered: true }),
        analyse: (s) => ({ ...s, analysed: true }),
    },
    edges: {
        [START]: "gather",
        gather: "analyse",
        analyse: END,
    },
});

const agent = agentBuilder()
    .name("Coordinator").role("FSM/Graph driver").goal("Walk the FSM and the graph")
    .body(async (input, ctx) => {
        await orderFsm.send("approve");
        await orderFsm.send("ship");
        const final = await researchGraph.run({ analysed: false });
        return { fsm: orderFsm.current, graph: final };
    })
    .build();

const crew = crewBuilder().withAgent(agent).build();
await crew.run();
