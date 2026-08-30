namespace Orkeon.Constants.Llm;

/// <summary>
/// The model each provider runs when a configuration names none.
/// <para>
/// Changing one of these changes the behaviour of every configuration that does not specify a
/// model, so they are written down in one visible place rather than scattered across provider
/// implementations. Each identifier was confirmed on the vendor's own documentation.
/// </para>
/// <para>
/// They live in a satellite (ADR-009) because three components must agree on them: the provider
/// implementations, <c>orkeon init</c>, and Orkeon Studio's connection form. Studio may not
/// reference <c>Orkeon.Infrastructure</c>, so it used to declare its own copy of all twelve and
/// a drift test compared them one by one against the runtime lookup.
/// </para>
/// <para>
/// Azure OpenAI is deliberately absent: it serves deployments an operator named, so there is
/// nothing to default to.
/// </para>
/// </summary>
public static class LlmProviderDefaultModels
{
    /// <summary>
    /// OpenAI — GPT-5.6 Sol, the frontier tier. Also the fallback model of a configuration that
    /// names neither provider nor model, which is why the Domain default reads it from here.
    /// </summary>
    public const string OpenAI = "gpt-5.6-sol";

    /// <summary>Anthropic Claude (the 3.5 generation was retired 2025-10-28).</summary>
    public const string Anthropic = "claude-sonnet-5";

    /// <summary>Ollama, the local server's small default.</summary>
    public const string Ollama = "llama3.2";

    /// <summary>Together AI.</summary>
    public const string Together = "meta-llama/Llama-3.3-70B-Instruct-Turbo";

    /// <summary>DeepSeek (<c>deepseek-chat</c> was retired 2026-07-24).</summary>
    public const string DeepSeek = "deepseek-v4-flash";

    /// <summary>Kimi (Moonshot AI).</summary>
    public const string Kimi = "kimi-k2.6";

    /// <summary>Z.AI (Zhipu GLM).</summary>
    public const string Zai = "glm-5.2";

    /// <summary>Qwen (Alibaba DashScope).</summary>
    public const string Qwen = "qwen3.7-plus";

    /// <summary>Google Gemini, over its OpenAI-compatibility surface.</summary>
    public const string Gemini = "gemini-3.7-flash";

    /// <summary>
    /// Mistral AI — the dated snapshot of the medium 3.5 generation. The previous value,
    /// <c>mistral-medium-3-5-26-04</c>, was never a served identifier: the API answers
    /// <c>Invalid model</c> (measured 2026-08-30), and the string reads like a concatenation
    /// of the two forms Mistral really serves — the alias <c>mistral-medium-3-5</c> and the
    /// vintage <c>2604</c>. Both were verified live the same day; the dated one is pinned,
    /// per this file's convention that a default does not drift under an alias.
    /// </summary>
    public const string Mistral = "mistral-medium-2604";


    /// <summary>
    /// Grok (x.AI) — the current chat flagship, verified live 2026-08-30 with a full 12-mode
    /// campaign (streaming, native tools, reasoning trace, vision, implicit cache).
    /// </summary>
    public const string Grok = "grok-4.6";

    /// <summary>
    /// MiniMax — the current flagship per the vendor's platform documentation (2026-08-30).
    /// UNVERIFIED BY CAMPAIGN: no key has been available; the Mistral lesson
    /// (a compiled default the API never served, found by the first real call) applies in
    /// full until a campaign archives a live M1 on this identifier.
    /// </summary>
    public const string MiniMax = "MiniMax-M2";

    /// <summary>HuggingFace, through the Inference Providers router.</summary>
    public const string HuggingFace = "meta-llama/Llama-3.1-8B-Instruct";

    /// <summary>
    /// Docker Model Runner. Parity with the committed <c>examples/appsettings/appsettings.json</c>,
    /// which <c>orkeon init</c> writes and Studio offers.
    /// </summary>
    public const string DockerModelRunner = "ai/granite-4.0-h-tiny";

    /// <summary>
    /// Docker Model Runner authenticates nothing, but the OpenAI dialect wants a key. This is
    /// what the committed template and <c>orkeon init</c> both write.
    /// </summary>
    public const string DockerModelRunnerApiKeyPlaceholder = "not-needed";
}
