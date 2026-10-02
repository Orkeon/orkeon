// Orkeon Scripting DSL — Llm provider namespace
// Declares LlmConfig and the `llm` namespace: `llm.default_`, `llm.model(...)` and
// `llm.profile(...)`.
//
// There is no per-vendor factory (GAP-12): `llm.openai()`, `llm.anthropic()` and their six
// siblings only renamed the host's provider — `llm.anthropic()` on an OpenAI host sent
// `claude-haiku-4-5` to OpenAI. A script picks a provider the host CONFIGURED, by the name of
// one of its profiles (`Llm:Profiles:<name>`, GAP-17), and sets the model and the settings below.

declare global {
    /** A provider response format, for `withResponseFormat` and the `responseFormat` options. */
    type ResponseFormatType = "text" | "json_object" | "json_schema";

    /**
     * The model settings an agent runs with — what `llm.default_`, `llm.model(...)` and
     * `with(...)` return, and the only value `agentBuilder().llm(...)` accepts.
     */
    interface LlmConfig {
        /** The provider this configuration talks to — the host's default one, or its profile's. Informational. */
        readonly provider: string;
        /** The host profile picked with `llm.profile(...)`; absent on the default profile. */
        readonly profile?: string;
        /**
         * The model the agent runs on. Empty when the host's provider configures none: the
         * agent then runs on that provider's own default model.
         */
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

        /**
         * One of the host's named profiles (`Llm:Profiles:<name>`), on its own model unless
         * `overrides.model` names another. An agent configured with it runs on that profile's
         * provider — its turns and its `ctx.llm` calls. `"default"` is `llm.default_`. A name the
         * host does not offer throws, listing the known profiles.
         */
        function profile(name: string, overrides?: LlmConfigOverrides): LlmConfig;
    }
}

export { };
