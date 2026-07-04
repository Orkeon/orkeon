namespace Orkeon.Infrastructure.Constants.Llm;

/// <summary>
/// Centralised LLM provider base URLs and vector store default endpoints.
/// Eliminates scattered hardcoded URI literals across LLM and memory providers.
/// </summary>
public static class LlmEndpoints
{
    // ── LLM providers ──────────────────────────────────────────────────────

    /// <summary>OpenAI REST API base URL.</summary>
#pragma warning disable S1075 // URIs should not be hardcoded — these are official, stable public endpoints
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

    /// <summary>Kimi (Moonshot AI) API base URL (OpenAI-compatible).</summary>
    public const string Kimi = "https://api.moonshot.cn/v1";

    /// <summary>Mistral AI API base URL (OpenAI-compatible).</summary>
    public const string Mistral = "https://api.mistral.ai/v1";

    /// <summary>Z.AI (Zhipu GLM) API base URL (OpenAI-compatible).</summary>
    public const string Zai = "https://api.z.ai/api/paas/v4";

    // ── Local / self-hosted ─────────────────────────────────────────────────

    /// <summary>Default Ollama local server base URL.</summary>
    public const string OllamaDefault = "http://localhost:11434";

    // ── Vector stores ───────────────────────────────────────────────────────

    /// <summary>Default ChromaDB local server base URL.</summary>
    public const string ChromaDbDefault = "http://localhost:8000";

    /// <summary>Default Redis connection string (host:port).</summary>
    public const string RedisDefault = "localhost:6379";
#pragma warning restore S1075
}
