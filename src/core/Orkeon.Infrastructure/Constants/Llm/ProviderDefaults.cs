using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Infrastructure.Constants.Llm;

/// <summary>
/// Default model names per LLM provider.
/// Centralises model name literals to avoid duplication across provider implementations.
/// </summary>
/// <remarks>
/// Every provider's default model lives here (LLM-01) so a single pinning test can assert
/// the whole set: changing a default changes the behaviour of every configuration that does
/// not specify a model, and must therefore be deliberate and visible in review.
/// Each identifier below was confirmed on the vendor's official documentation on 2026-07-27.
/// </remarks>
public static class ProviderDefaults
{
    /// <summary>Default model constants for OpenAI.</summary>
    internal static class OpenAIDefaults
    {
        /// <summary>
        /// Default OpenAI model — GPT-5.6 Sol, the frontier tier.
        /// Aliased onto <see cref="LlmDefaults.DefaultModelName"/>, which is also the
        /// fallback model of <c>LlmConfig</c> itself, so both stay in lockstep.
        /// </summary>
        public const string DefaultModel = LlmDefaults.DefaultModelName;
    }

    /// <summary>Default model constants for Anthropic Claude.</summary>
    internal static class AnthropicDefaults
    {
        /// <summary>Default Anthropic Claude model (the 3.5 generation was retired 2025-10-28).</summary>
        public const string DefaultModel = "claude-sonnet-5";
    }

    /// <summary>Default model constants for Ollama.</summary>
    internal static class OllamaDefaults
    {
        /// <summary>Default Ollama model.</summary>
        public const string DefaultModel = "llama3.2";
    }

    /// <summary>Default model constants for Together AI.</summary>
    internal static class TogetherDefaults
    {
        /// <summary>Default Together AI model.</summary>
        public const string DefaultModel = "meta-llama/Llama-3.3-70B-Instruct-Turbo";
    }

    /// <summary>Default model constants for DeepSeek.</summary>
    internal static class DeepSeekDefaults
    {
        /// <summary>Default DeepSeek model (<c>deepseek-chat</c> was retired 2026-07-24).</summary>
        public const string DefaultModel = "deepseek-v4-flash";
    }

    /// <summary>Default model constants for Kimi (Moonshot AI).</summary>
    internal static class KimiDefaults
    {
        /// <summary>
        /// Default Kimi model. The <c>moonshot-v1-*</c> series sunsets 2026-08-31 and
        /// conflated input and output windows in a single identifier.
        /// </summary>
        public const string DefaultModel = "kimi-k2.6";
    }

    /// <summary>Default model constants for Z.AI (Zhipu GLM).</summary>
    internal static class ZaiDefaults
    {
        /// <summary>Default Z.AI model.</summary>
        public const string DefaultModel = "glm-5.2";
    }

    /// <summary>Default model constants for Qwen (Alibaba DashScope).</summary>
    internal static class QwenDefaults
    {
        /// <summary>Default Qwen model (<c>qwen-turbo</c> is absent from the current catalogue).</summary>
        public const string DefaultModel = "qwen3.7-plus";
    }

    /// <summary>Default model constants for Mistral AI.</summary>
    internal static class MistralDefaults
    {
        /// <summary>Default Mistral AI model — Mistral Medium 3.5, the current frontier generalist.</summary>
        public const string DefaultModel = "mistral-medium-3-5-26-04";
    }

    /// <summary>Default model constants for Groq.</summary>
    internal static class GroqDefaults
    {
        /// <summary>Default Groq model.</summary>
        public const string DefaultModel = "llama-3.3-70b-versatile";
    }

    /// <summary>Default model constants for HuggingFace Inference Providers.</summary>
    internal static class HuggingFaceDefaults
    {
        /// <summary>Default HuggingFace model, routed through Inference Providers.</summary>
        public const string DefaultModel = "meta-llama/Llama-3.1-8B-Instruct";
    }
}
