using System.Diagnostics.CodeAnalysis;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Studio.Core.Presets;

/// <summary>
/// The endpoint and model defaults the Orkeon runtime itself uses, copied here on purpose.
/// <para>
/// Studio Core is referenced by three self-contained front-ends. Referencing
/// <c>Orkeon.Infrastructure</c> for a handful of string constants dragged the whole runtime —
/// ONNX runtimes, tree-sitter grammars, the local embedding model — into every published app,
/// for roughly 230 MB each. Copying the constants and pinning them with a test is the trade
/// STUDIO-01 §7 chose: the copy is checked against
/// <c>Orkeon.Infrastructure.Constants.Llm.LlmEndpoints</c>,
/// <c>ProviderDefaults</c> and <c>DockerModelRunnerDefaults</c> by
/// <c>ConstantDriftTests</c> in <c>Orkeon.Studio.Core.Tests</c>, which does reference
/// Infrastructure. Change one side and that test fails.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1054",
    Justification = "These are JSON string field values shown in and edited from text fields, " +
                    "and they are compared verbatim against the runtime's own string constants.")]
public static class OrkeonCliDefaults
{
#pragma warning disable S1075 // URIs should not be hardcoded — official, stable public endpoints (copies of LlmEndpoints)
    /// <summary>OpenAI REST API base URL.</summary>
    public const string OpenAI = "https://api.openai.com/v1";

    /// <summary>Anthropic Messages API base URL.</summary>
    public const string Anthropic = "https://api.anthropic.com";

    /// <summary>Groq API base URL (OpenAI-compatible).</summary>
    public const string Groq = "https://api.groq.com/openai/v1";

    /// <summary>DeepSeek API base URL (OpenAI-compatible).</summary>
    public const string DeepSeek = "https://api.deepseek.com";

    /// <summary>Together AI API base URL (OpenAI-compatible).</summary>
    public const string Together = "https://api.together.xyz/v1";

    /// <summary>Qwen (Alibaba DashScope) OpenAI-compatible endpoint.</summary>
    public const string Qwen = "https://dashscope.aliyuncs.com/compatible-mode/v1";

    /// <summary>Google Gemini OpenAI-compatible endpoint.</summary>
    public const string Gemini = "https://generativelanguage.googleapis.com/v1beta/openai";

    /// <summary>Kimi (Moonshot AI) international API base URL (OpenAI-compatible).</summary>
    public const string Kimi = "https://api.moonshot.ai/v1";

    /// <summary>HuggingFace Inference Providers router (OpenAI-compatible).</summary>
    public const string HuggingFace = "https://router.huggingface.co/v1";

    /// <summary>Mistral AI API base URL (OpenAI-compatible).</summary>
    public const string Mistral = "https://api.mistral.ai/v1";

    /// <summary>Z.AI (Zhipu GLM) API base URL (OpenAI-compatible).</summary>
    public const string Zai = "https://api.z.ai/api/paas/v4";

    /// <summary>Default Ollama local server base URL.</summary>
    public const string OllamaDefault = "http://localhost:11434";

    /// <summary>Docker Model Runner llama.cpp OpenAI-compatible endpoint.</summary>
    public const string DockerModelRunner = "http://localhost:12434/engines/llama.cpp/v1";
#pragma warning restore S1075

    /// <summary>
    /// The mainland-China Moonshot host. It has no <c>LlmEndpoints</c> constant of its own —
    /// it is documented on <c>LlmEndpoints.Kimi</c> — so this one is a copy of a doc comment,
    /// which is why the detector needs it spelled out.
    /// </summary>
    public const string KimiChinaHost = "api.moonshot.cn";

    /// <summary>Ollama's default model (<c>ProviderDefaults.ForProvider("ollama")</c>).</summary>
    public const string OllamaDefaultModel = "llama3.2";

    /// <summary>
    /// OpenAI's default model. Taken straight from the Domain constant the runtime's own
    /// <c>ProviderDefaults</c> reads, so this one cannot drift at all — Studio Core keeps its
    /// Domain reference.
    /// </summary>
    public const string OpenAIDefaultModel = LlmDefaults.DefaultModelName;

    /// <summary>Docker Model Runner default model (<c>DockerModelRunnerDefaults.DefaultModel</c>).</summary>
    public const string DockerModelRunnerDefaultModel = "ai/granite-4.0-h-tiny";

    // Cloud default models — copies of ProviderDefaults.ForProvider(<id>), same drift pinning.

    /// <summary>Anthropic's default model (<c>ProviderDefaults.ForProvider("anthropic")</c>).</summary>
    public const string AnthropicDefaultModel = "claude-sonnet-5";

    /// <summary>DeepSeek's default model (<c>ProviderDefaults.ForProvider("deepseek")</c>).</summary>
    public const string DeepSeekDefaultModel = "deepseek-v4-flash";

    /// <summary>Gemini's default model (<c>ProviderDefaults.ForProvider("gemini")</c>).</summary>
    public const string GeminiDefaultModel = "gemini-3.7-flash";

    /// <summary>Groq's default model (<c>ProviderDefaults.ForProvider("groq")</c>).</summary>
    public const string GroqDefaultModel = "llama-3.3-70b-versatile";

    /// <summary>HuggingFace's default model (<c>ProviderDefaults.ForProvider("huggingface")</c>).</summary>
    public const string HuggingFaceDefaultModel = "meta-llama/Llama-3.1-8B-Instruct";

    /// <summary>Kimi's default model (<c>ProviderDefaults.ForProvider("kimi")</c>).</summary>
    public const string KimiDefaultModel = "kimi-k2.6";

    /// <summary>Mistral's default model (<c>ProviderDefaults.ForProvider("mistral")</c>).</summary>
    public const string MistralDefaultModel = "mistral-medium-3-5-26-04";

    /// <summary>Qwen's default model (<c>ProviderDefaults.ForProvider("qwen")</c>).</summary>
    public const string QwenDefaultModel = "qwen3.7-plus";

    /// <summary>Together AI's default model (<c>ProviderDefaults.ForProvider("together")</c>).</summary>
    public const string TogetherDefaultModel = "meta-llama/Llama-3.3-70B-Instruct-Turbo";

    /// <summary>Z.AI's default model (<c>ProviderDefaults.ForProvider("zai")</c>).</summary>
    public const string ZaiDefaultModel = "glm-5.2";

    /// <summary>Docker Model Runner API key placeholder (<c>DockerModelRunnerDefaults.ApiKeyPlaceholder</c>).</summary>
    public const string DockerModelRunnerApiKeyPlaceholder = "not-needed";
}
