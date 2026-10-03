// Orkeon Scripting DSL — Agent builder
// Declares Agent, AgentBuilder, the onCommand envelope, and the ErrorAction outcomes an onError handler returns.

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
        /**
         * The model this agent runs on: `llm.default_`, `llm.model("…")` or `.with(...)` on
         * either. The provider is the host's — an agent cannot pick another one. A model name or
         * a `{ provider, model }` literal is refused: the runtime used to drop it without a word.
         */
        llm(config: LlmConfig): this;
        /**
         * Forces this agent's output format on the providers that support it. Declarative shape.
         * `"text"` keeps the provider default.
         */
        withResponseFormat(type: ResponseFormatType): this;
        /**
         * Constrains this agent's output to a JSON Schema, on the providers whose API validates
         * one server-side. Implies `json_schema`. `strict` defaults to true. Declarative shape.
         */
        withResponseSchema(name: string, schema: JsonSchema | string, strict?: boolean): this;
        allowDelegation(value: boolean): this;
        /** The turns the agent may take on a task. Default 20 — YAML parity `maxIter:`. Must be 1 or more. */
        maxIterations(value: number): this;
        /**
         * YAML parity `maxRpm:` — the model requests this agent may make per minute, each of its
         * turns on the declarative shape: the request of too many waits its turn, it never fails the
         * task. Bounded too by the host's `RateLimiting:AgentRequestsPerMinute`, the stricter winning.
         * Left out, no limit of its own; must be 1 or more. The procedural shape's `ctx.llm` calls are
         * no agent turns: it applies none, and says so.
         */
        maxRpm(value: number): this;
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
        /**
         * A `toolBuilder()` tool this agent may call: offered to the model by the declarative
         * shape and by `ctx.llm.act` alike, where its `execute` runs in the script. Built-in tools
         * are named with `tools([...])`; anything that is not a built tool is refused.
         */
        withAutonomousTool(tool: Tool<unknown, unknown>): this;
        withAutonomousTools(tools: readonly Tool<unknown, unknown>[]): this;
        body(fn: (input: TIn, ctx: AgentContext<TState>) => Promise<TOut> | TOut): this;
        onError(handler: (err: ErrorContext, ctx: AgentContext<TState>) => Promise<ErrorAction> | ErrorAction): this;
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

    /** What an `onError` handler receives first; the failed attempt's context comes second. */
    interface ErrorContext {
        /** The normalized code of the failure. */
        readonly code: ErrorCode;
        /** The failure's message (the innermost .NET message for a host error). */
        readonly message: string;
        /** The .NET exception behind the failure; a script's own `throw` arrives wrapped in one. */
        readonly exception: unknown;
        /** 1-based number of the attempt that just failed. */
        readonly attempt: number;
        /** The agent whose body failed. */
        readonly agent: { readonly id: string; readonly name: string };
    }

    // ErrorAction was declared here as a union of plain object literals
    // (`{ kind: "retry"; afterMs?: number }` and friends). The runtime accepts no such
    // thing: the run loop's decideAction helper (JsCrew.Run.cs) converts the handler's
    // return value and keeps it only when it is a JsErrorAction instance, i.e. something the
    // global `ErrorAction` factory produced — anything else becomes `fail()`. A handler
    // written against the old declaration therefore compiled, ran, and silently turned every
    // retry into a failed run. What follows is the surface ErrorActionBinding actually registers.

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
