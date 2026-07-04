namespace Orkeon.Infrastructure.Constants.Llm;

/// <summary>
/// Default model names per LLM provider.
/// Centralises model name literals to avoid duplication across provider implementations.
/// </summary>
public static class ProviderDefaults
{
    /// <summary>Default model constants for OpenAI.</summary>
    internal static class OpenAIDefaults
    {
        /// <summary>Default OpenAI model.</summary>
        public const string DefaultModel = "gpt-4";
    }

    /// <summary>Default model constants for Anthropic Claude.</summary>
    internal static class AnthropicDefaults
    {
        /// <summary>Default Anthropic Claude model.</summary>
        public const string DefaultModel = "claude-3-5-sonnet-20241022";
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
        /// <summary>Default DeepSeek model.</summary>
        public const string DefaultModel = "deepseek-chat";
    }

    /// <summary>Default model constants for Kimi (Moonshot AI).</summary>
    internal static class KimiDefaults
    {
        /// <summary>Default Kimi model.</summary>
        public const string DefaultModel = "moonshot-v1-8k";
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
        /// <summary>Default Qwen model.</summary>
        public const string DefaultModel = "qwen-turbo";
    }

    /// <summary>Default model constants for Mistral AI.</summary>
    internal static class MistralDefaults
    {
        /// <summary>Default Mistral AI model.</summary>
        public const string DefaultModel = "mistral-large-latest";
    }
}
