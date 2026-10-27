// Orkeon Scripting DSL — Execution context module
// `ExecutionContext` is the surface every agent body and hook receives.

declare global {
    /**
     * The run's cancellation handle.
     *
     * NOT a DOM `AbortSignal`, which is what this was declared as until 2026-09-07. There
     * is no DOM in the Jint engine: what a script receives is a .NET `CancellationToken`
     * projected through interop, so `aborted`, `addEventListener` and `throwIfAborted` are
     * all `undefined`. The idiomatic `if (ctx.signal.aborted) return;` therefore compiled,
     * ran, and never once fired -- the most expensive shape a wrong declaration can take.
     *
     * The members below are the ones that exist, CLR-cased because that is how interop
     * projects them. Prefer passing the signal along to awaiting on it: `crew.run()` and
     * the `llm` calls observe it for you.
     */
    interface CancellationSignal {
        /** True once cancellation has been requested for this run. */
        readonly IsCancellationRequested: boolean;
        /** False for a signal that can never be cancelled -- nothing will ever request it. */
        readonly CanBeCanceled: boolean;
    }

    interface MemoryHit<T = unknown> {
        readonly id: string;
        readonly score: number;
        readonly value: T;
    }

    interface MemoryScope {
        store(key: string, value: unknown): Promise<void>;
        get<T = unknown>(key: string): Promise<T | undefined>;
        search<T = unknown>(query: string, k?: number): Promise<readonly MemoryHit<T>[]>;
        /** Resolves to whether the key existed. */
        delete(key: string): Promise<boolean>;
    }

    interface LlmFacade {
        complete(prompt: string, opts?: LlmCallOptions): Promise<string>;
        chat(messages: readonly Message[], opts?: LlmCallOptions): Promise<ChatResponse>;
        /**
         * Streams the completion chunk by chunk: `for await (const c of
         * ctx.llm.stream(prompt))`. Takes a PROMPT, not a message array — the
         * message-array form is `chat(...)`. (This signature said
         * `readonly Message[]` while the runtime has always taken a string.)
         *
         * Per-token when the provider exposes a real SSE path; otherwise a single
         * full-text chunk. A `break` releases the underlying read.
         *
         * Chunks are the model's VISIBLE content only. A thinking model's
         * reasoning is COUNTED on the returned object, not yielded — it is not
         * part of the answer, and a caller writing chunks to a file must not
         * find it there.
         */
        stream(prompt: string, opts?: LlmCallOptions): LlmStream;
        extract<T>(prompt: string, schema: JsonSchema, opts?: LlmCallOptions): Promise<T>;
        decide<T extends string>(prompt: string, choices: readonly T[], opts?: LlmCallOptions): Promise<T>;
        embed(text: string | readonly string[], opts?: LlmCallOptions): Promise<readonly number[][]>;
        /**
         * The LLM ⇄ tool-call loop over the agent's tools: `.tools([...])` built-ins and
         * `withAutonomousTool` instances. Rejects with an `UnknownToolError`, before any model
         * call, when `.tools([...])` names a tool the host does not offer.
         */
        act(prompt: string, opts?: ActOptions): Promise<ActResult>;
        /** Cancels the in-flight and future llm calls of this context; a running `act()` resolves with `{ interrupted: true }`. */
        interrupt(): void;
        /** Whether `interrupt()` was requested (or the host cancelled the run). */
        readonly isInterrupted: boolean;
        /**
         * The host's permission gate, read-only. `enabled` is false when the host registered none
         * (`Orkeon:Security:PermissionGate:Enabled` is not `true`): every tool call of `act()` then
         * runs unchecked, and a `permissionMode` passed to it only logs a warning.
         */
        readonly permissionGate: PermissionGateState;
    }

    /** What `ctx.llm.permissionGate` reports. */
    interface PermissionGateState {
        /** Whether a permission gate checks each tool call of `act()`. */
        readonly enabled: boolean;
    }

    /**
     * The per-call options every `ctx.llm` method reads. Two settings, both PATCHING the host
     * provider's configuration for this call only: the response format, and the model. There is
     * no per-call provider, temperature or token cap (GAP-12: they were declared here and never
     * read), and no `signal` — the call already observes the context's.
     */
    interface LlmCallOptions {
        responseFormat?: ResponseFormatType;
        /** The per-call model: the host's provider keeps its credentials and endpoint. */
        llm?: { model?: string };
        /** Turns the provider's thinking on at this effort for the call. */
        effort?: "low" | "medium" | "high";
    }

    /**
     * What `ctx.llm.stream` returns: the chunk sequence, plus a side-channel for
     * what the chunks cannot carry.
     *
     * Read `usage` and `reasoningChunks` AFTER the loop. They are deliberately
     * properties rather than callbacks: a callback would have to be invoked from
     * the stream's own thread, and the script engine is single-threaded — doing
     * that with several streams in flight corrupts it.
     */
    interface LlmStream extends AsyncIterable<string> {
        /**
         * Terminal token counts, or `null` when the provider reported none —
         * which is NOT the same as a call that used nothing. Orkeon asks for
         * usage (`stream_options.include_usage`); a provider may ignore it.
         * `null` until the stream ends.
         */
        readonly usage: LlmStreamUsage | null;
        /**
         * Reasoning deltas seen so far. Worth reading on a thinking model: while
         * it reasons, the CONTENT stream emits nothing, so a working stream and a
         * dead one look identical. Measured on a nine-minute call whose reasoning
         * was 22 673 of its 32 627 completion tokens — most of the call. The host
         * also logs progress every 200 deltas.
         */
        readonly reasoningChunks: number;
    }

    /** Terminal usage of one streamed call, on `LlmStream.usage`. */
    interface LlmStreamUsage {
        readonly tokensUsed: number;
        /**
         * `null` when the provider reported no usage for the stream — which is not
         * the same as zero. Orkeon asks for it (`stream_options.include_usage`),
         * but a provider may ignore the request.
         */
        readonly promptTokens: number | null;
        readonly completionTokens: number | null;
        readonly cacheHitTokens: number | null;
        readonly model: string;
    }

    interface ActOptions extends LlmCallOptions {
        /** Maximum number of tool-call iterations. Default: 10. `0` or `Infinity` = unlimited. */
        maxIterations?: number;
        /**
         * `"children"`: when the model answers while background children (`ctx.spawnBackground`) are
         * out, wait for their completions and take another turn, until every child has settled or
         * `maxIterations` is reached. The end of the call cancels the children still running.
         */
        waitFor?: "children";
        /**
         * System prompt seeded as the first message of the tool-call conversation.
         * Wins over any `SystemMessage` set in the provider configuration (the
         * conversation-level message has precedence on every provider). Omitted:
         * the conversation starts with the user prompt alone, as before.
         */
        system?: string;
        /**
         * Permission mode gating each autonomous tool call (when the host registered a
         * permission gate: `ctx.llm.permissionGate.enabled`). Default: "default" (ask; denied in
         * non-interactive sessions). Without a gate the mode checks nothing, and the call logs one
         * warning saying so.
         */
        permissionMode?: "default" | "acceptEdits" | "bypassPermissions" | "plan" | "dontAsk";
        /**
         * Called with each streamed content delta of the assistant turns (when the provider
         * supports SSE streaming). Keep it cheap — rendering/logging only.
         */
        onDelta?: (delta: string) => void;
        /**
         * Called before each tool call, after the permission gate allowed it. Answers `"allow"`
         * (run it), `"ask"` (hand the call to the approval channel, as a default-mode edit
         * would; a refusal denies), `{ deny: reason }` (block, the model reads `DENIED: <reason>`),
         * `{ input }` (run with this input, gated again) or `{ context }`. A callback that throws
         * denies the call with the exception message. Any other return value (nothing, `null`, a
         * non-object `input`, an unknown shape) denies the call; `"ask"` without a registered
         * permission gate denies too. The `act` call never rejects because of a returned value.
         */
        onToolCall?: (call: { name: string; input: unknown }) =>
            "allow" | "ask" | { deny: string } | { input: unknown } | { context: string }
            | Promise<"allow" | "ask" | { deny: string } | { input: unknown } | { context: string }>;
        /**
         * Runs after each tool call, with the call and its result (the tool's JSON parsed, else
         * its text). A returned `{ context }` replaces the line the model is shown for that
         * result; a void return leaves it. Runs on the script's thread.
         */
        onToolResult?: (call: { name: string; input: unknown }, result: unknown) =>
            void | { context: string } | Promise<void | { context: string }>;
        /**
         * Subset of the agent's tools to offer the model for this call. A name the agent does
         * not hold throws `UnknownToolError` before the model is asked. Omitted: all its tools.
         */
        tools?: string[];
    }

    /** What `act(...)` resolves to. */
    interface ActResult {
        /** The model's final answer — or a marker when the loop stopped first. */
        readonly output: string;
        /** The turns the loop ran. */
        readonly iterations: number;
        /** Set when `interrupt()` stopped the loop; `output` is then `"(interrupted)"`. */
        readonly interrupted?: true;
        /** Set when `maxIterations` was reached without a final answer. */
        readonly exhausted?: true;
        /**
         * Set when the token budget (`budget({ tokens })`) ran out during the loop: the act then
         * made one last call with no tools, telling the model its budget was spent, and `output` is
         * that answer, cut short. When that last call fails or answers nothing, the act rejects with
         * `BudgetExhaustedError` instead.
         */
        readonly budgetExhausted?: true;
    }

    interface Message {
        readonly role: "system" | "user" | "assistant" | "tool";
        readonly content: string;
        readonly name?: string;
    }

    /** What `chat(...)` resolves to. A tool call is `act(...)`'s business: `chat` offers no tools. */
    interface ChatResponse {
        readonly content: string;
        readonly tokensUsed: number;
        readonly model: string;
    }

    interface JsonSchema {
        readonly type: string;
        readonly [key: string]: unknown;
    }

    interface ExecutionContext {
        readonly llm: LlmFacade;
        readonly memory: { readonly crew: MemoryScope };
        readonly events: EventBroker;
        /**
         * The run's cancellation handle. Pass it on to `crew.run({ signal })` or an
         * `llm` call; that is the whole of its intended use.
         */
        readonly signal: CancellationSignal;
        readonly log: Logger;
        /** A read-only view of the crew this context's agent belongs to. */
        readonly crew: CrewView;
        /**
         * Runs another agent of the crew, by object or by name, exactly as `crew.runAgent` does:
         * under its own context, its semaphore and its `onError` policy. An unknown name throws
         * `AgentNotInThisCrewError`; the calling agent itself, `RecursiveAgentInvocationError`.
         * `opts.budget` bounds the run; `opts.charge: "crew"` also charges it to this context's budget.
         */
        delegate<T = unknown>(agent: string | Agent<unknown, T>, input: unknown, opts?: DelegateOptions): Promise<T>;
        /** Queues `message` for an agent of the crew, by object or by name. Synchronous. */
        send(agent: string | Agent, message: unknown): void;
        /** The next message for this agent. `timeout` (ms or "2s") rejects with `ReceiveTimeoutError`. */
        receive<T = unknown>(options?: { timeout?: number | string }): Promise<T>;
        /** Queues `message` for every other agent of the crew. Synchronous. */
        broadcast(message: unknown): void;
    }

    /** `ctx.crew`: the crew as a context sees it — lookups and the crew-wide named lock. */
    interface CrewView {
        readonly name: string;
        findByName(name: string): Agent | undefined;
        findById(id: string): Agent | undefined;
        findByRole(role: string): readonly Agent[];
        has(agent: Agent | string): boolean;
        /** A named lock shared by every agent of the crew. */
        lock<T>(name: string, fn: () => Promise<T>): Promise<T>;
    }

    /**
     * The agent's state, as the body sees it: a read-only view carrying the ONE mutator.
     *
     * Direct assignment is trapped at runtime -- `ctx.state.count = 1` throws
     * `StateMutationOutsideWithException` rather than quietly working -- because
     * `with()` is what serialises mutations against the agent's state mutex.
     */
    type AgentState<TState> = Readonly<TState> & {
        /**
         * Replaces the state with what `mutate` returns, under the state mutex, and
         * resolves to the new state.
         *
         * REPLACES, not merges: the return value becomes the whole state, so carry the
         * fields you are not changing (`prev => ({ ...prev, count: prev.count + 1 })`).
         * `mutate` may be async; concurrent calls queue rather than interleave.
         */
        with(mutate: (prev: Readonly<TState>) => TState | Promise<TState>): Promise<Readonly<TState>>;
    };

    interface AgentContext<TState = unknown> extends ExecutionContext {
        readonly state: AgentState<TState>;
        readonly memory: { readonly agent: MemoryScope; readonly crew: MemoryScope };
        /**
         * The state mutator, reachable when `state` is `undefined` or `null` and therefore has no
         * `with` to call: `ctx.stateWith(prev => …)` is `ctx.state.with(prev => …)`.
         */
        stateWith(mutate: (prev: Readonly<TState>) => TState | Promise<TState>): Promise<Readonly<TState>>;
        lock<T>(name: string, fn: () => Promise<T>): Promise<T>;
        spawn<TIn, TOut>(builder: AgentBuilder<TIn, TOut, unknown>): Agent<TIn, TOut>;
        /**
         * Starts a crew agent as a child in the background and returns its ticket at once. Its
         * completion reaches the next `ctx.llm.act` turn as a user message, `agent <name> completed: <text>`
         * (`agent <name> failed: <reason>` when it fails). At most `crew.concurrency(n)` children run at once.
         */
        spawnBackground(agent: string, input: string): { readonly id: number; readonly agent: string };
        /** The id (`a1`…) of the agent instance this body runs as (`crew.instances()`); `undefined` outside one. */
        readonly instanceId: string | undefined;
        /**
         * The global `tools`, called as this agent: held to its `.workingRoot(...)` however many agents
         * run beside it. The global `tools` follows the open agents' root while they share one, and
         * refuses a call when they do not: call `ctx.tools` from a body that runs beside others.
         */
        readonly tools: OrkeonTools;
    }

    /** `ctx.log`: extra arguments are joined to the message by a space, objects as JSON. */
    interface Logger {
        debug(message: string, ...args: unknown[]): void;
        info(message: string, ...args: unknown[]): void;
        warn(message: string, ...args: unknown[]): void;
        error(message: string, ...args: unknown[]): void;
    }
}

export { };
