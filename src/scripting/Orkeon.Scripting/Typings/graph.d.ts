// Orkeon Scripting DSL — State graph literal (LangGraph-style)
// See chapter 07 (orchestration-fsm-graph.md).

declare global {
    const START: unique symbol;
    const END: unique symbol;
    type GraphNode = string | typeof START | typeof END;

    interface GraphEdgeOptions<TState> {
        when?: (state: TState) => boolean;
    }

    interface StateGraphDefinition<TState> {
        name: string;
        nodes: Record<string, (state: TState) => Promise<TState> | TState>;
        edges: ReadonlyArray<{
            from: GraphNode;
            to: GraphNode | ((state: TState) => GraphNode);
            opts?: GraphEdgeOptions<TState>;
        }>;
        circuitBreaker?: CircuitBreakerOptions;
    }

    interface StateGraph<TState> {
        readonly name: string;
        run(state: TState, opts?: CrewRunOptions): Promise<TState>;
        runStream(state: TState, opts?: CrewRunOptions): AsyncIterable<CrewStreamEvent>;
    }

    function stateGraph<TState>(def: StateGraphDefinition<TState>): StateGraph<TState>;
}

export { };
