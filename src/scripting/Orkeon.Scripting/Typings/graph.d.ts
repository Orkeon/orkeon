// Orkeon Scripting DSL - State graph literal (LangGraph-style)
// Declares the stateGraph() literal: nodes, the edge map, the START/END markers and the
// circuit-breaker budget.
//
// Rewritten on 2026-09-07 against StateGraphBinding.cs and JsStateGraph.cs. The declaration
// it replaces was wrong on four counts: `edges` was an ARRAY of `{ from, to, opts }` where
// the runtime wants an OBJECT keyed by source node; the budget was called `circuitBreaker`
// with failure-window fields where the runtime reads `graphConfig` with transition-count
// fields; `START`/`END` were `unique symbol` where the runtime plants plain strings; and
// `run`/`runStream` took a `CrewRunOptions` they never accepted. The array form fails loudly
// (its keys are indices, so no edge from START is found) rather than silently -- unlike the
// FSM declaration next door, which is why this one had not been noticed either.

declare global {
    /**
     * The entry pseudo-node. A plain string at runtime (`"__START__"`), not a symbol, so it
     * works as a computed key: `edges: { [START]: "first-node" }`.
     */
    const START: "__START__";
    /** The exit pseudo-node, `"__END__"`. Used as an edge TARGET; never declare it in `nodes`. */
    const END: "__END__";

    /** A node id, or one of the two markers. */
    type GraphNode = string;

    /**
     * Where an edge leads: a node id, `END`, or a function computing one from the state
     * that the node just returned. A conditional edge is resolved on every traversal, so
     * it is also how a cycle is expressed.
     */
    type GraphEdge<TState> = GraphNode | ((state: TState) => GraphNode);

    /**
     * The traversal budget. Every bound is enforced per `run()`, and exceeding one throws.
     * A preset sets all four at once; an explicit field written alongside it wins.
     */
    interface GraphConfig {
        /** Strict = 50/3/1/30 s, Default = 200/10/5/5 min, Permissive = 1000/100/50/30 min. */
        circuitBreakerPreset?: "Strict" | "Default" | "Permissive";
        /** Total node traversals. Default 200. */
        maxTransitions?: number;
        /** Traversals of any single node -- the bound that catches a tight cycle. */
        maxStateVisits?: number;
        /** Re-entries of an already-visited node. */
        maxRetryCycles?: number;
        /** Wall-clock bound; cancels the run rather than throwing on the next hop. */
        maxTotalDurationSeconds?: number;
    }

    /** One hop, as yielded by `runStream`. */
    interface GraphStreamEvent<TState = Record<string, unknown>> {
        readonly fromNode: string;
        /** The node just entered, or the literal `"END"` on the final hop. */
        readonly toNode: string;
        readonly state: TState;
    }

    interface StateGraphDefinition<TState = Record<string, unknown>> {
        name: string;
        /**
         * Keyed by node id. Each node takes the current state and returns the next one;
         * the return value REPLACES the state, so spread it (`{ ...s, done: true }`).
         * `START` and `END` are reserved and rejected as node ids.
         */
        nodes: Record<string, (state: TState) => TState | Promise<TState>>;
        /**
         * Keyed by SOURCE node, not a list of pairs. Must contain an entry for `[START]`,
         * and END must be statically reachable from it, or the literal is rejected at
         * build time. String targets are checked against `nodes` then; conditional edges
         * can only be checked when they run.
         */
        edges: Record<string, GraphEdge<TState>>;
        graphConfig?: GraphConfig;
    }

    interface StateGraph<TState = Record<string, unknown>> {
        readonly name: string;
        /** Walks from START until END, returning the final state. */
        run(state: TState): Promise<TState>;
        /** The same walk, yielding one event per hop. */
        runStream(state: TState): AsyncIterable<GraphStreamEvent<TState>>;
    }

    /**
     * Builds a graph from a literal.
     *
     * `TState` cannot be inferred from the node bodies -- a node both takes and returns the
     * state, so inference is circular -- and falls back to `Record<string, unknown>`, which
     * spreads and indexes but checks nothing. Annotate the call to get real checking:
     * `stateGraph<OrderState>({ ... })` then reports a node that returns the wrong shape.
     */
    function stateGraph<TState = Record<string, unknown>>(
        def: StateGraphDefinition<TState>,
    ): StateGraph<TState>;
}

export { };
