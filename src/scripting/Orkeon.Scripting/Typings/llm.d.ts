// Orkeon Scripting DSL — Llm provider namespace
// Declares LlmConfig and the `llm` namespace: `llm.default_` and `llm.model(...)`.
//
// There is no per-vendor factory (GAP-12). Every agent of a script talks to the provider the
// host registered; `llm.openai()`, `llm.anthropic()` and their six siblings only renamed it —
// `llm.anthropic()` on an OpenAI host sent `claude-haiku-4-5` to OpenAI. What a script sets is
// the MODEL, and the settings below.

declare global {
    /** A provider response format, for `withResponseFormat` and the `responseFormat` options. */
    type ResponseFormatType = "text" | "json_object" | "json_schema";

    /**
     * The model settings an agent runs with — what `llm.default_`, `llm.model(...)` and
     * `with(...)` return, and the only value `agentBuilder().llm(...)` accepts.
     */
    interface LlmConfig {
        /** The host's provider, which every agent talks to. Informational: a script cannot change it. */
        readonly provider: string;
        readonly model: string;
        readonly temperature?: number;
        readonly maxTokens?: number;

        /** Returns a copy with the given fields overridden. Any other key is refused. */
        with(overrides: LlmConfigOverrides): LlmConfig;
    }

    /** What `LlmConfig.with(...)` and `llm.model(name, ...)` apply. */
    interface LlmConfigOverrides {
        model?: string;
        temperature?: number;
        maxTokens?: number;
        responseFormat?: ResponseFormatType;
    }

    namespace llm {
        /**
         * The host's provider on its own configured model — a value, not a factory. The
         * `<undefined-llm>` echo (provider `"undefined"`) when the host has no provider.
         */
        const default_: LlmConfig;

        /** `llm.default_` on another model: `llm.default_.with({ model: name, ...overrides })`. */
        function model(name: string, overrides?: Omit<LlmConfigOverrides, "model">): LlmConfig;
    }
}

export { };
