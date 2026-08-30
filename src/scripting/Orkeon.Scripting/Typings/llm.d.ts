// Orkeon Scripting DSL — Llm provider namespace
// See chapter 03 §`llm namespace` and chapter 10 §default provider.

declare global {
    /**
     * A resolved LLM configuration — what `llm.openai(...)` and friends return.
     *
     * These used to be declared as non-callable `LlmProvider` objects carrying a `name`
     * property. The runtime exposes them as FACTORIES returning this shape, whose provider
     * field is `provider`, not `name`: every script typed against the old declarations got
     * an error on the correct code and completion on code that cannot run.
     */
    interface LlmConfig {
        readonly provider: string;
        readonly model: string;
        readonly apiKey?: string;
        readonly temperature?: number;
        readonly maxTokens?: number;
        readonly baseUrl?: string;

        /** Returns a copy with the given fields overridden. */
        with(overrides: Partial<Pick<LlmConfig, "model" | "temperature" | "maxTokens" | "baseUrl">>): LlmConfig;
    }

    /** What a call to one of the `llm` factories accepts. */
    interface LlmProviderOptions {
        model?: string;
        temperature?: number;
        maxTokens?: number;
        baseUrl?: string;
    }

    /** A provider factory: called with no argument, it uses that provider's default model. */
    type LlmProvider = (options?: LlmProviderOptions) => LlmConfig;

    namespace llm {
        const openai: LlmProvider;
        const anthropic: LlmProvider;
        const ollama: LlmProvider;
        const azureOpenai: LlmProvider;
        const grok: LlmProvider;

        /**
         * The configured provider, already resolved — a value, not a factory. Comes from
         * `Orkeon:DefaultLlmProvider`; the `UndefinedLlm` echo when nothing is configured.
         */
        const default_: LlmConfig;
    }
}

export { };
