// Orkeon Scripting DSL — Execution context module
// `ExecutionContext` is the surface every agent body and hook receives.
// See chapter 04 (execution-context-and-communication.md).

declare global {
    interface MemoryHit<T = unknown> {
        readonly id: string;
        readonly score: number;
        readonly value: T;
    }

    interface MemoryScope {
        store(key: string, value: unknown): Promise<void>;
        get<T = unknown>(key: string): Promise<T | undefined>;
        search<T = unknown>(query: string, k?: number): Promise<readonly MemoryHit<T>[]>;
        delete(key: string): Promise<void>;
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
         * reasoning reaches `opts.onReasoning` instead of the chunk sequence —
         * it is not part of the answer, and a caller writing chunks to a file
         * must not find it there.
         */
        stream(prompt: string, opts?: LlmStreamOptions): AsyncIterable<string>;
        extract<T>(prompt: string, schema: JsonSchema, opts?: LlmCallOptions): Promise<T>;
        decide<T extends string>(prompt: string, choices: readonly T[], opts?: LlmCallOptions): Promise<T>;
        embed(text: string | readonly string[], opts?: LlmCallOptions): Promise<readonly number[][]>;
        act<T = unknown>(prompt: string, opts?: ActOptions): Promise<T>;
        /** Cancels the in-flight and future llm calls of this context; a running `act()` resolves with `{ interrupted: true }`. */
        interrupt(): void;
        /** Whether `interrupt()` was requested (or the host cancelled the run). */
        readonly isInterrupted: boolean;
    }

    interface LlmCallOptions {
        provider?: string;
        model?: string;
        temperature?: number;
        maxTokens?: number;
        signal?: AbortSignal;
    }

    /** Terminal usage of one streamed call, passed to `LlmStreamOptions.onComplete`. */
    interface LlmStreamUsage {
        readonly tokensUsed: number;
        /**
         * `null` when the provider reported no usage for the stream — which is not
         * the same as zero. Orkéon asks for it (`stream_options.include_usage`),
         * but a provider may ignore the request.
         */
        readonly promptTokens: number | null;
        readonly completionTokens: number | null;
        readonly cacheHitTokens: number | null;
        readonly model: string;
    }

    interface LlmStreamOptions extends LlmCallOptions {
        /**
         * Called with each reasoning delta of a thinking model, in order.
         *
         * Worth wiring on any long call: while the model reasons, the CONTENT
         * stream emits nothing, so a stream that is working looks exactly like a
         * stream that has died. Measured on a 9-minute call whose reasoning was
         * 22 673 of its 32 627 completion tokens — most of the call.
         *
         * Keep it cheap (rendering/logging): it runs sequentially on the same
         * enumeration as the chunks.
         */
        onReasoning?: (delta: string) => void;
        /**
         * Called once when the stream ends, with the terminal usage. Fires on the
         * non-streaming fallback too, so "no callback" and "no usage" stay
         * distinguishable.
         */
        onComplete?: (usage: LlmStreamUsage) => void;
    }

    interface ActOptions extends LlmCallOptions {
        /** Maximum number of tool-call iterations. Default: 10. `0` or `Infinity` = unlimited. */
        maxIterations?: number;
        /**
         * Permission mode gating each autonomous tool call (when the host registered a
         * permission gate). Default: "default" (ask; denied in non-interactive sessions).
         */
        permissionMode?: "default" | "acceptEdits" | "bypassPermissions" | "plan";
        /**
         * Called with each streamed content delta of the assistant turns (when the provider
         * supports SSE streaming). Keep it cheap — rendering/logging only.
         */
        onDelta?: (delta: string) => void;
    }

    interface Message {
        readonly role: "system" | "user" | "assistant" | "tool";
        readonly content: string;
        readonly name?: string;
    }

    interface ChatResponse {
        readonly content: string;
        readonly toolCalls?: readonly ToolCall[];
    }

    interface ToolCall {
        readonly name: string;
        readonly arguments: Record<string, unknown>;
    }

    interface JsonSchema {
        readonly type: string;
        readonly [key: string]: unknown;
    }

    interface ExecutionContext {
        readonly llm: LlmFacade;
        readonly memory: { readonly crew: MemoryScope };
        readonly events: EventBroker;
        readonly signal: AbortSignal;
        readonly log: Logger;
        delegate<T = unknown>(agent: string | Agent<unknown, T>, input: unknown): Promise<T>;
        send(agent: string, message: unknown): Promise<void>;
        receive<T = unknown>(): Promise<T>;
        broadcast(message: unknown): Promise<void>;
    }

    interface AgentContext<TState = unknown> extends ExecutionContext {
        readonly state: Readonly<TState>;
        readonly memory: { readonly agent: MemoryScope; readonly crew: MemoryScope };
        lock<T>(name: string, fn: () => Promise<T>): Promise<T>;
        spawn<TIn, TOut>(builder: AgentBuilder<TIn, TOut, unknown>): Agent<TIn, TOut>;
    }

    interface Logger {
        debug(message: string, ...args: unknown[]): void;
        info(message: string, ...args: unknown[]): void;
        warn(message: string, ...args: unknown[]): void;
        error(message: string, ...args: unknown[]): void;
    }
}

export { };
