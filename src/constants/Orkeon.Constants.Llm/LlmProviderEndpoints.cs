namespace Orkeon.Constants.Llm;

/// <summary>
/// The base URL of every LLM provider Orkeon speaks to.
/// <para>
/// They live in a satellite (ADR-009) because three components must agree on them and cannot
/// all reference one another: the provider implementations call them, <c>orkeon init</c> writes
/// them into a settings file, and Orkeon Studio offers them in its connection form. Studio may
/// not reference <c>Orkeon.Infrastructure</c>, so it used to declare its own copy of all twelve
/// and a drift test asserted them one by one.
/// </para>
/// </summary>
public static class LlmProviderEndpoints
{
#pragma warning disable S1075 // URIs should not be hardcoded - these are official, stable public endpoints
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

    /// <summary>
    /// Google Gemini OpenAI-compatible endpoint (Bearer auth with the Gemini API key).
    /// Verified on ai.google.dev/gemini-api/docs/openai, 2026-08-18.
    /// </summary>
    public const string Gemini = "https://generativelanguage.googleapis.com/v1beta/openai";

    /// <summary>Qwen (Alibaba DashScope) OpenAI-compatible endpoint.</summary>
    public const string Qwen = "https://dashscope.aliyuncs.com/compatible-mode/v1";

    /// <summary>
    /// Kimi (Moonshot AI) international API base URL (OpenAI-compatible). The mainland-China
    /// twin is served from <see cref="KimiChinaHost"/>; set it explicitly on the configuration's
    /// base URL when the account is provisioned there, because a key issued for one region is
    /// not accepted by the other.
    /// </summary>
    public const string Kimi = "https://api.moonshot.ai/v1";

    /// <summary>
    /// Host of Kimi's mainland-China endpoint. Separate accounts, separate keys: naming the host
    /// lets a caller recognise which region a configured base URL points at.
    /// </summary>
    public const string KimiChinaHost = "api.moonshot.cn";

    /// <summary>
    /// HuggingFace Inference Providers router (OpenAI-compatible chat completions).
    /// Replaces the retired <c>api-inference.huggingface.co</c> host.
    /// </summary>
    public const string HuggingFace = "https://router.huggingface.co/v1";

    /// <summary>Mistral AI API base URL (OpenAI-compatible).</summary>
    public const string Mistral = "https://api.mistral.ai/v1";

    /// <summary>Z.AI (Zhipu GLM) API base URL (OpenAI-compatible).</summary>
    public const string Zai = "https://api.z.ai/api/paas/v4";

    /// <summary>
    /// x.AI (Grok) API base URL (OpenAI-compatible). Verified live 2026-08-30 — a full
    /// 12-mode campaign through the OpenAI dialect passed against this host before the
    /// provider existed (archived under <c>llmproviders-test/custom-endpoints/</c>).
    /// </summary>
    public const string Grok = "https://api.x.ai/v1";

    /// <summary>
    /// OpenAI's API root as the EMBEDDING clients address it — no <c>/v1</c> suffix and a
    /// trailing slash, because they append their own path. Deliberately distinct from
    /// <see cref="OpenAI"/>, which is the chat base URL: the two are not interchangeable, and
    /// writing one where the other belongs produces a 404 rather than a compile error.
    /// </summary>
    public const string OpenAIEmbeddingsRoot = "https://api.openai.com/";

    /// <summary>Default Ollama local server base URL.</summary>
    public const string OllamaDefault = "http://localhost:11434";

    /// <summary>
    /// Docker Model Runner's llama.cpp OpenAI-compatible endpoint. Not a provider of its own —
    /// it is driven through the OpenAI dialect — but <c>orkeon init</c> writes it, the committed
    /// example settings ship it and Studio offers it, so the three cannot be left to drift.
    /// </summary>
    public const string DockerModelRunner = "http://localhost:12434/engines/llama.cpp/v1";
#pragma warning restore S1075
}
