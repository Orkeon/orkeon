// Orkeon Scripting DSL — Crew builder
// See chapter 03 (creation-agent-crew-task.md).

declare global {
    type Process =
        | "sequential"
        | "hierarchical"
        | "parallel"
        | "consensual"
        | "graph"
        | "autonomous";

    interface CrewRunOptions {
        signal?: AbortSignal;
        timeout?: number | string;
        inputs?: Record<string, unknown>;
    }

    interface CrewResult<TOut = unknown> {
        readonly output: TOut;
        readonly artifacts: ReadonlyMap<string, unknown>;
        readonly tasks: readonly TaskResult[];
    }

    interface TaskResult<TOut = unknown> {
        readonly name: string;
        readonly output: TOut;
        readonly durationMs: number;
    }

    interface Crew {
        readonly name: string;
        run<TOut = unknown>(opts?: CrewRunOptions): Promise<CrewResult<TOut>>;
        runAgent<TIn, TOut>(agent: string | Agent<TIn, TOut>, input: TIn, opts?: CrewRunOptions): Promise<TOut>;
        runStream(opts?: CrewRunOptions): AsyncIterable<CrewStreamEvent>;
        add(agent: Agent<unknown, unknown>): void;
        remove(agent: Agent<unknown, unknown> | string): void;
        has(agent: Agent<unknown, unknown> | string): boolean;
        findByName(name: string): Agent<unknown, unknown> | undefined;
        findByRole(role: string): readonly Agent<unknown, unknown>[];
    }

    interface CrewStreamEvent {
        readonly type: "agent.start" | "agent.stop" | "task.start" | "task.complete" | "task.error" | "graph.node" | "graph.edge";
        readonly payload: unknown;
        readonly at: number;
    }

    interface CrewBuilder {
        name(value: string): this;
        /** The crew goal; when omitted, one is synthesized from the name. */
        goal(value: string): this;
        process(value: Process): this;
        withAgent(agent: Agent<unknown, unknown> | ((b: AgentBuilder) => AgentBuilder)): this;
        withAgents(agents: readonly Agent<unknown, unknown>[]): this;
        withTask(task: Task<unknown, unknown> | ((b: TaskBuilder) => TaskBuilder)): this;
        withTasks(tasks: readonly Task<unknown, unknown>[]): this;
        manager(agent: Agent<unknown, unknown>): this;
        budget(opts: ExecutionBudget): this;
        graph(graph: StateGraph<unknown>): this;
        verbose(value?: boolean): this;
        /** YAML parity `memory: true` — the crew keeps a shared memory scope. */
        memory(value?: boolean): this;
        when(predicate: () => boolean): this;
        onCrewStart(hook: (ctx: ExecutionContext) => Promise<void> | void): this;
        onCrewComplete(hook: (ctx: ExecutionContext, result: CrewResult) => Promise<void> | void): this;
        onCrewError(hook: (ctx: ExecutionContext, err: Error) => Promise<void> | void): this;
        build(): Crew;
    }

    interface ExecutionBudget {
        readonly toolCalls?: number;
        readonly delegationDepth?: number;
        readonly wallTime?: number | string;
        readonly tokens?: number;
        readonly spawnedAgents?: number;
    }

    function crewBuilder(): CrewBuilder;
}

export { };
