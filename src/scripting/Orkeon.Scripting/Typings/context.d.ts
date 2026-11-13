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
        /**
         * One prompt, the answer. A call a stage of the host's decision profile blocks rejects
         * with a `DecisionBlockedError`, as `chat`, `extract`, `decide`, `act` and a `stream`
         * iteration do; uncaught, it fails the run.
         */
        complete(prompt: string, opts?: LlmCallOptions): Promise<string>;
        /** A conversation, the answer. Takes a bare message array or `{ messages: [...] }`. */
        chat(messages: readonly Message[] | MessageList, opts?: LlmCallOptions): Promise<ChatResponse>;
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
        /**
         * Structured JSON matching `schema`. A prompt string, or the forms `chat` takes: a message
         * array or `{ messages: [...] }`, the caller's turns sent in order, so a re-ask can carry the
         * refused answer as an assistant turn:
         *
         * ```ts
         * const again = await ctx.llm.extract<Invoice>({ messages: [
         *     { role: "user", content: prompt },
         *     { role: "assistant", content: refused },
         *     { role: "user", content: "That is not JSON. Answer with the object only." },
         * ] }, schema, { system });
         * ```
         *
         * The JSON instruction rides on `opts.system` when given, else on the last user message (or on
         * the prompt). The schema goes as a `json_schema` constraint when the provider validates one,
         * as `json_object` otherwise; an explicit `responseFormat` wins, `"text"` included.
         */
        extract<T>(prompt: string | readonly Message[] | MessageList, schema: JsonSchema, opts?: ExtractOptions): Promise<T>;
        decide<T extends string>(prompt: string, choices: readonly T[], opts?: LlmCallOptions): Promise<T>;
        /**
         * A deterministic local vector per text, for exercising the API without a model: no provider is
         * called, so `opts` is read by nothing (an unknown `llm.profile` does not throw here).
         */
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
     * The per-call options of the `ctx.llm` methods that send a call (`embed`, a local stub, reads
     * none of them). The call runs on the agent's provider, or
     * on the host profile `llm.profile` names; the response format and the model PATCH that
     * provider's configuration for this call only. There is no per-call vendor, temperature or
     * token cap (GAP-12: they were declared here and never read), and no `signal` — the call
     * already observes the context's.
     */
    interface LlmCallOptions {
        responseFormat?: ResponseFormatType;
        /**
         * The per-call LLM. `profile` runs this call on the host profile of that name
         * (`Llm:Profiles:<name>`), by the rule of `llm.profile(name)`: a name the host does not offer
         * throws, listing the known ones; `"default"` is the host's own provider; a decision profile
         * applies its stages to the call. `model` is the per-call model within the call's provider
         * (the profile's when `profile` is set, else the agent's): the provider keeps its
         * credentials and endpoint — `{ profile, model }` is `llm.profile(profile, { model })`. An
         * `LlmConfig` is not accepted here (it throws): its temperature and token cap would not apply.
         */
        llm?: { profile?: string; model?: string; provider?: never };
        /** Turns the provider's thinking on at this effort for the call. */
        effort?: "low" | "medium" | "high" | "xhigh" | "max";
        /**
         * The prompt-cache request of the call, as `agentBuilder().cache(...)` takes it; it wins over the
         * agent's own. `{}` turns caching off for the call.
         */
        cache?: LlmCacheOptions;
    }

    /** The object form of a conversation: `{ messages: [...] }`. */
    interface MessageList {
        readonly messages: readonly Message[];
    }

    interface ExtractOptions extends LlmCallOptions {
        /**
         * System prompt sent as the first message; the JSON instruction is appended to it and the
         * user content is sent untouched. Omitted: the instruction is appended to the prompt (or to
         * the last user message of the messages form).
         */
        system?: string;
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
         * Context seeded as a user message of its own, after `system` and before the prompt: memory,
         * repository state, earlier turns. The prompt stays the last user message, the request the
         * host's decision profile judges (`guard`) and routes on (`route`), so the context cannot
         * dilute its risk. `system` stays the same from one call to the next and keeps its prompt-cache
         * hit while the context changes. Omitted or blank: no message is added.
         */
        context?: string;
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
         * Called with each fragment of a tool call the model is building (when the provider supports
         * SSE streaming): `index` is the call's position in the turn, `id` and `name` come on the
         * fragment that carries them, `argumentsDelta` is a piece of the JSON arguments. Ollama sends
         * a call whole, as one fragment. When the host enforces DLP, the arguments of each call are held
         * until the call ends and arrive in one fragment, redacted. Keep it cheap.
         */
        onToolCallDelta?: (fragment: { index: number; id?: string; name?: string; argumentsDelta: string }) => void;
        /**
         * Called before each tool call, before the permission gate judges it. Answers `"allow"`
         * (hand the call to the gate), `"ask"` (once the mode admits the call, hand it to the
         * approval channel, as a default-mode edit would; a refusal denies), `{ deny: reason }`
         * (block; the gate is not consulted, the model reads `DENIED: <reason>`) or `{ input }` (the
         * input the gate judges, once, then runs with). A callback that throws denies the call with
         * the exception message. Any other return value (nothing, `null`, a
         * non-object `input`, an unknown shape) denies the call; `"ask"` without a registered
         * permission gate denies too. The `act` call never rejects because of a returned value.
         */
        onToolCall?: (call: { name: string; input: unknown }) =>
            "allow" | "ask" | { deny: string } | { input: unknown }
            | Promise<"allow" | "ask" | { deny: string } | { input: unknown }>;
        /**
         * Runs after each tool call, with the call and its result (the tool's JSON parsed, else
         * its text). A returned `{ context }` replaces the line the model is shown for that
         * result; a void return leaves it. Runs on the script's thread. Through the host's
         * invocation pipeline (the REPL, `orkeon run`) a call the guardian or the permission gate
         * refused (an operator's refusal included) is a result too, `DENIED: …`; without it, and for
         * a call `onToolCall` denied, a refused call never reaches this callback.
         */
        onToolResult?: (call: { name: string; input: unknown }, result: unknown) =>
            void | { context: string } | Promise<void | { context: string }>;
        /**
         * Runs before a model turn once the loop's messages pass `contextPressure.thresholdTokens`
         * (estimated as characters / 4) and older tool results remain to judge (#327). It is handed
         * the results of the read tools that ran (never a write, a command or a refused call; the last
         * `contextPressure.keepRecent` messages are never offered, nor a result a verdict already
         * settled) and answers by call id: `"keep"`, `"head"` (the result cut to its first
         * `contextPressure.headChars` characters) or `"drop"` (the result replaced by a line naming
         * the call). A pruned result always leaves a line that says what was removed and that the
         * call can be run again. A call left out keeps its result. A callback that throws, or a
         * return that is not an object, prunes nothing and the loop goes on. Runs on the script's
         * thread.
         */
        onContextPressure?: (pressure: ContextPressure) =>
            Record<string, ContextPressureVerdict> | void | Promise<Record<string, ContextPressureVerdict> | void>;
        /** When `onContextPressure` runs, and what `"head"` keeps. */
        contextPressure?: {
            /** Estimated tokens of the loop's messages from which the callback runs. Default 100000. */
            thresholdTokens?: number;
            /** Most recent messages (not turns) whose tool results are never offered. Default 4. */
            keepRecent?: number;
            /** Characters a `"head"` verdict keeps. Default 1000. */
            headChars?: number;
        };
        /**
         * Subset of the agent's tools to offer the model for this call. A name the agent does
         * not hold throws `UnknownToolError` before the model is asked. Omitted: all its tools.
         */
        tools?: string[];
    }

    /** What a verdict of `onContextPressure` does to one tool result. */
    type ContextPressureVerdict = "keep" | "head" | "drop";

    /** What `onContextPressure` is handed. */
    interface ContextPressure {
        /** Estimated tokens of the loop's messages (characters / 4). */
        readonly usedTokens: number;
        /** The `contextPressure.thresholdTokens` the loop passed. */
        readonly thresholdTokens: number;
        /** The share of `usedTokens` held by tool results. */
        readonly usedTokensByKind: { readonly tool: number };
        /** The tool calls whose result may be pruned, oldest first. */
        readonly calls: ReadonlyArray<{
            /** The key to answer with: `call-1`, `call-2`... in call order. */
            readonly id: string;
            readonly name: string;
            readonly input: unknown;
            /** Characters of the result the model is shown. */
            readonly resultChars: number;
            /** Model turns since the call ran. */
            readonly age: number;
        }>;
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
         * made one last call that may call no tool (the loop's tools stay declared under
         * `ToolMode.None`; a tool call it returns is never run), telling the model its budget was
         * spent, and `output` is that answer, cut short. When that last call fails or answers nothing, the act rejects with
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
        /** The answer; a provider failure gives the `<undefined-llm:<the provider's error>>` marker, never an empty string, as `complete` does. */
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
