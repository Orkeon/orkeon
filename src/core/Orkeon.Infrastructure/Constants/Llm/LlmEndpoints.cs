using Orkeon.Constants.Llm;

namespace Orkeon.Infrastructure.Constants.Llm;

/// <summary>
/// LLM provider base URLs and vector store default endpoints, as this assembly publishes them.
/// <para>
/// The provider URLs are no longer written here: they come from
/// <see cref="LlmProviderEndpoints"/> (ADR-009), which is also what Orkeon Studio reads. These
/// names stay because they are a shipped public surface; their value now has one home. The two
/// vector-store entries below are not shared with anything and remain declared here.
/// </para>
/// </summary>
public static class LlmEndpoints
{
    // ── LLM providers ──────────────────────────────────────────────────────

    /// <summary>OpenAI REST API base URL.</summary>
#pragma warning disable S1075 // URIs should not be hardcoded — these are official, stable public endpoints
    public const string OpenAI = LlmProviderEndpoints.OpenAI;

    /// <summary>Anthropic Messages API base URL.</summary>
    public const string Anthropic = LlmProviderEndpoints.Anthropic;

    /// <summary>Groq API base URL (OpenAI-compatible).</summary>
    public const string Groq = LlmProviderEndpoints.Groq;

    /// <summary>DeepSeek API base URL (OpenAI-compatible).</summary>
    public const string DeepSeek = LlmProviderEndpoints.DeepSeek;

    /// <summary>Together AI API base URL (OpenAI-compatible).</summary>
    public const string Together = LlmProviderEndpoints.Together;

    /// <summary>
    /// Google Gemini OpenAI-compatible endpoint (Bearer auth with the Gemini API key).
    /// Verified on ai.google.dev/gemini-api/docs/openai, 2026-08-18.
    /// </summary>
    public const string Gemini = LlmProviderEndpoints.Gemini;

    /// <summary>Qwen (Alibaba DashScope) OpenAI-compatible endpoint.</summary>
    public const string Qwen = LlmProviderEndpoints.Qwen;

    /// <summary>
    /// Kimi (Moonshot AI) international API base URL (OpenAI-compatible).
    /// The mainland-China twin is <c>https://api.moonshot.cn/v1</c>; set it explicitly
    /// via <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmConfig.BaseUrl"/> when
    /// the account is provisioned there.
    /// </summary>
    public const string Kimi = LlmProviderEndpoints.Kimi;

    /// <summary>
    /// HuggingFace Inference Providers router (OpenAI-compatible chat completions).
    /// Replaces the retired <c>api-inference.huggingface.co</c> host.
    /// </summary>
    public const string HuggingFace = LlmProviderEndpoints.HuggingFace;

    /// <summary>Mistral AI API base URL (OpenAI-compatible).</summary>
    public const string Mistral = LlmProviderEndpoints.Mistral;

    /// <summary>Z.AI (Zhipu GLM) API base URL (OpenAI-compatible).</summary>
    public const string Zai = LlmProviderEndpoints.Zai;

    // ── Local / self-hosted ─────────────────────────────────────────────────

    /// <summary>Default Ollama local server base URL.</summary>
    public const string OllamaDefault = LlmProviderEndpoints.OllamaDefault;

    // ── Vector stores ───────────────────────────────────────────────────────

    /// <summary>Default ChromaDB local server base URL.</summary>
    public const string ChromaDbDefault = "http://localhost:8000";

    /// <summary>Default Redis connection string (host:port).</summary>
    public const string RedisDefault = "localhost:6379";
#pragma warning restore S1075
}
