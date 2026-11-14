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
        /**
         * What the provider and this configuration's model take for reasoning, read from the runtime's capability
         * table (the host's `Llm:ModelCapabilities` overrides included), by the rule every call is judged with.
         * `null` when the provider cannot tell (no provider, a test double).
         */
        readonly capabilities: LlmCapabilities | null;
        /**
         * The effort a call on this configuration asks for and the one the provider sends — the host's
         * `Llm:Thinking:Effort` included. `null` when no effort is asked or the provider cannot tell. The answer
         * `extract` and `complete` cannot carry: read it on the configuration they are given.
         */
        readonly reasoning: LlmReasoning | null;

        /** Returns a copy with the given fields overridden. Any other key is refused. */
        with(overrides: LlmConfigOverrides): LlmConfig;
    }

    /** An effort level a script may ask for. */
    type EffortLevel = "low" | "medium" | "high" | "xhigh" | "max";

    /** What a provider and one of its models take for reasoning, on `LlmConfig.capabilities`. */
    interface LlmCapabilities {
        /** The model judged: the configuration's, else the provider's own. */
        readonly model: string;
        /**
         * Whether the capability table knows the model. When it does not, the provider's levels rule and an effort
         * is sent unchecked.
         */
        readonly known: boolean;
        /**
         * The lower of the provider's and the model's support: `none` (no reasoning pass), `effortOnly` (an effort
         * hint, no off switch), `toggle` (on and off) or `budget` (a token budget as well).
         */
        readonly thinking: "none" | "effortOnly" | "toggle" | "budget";
        /**
         * The levels a call sees sent as asked, in order: the provider's, narrowed by the model's entry. Empty when the
         * model takes no effort or has no reasoning pass. Another level is lowered, or dropped (`LlmReasoning`).
         */
        readonly efforts: EffortLevel[];
    }

    /** What the runtime does with the effort a call asks for: `reasoning` on `LlmConfig`, `ActResult`, `ChatResponse` and `LlmStream`. */
    interface LlmReasoning {
        /** The model the call goes to. */
        readonly model: string;
        /** The effort asked: the call's `effort`, the agent's, the configuration's, or the host's `Llm:Thinking:Effort`. */
        readonly asked: EffortLevel;
        /** The effort sent to the provider; `null` when none is. */
        readonly sent: EffortLevel | null;
        /**
         * `null` when `sent` is `asked` on a model the table knows. Otherwise: `lowered` (the level is not one the
         * provider and the model both take: the highest one below was sent, or the lowest one when none lies below),
         * `dropped` (`sent` is `null`: the model takes no effort, the model or the provider has no thinking control,
         * or the provider's field carries none of the model's levels), or `unchecked` (the table does not know the
         * model: the effort was sent as asked).
         */
        readonly notice: "lowered" | "dropped" | "unchecked" | null;
    }

    /** What `LlmConfig.with(...)` and `llm.model(name, ...)` apply. */
    interface LlmConfigOverrides {
        model?: string;
        temperature?: number;
        /** A positive integer (1 to 2147483647). Omitted keeps the inherited cap; anything else throws. */
        maxTokens?: number;
        responseFormat?: ResponseFormatType;
        /**
         * Turns thinking on at this level; anything else throws. A provider whose field knows fewer levels (three, the
         * default, for Ollama's `think`, Gemini, Azure OpenAI and Mistral; `low` to `xhigh` for OpenAI and Grok; `low`,
         * `medium`, `high` and `max` for Z.ai; all five for Anthropic, OpenRouter, DeepSeek, Kimi, Qwen, Together AI,
         * Hugging Face, MiniMax and Mammouth AI) sends the highest it knows below the one asked, and warns. The model's
         * entry in the capability table narrows the levels further (GLM-5.3 takes `low`, `high` and `max` only):
         * `LlmConfig.capabilities` lists the levels of the model, `reasoning` says what the call sends.
         */
        effort?: EffortLevel;
        /**
         * The prompt-cache request, as `agentBuilder().cache(...)` takes it; an object that marks nothing
         * cacheable (`{}`) turns caching off, the host default `Llm:Cache` included.
         */
        cache?: LlmCacheOptions;
    }

    /**
     * A prompt-cache request (YAML `cache:`). Caching is off unless asked for: a breakpoint changes what the
     * vendor stores and how the call is billed. Anthropic caches only what is marked; other providers
     * cache by their own rules.
     */
    interface LlmCacheOptions {
        /** Marks the system prompt cacheable — usually the largest stable prefix. */
        systemPrompt?: boolean;
        /** Marks the tool definitions cacheable — worth it for a large, stable catalogue. */
        tools?: boolean;
        /**
         * Cache lifetime: `"5m"` (the vendor default) or `"1h"` (billed higher on write). Needs `systemPrompt`
         * or `tools`; on its own it throws.
         */
        ttl?: "5m" | "1h";
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
