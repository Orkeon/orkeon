// Orkeon Scripting DSL — Crew builder
// Declares Crew, CrewBuilder, the six process modes, and the result and stream shapes a run produces.

declare global {
    type Process =
        | "sequential"
        | "hierarchical"
        | "parallel"
        | "consensual"
        | "graph"
        | "autonomous";

    interface CrewRunOptions {
        /**
         * A signal obtained from `ctx.signal`. Anything else is ignored: the runner only
         * honours a value that arrives as a .NET CancellationToken.
         */
        signal?: CancellationSignal;
        timeout?: number | string;
        // No `inputs` (GAP-12): it was declared and never read. A procedural script reads the
        // `inputs` global the runner plants from --inputs.
    }

    /**
     * What `crew.run()` resolves to: exactly what the runtime serves (GAP-27). No `artifacts`:
     * it was declared and always empty, so `result.artifacts.get("x")` compiled and threw.
     */
    interface CrewResult {
        /** The text of the last non-null agent output; `""` when no agent returned anything. */
        readonly output: string;
        /** One entry per agent run, in the order they ran. */
        readonly tasks: readonly TaskResult[];
    }

    interface TaskResult {
        /** The agent's name. */
        readonly name: string;
        /** What the agent's body returned (`null` for `undefined` or a skipped body). */
        readonly output: unknown;
        /** Wall time of the agent's run, every attempt included. */
        readonly durationMs: number;
    }

    interface Crew {
        readonly name: string;
        /** The members, in the order they joined. */
        readonly agents: readonly Agent<unknown, unknown>[];
        run(opts?: CrewRunOptions): Promise<CrewResult>;
        runAgent<TIn, TOut>(agent: string | Agent<TIn, TOut>, input: TIn, opts?: CrewRunOptions): Promise<TOut>;
        runStream(opts?: CrewRunOptions): AsyncIterable<CrewStreamEvent>;
        add(agent: Agent<unknown, unknown>): void;
        remove(agent: Agent<unknown, unknown> | string): void;
        has(agent: Agent<unknown, unknown> | string): boolean;
        findByName(name: string): Agent<unknown, unknown> | undefined;
        findById(id: string): Agent<unknown, unknown> | undefined;
        findByRole(role: string): readonly Agent<unknown, unknown>[];
    }

    /**
     * One step of `crew.runStream()`: the procedural shape runs agents, not tasks or a graph,
     * so these are the only two events it emits. `payload` is `{ name }` on start and
     * `{ name, output }` on stop; `at` is epoch milliseconds.
     */
    interface CrewStreamEvent {
        readonly type: "agent.start" | "agent.stop";
        readonly payload: { readonly name: string; readonly output?: unknown };
        readonly at: number;
    }

    interface CrewBuilder {
        name(value: string): this;
        /** The crew goal; when omitted, one is synthesized from the name. */
        goal(value: string): this;
        /**
         * `process("graph")` runs the crew on the domain's graph strategy — a
         * retry-and-route topology with a circuit breaker, tuned by `GraphConfig`,
         * not a topology the script draws. To author your own nodes and edges, build a
         * `stateGraph({...})` and call `.run()` on it from an agent `.body()`.
         */
        process(value: Process): this;
        /** A built agent (`agentBuilder()…build()`); a builder callback is refused. */
        withAgent(agent: Agent<unknown, unknown>): this;
        withAgents(agents: readonly Agent<unknown, unknown>[]): this;
        /** A built task (`taskBuilder()…build()`); a builder callback is refused. */
        withTask(task: Task<unknown, unknown>): this;
        withTasks(tasks: readonly Task<unknown, unknown>[]): this;
        manager(agent: Agent<unknown, unknown>): this;
        budget(opts: ExecutionBudget): this;
        verbose(value?: boolean): this;
        /**
         * YAML parity `memory: true` — the crew remembers: each run stores the result of its tasks
         * and recalls the closest ones before each task, in the host's default memory store, under
         * the crew's name. Off by default. Not `ctx.memory.*`, the scoped key/value stores of a run.
         */
        memory(value?: boolean): this;
        /**
         * YAML parity `planning: true` — before the first task, a planner writes a step-by-step
         * plan for each task, which the task reads in its prompt, in every process. The plan
         * changes neither the order of the tasks nor who runs them. On the host's default LLM
         * profile; skipped, with a warning, on the echo provider. Off by default.
         */
        planning(value?: boolean): this;
        onCrewStart(hook: (ctx: ExecutionContext) => Promise<void> | void): this;
        onCrewComplete(hook: (ctx: ExecutionContext, result: CrewResult) => Promise<void> | void): this;
        /** `message` is the displayed message of the failure — the innermost CLR message, or a script error's own. */
        onCrewError(hook: (ctx: ExecutionContext, message: string) => Promise<void> | void): this;
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
