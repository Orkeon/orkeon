// Orkeon Scripting DSL — Llm provider namespace
// See chapter 03 §`llm namespace` and chapter 10 §default provider.

declare global {
    interface LlmProvider {
        readonly name: string;
        with(config: Partial<LlmConfig>): LlmProvider;
    }

    namespace llm {
        const openai: LlmProvider;
        const anthropic: LlmProvider;
        const ollama: LlmProvider;
        const azureOpenai: LlmProvider;
        const groq: LlmProvider;
        /** Resolved through `Orkeon:DefaultLlmProvider`; `UndefinedLlm` echo when not configured. */
        const default_: LlmProvider;
    }
}

export { };
