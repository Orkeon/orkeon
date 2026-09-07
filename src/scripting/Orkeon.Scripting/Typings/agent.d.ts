// Orkeon Scripting DSL — Agent builder
// See chapter 03 (creation-agent-crew-task.md).

declare global {
    interface Agent<TIn = unknown, TOut = unknown> {
        readonly name: string;
        readonly id: string;
        readonly role: string;
    }

    interface AgentBuilder<TIn = unknown, TOut = unknown, TState = unknown> {
        name(value: string): this;
        role(value: string): this;
        goal(value: string): this;
        backstory(value: string): this;
        llm(config: LlmConfig | string): this;
        allowDelegation(value: boolean): this;
        maxIterations(value: number): this;
        verbose(value?: boolean): this;
        concurrency(n: number): this;
        withState<S>(factory: (() => S) | S): AgentBuilder<TIn, TOut, S>;
        /**
         * Registers built-in tool names resolved by `IToolRegistry` at runtime.
         * Mirror of YAML `tools: [...]` on an agent block. Accepts a single name or
         * an array; tool instances belong to `withAutonomousTool`.
         *
         * @example
         * agentBuilder().tools(["file_read", "count_pattern"])
         */
        tools(names: string | readonly string[]): this;
        withAutonomousTool(tool: Tool<unknown, unknown>): this;
        withAutonomousTools(tools: readonly Tool<unknown, unknown>[]): this;
        body(fn: (input: TIn, ctx: AgentContext<TState>) => Promise<TOut> | TOut): this;
        onError(handler: (err: ErrorContext) => Promise<ErrorAction> | ErrorAction): this;
        onAgentStart(hook: (ctx: AgentContext<TState>) => Promise<void> | void): this;
        onAgentStop(hook: (ctx: AgentContext<TState>) => Promise<void> | void): this;
        /**
         * Declare that this agent answers dispatched CLI commands. With one argument the handler
         * answers any intent; with two, the first is the intent it answers. The handler receives
         * the command envelope and returns the response payload (string) or
         * `{ success?, payload, error? }`. This is the seam a `.cmd.ts` command reaches by name.
         */
        onCommand(handler: (env: AgentCommandEnvelope) => AgentCommandReply): this;
        onCommand(intent: string, handler: (env: AgentCommandEnvelope) => AgentCommandReply): this;
        when(predicate: () => boolean): this;
        build(): Agent<TIn, TOut>;
    }

    /** Request envelope handed to an `onCommand` handler. */
    interface AgentCommandEnvelope {
        readonly intent: string;
        readonly payload: string;
        readonly from: string;
        readonly correlationId: string;
    }

    /** What an `onCommand` handler may return: a payload string, an object, or a promise of either. */
    type AgentCommandReply =
        | string
        | { success?: boolean; payload?: string; error?: string }
        | Promise<string | { success?: boolean; payload?: string; error?: string }>;

    // LlmConfig is declared once, in llm.d.ts, as what the `llm.*` factories return. A
    // second copy lived here — mutable where the other is readonly, `model` optional where
    // the other requires it — so the two merged into a global interface TypeScript refuses
    // (TS2687 on all six members, TS2717 on `model`). It also documented a shape the runtime
    // discards: JsCrewConfigurationAdapter.ExtractLlmConfig returns null for anything that is
    // not a JsLlmConfig, so a hand-written `{ provider: "openai", model: "x" }` literal was
    // silently ignored while this declaration promised it worked.

    interface ErrorContext {
        readonly error: Error;
        readonly code: ErrorCode;
        readonly attempt: number;
        readonly agentName: string;
    }

    // ErrorAction was declared here as a union of plain object literals
    // (`{ kind: "retry"; afterMs?: number }` and friends). The runtime accepts no such
    // thing: JsCrew.InvokeOnError converts the handler's return value and keeps it only
    // when it is a JsErrorAction instance, i.e. something the global `ErrorAction` factory
    // produced — anything else becomes `fail()`. A handler written against the old
    // declaration therefore compiled, ran, and silently turned every retry into a failed
    // run. What follows is the surface ErrorActionBinding actually registers.

    /**
     * The outcome an `onError` handler returns. Opaque by design: build one with the global
     * {@link ErrorAction} factory below. A plain object is treated as `ErrorAction.fail()`.
     */
    interface ErrorAction {
        readonly kind: "fail" | "retry" | "skip" | "fallback";
    }

    /** The four outcomes an `onError` handler can return. */
    const ErrorAction: {
        /** Give up and fail the run with the original error. */
        fail(): ErrorAction;
        /** Swallow the error and continue with no output for this agent. */
        skip(): ErrorAction;
        /** Swallow the error and use <paramref name="value" /> as the agent's output. */
        fallback(value: unknown): ErrorAction;
        /**
         * Run the body again. `delay` is milliseconds or a duration string ("250ms", "2s");
         * `max` caps the attempts, and reaching it rethrows the original error.
         */
        retry(options?: { delay?: number | string; max?: number }): ErrorAction;
    };

    function agentBuilder<TIn = unknown, TOut = unknown, TState = unknown>(): AgentBuilder<TIn, TOut, TState>;
}

export { };
