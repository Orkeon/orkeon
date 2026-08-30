namespace Orkeon.Constants.Llm;

/// <summary>
/// The keys that name an LLM provider, as a user writes them.
/// <para>
/// This is the vocabulary of <c>"Provider": "kimi"</c> in a settings file, of
/// <c>orkeon init</c>'s prompts, of Studio's endpoint detector and of the factory switch that
/// turns the answer into a provider. Nothing validates the agreement between them at build time:
/// a key the tooling offers and the factory does not recognise falls through to the OpenAI
/// default, which then talks the OpenAI dialect to whatever endpoint was configured and fails as
/// a remote 4xx rather than as the typo it is.
/// </para>
/// <para>
/// Aliases are part of the contract, not conveniences: a settings file written against
/// <c>togetherai</c> or <c>moonshot</c> keeps working, so every consumer must accept them and
/// none may invent new ones locally.
/// </para>
/// </summary>
public static class LlmProviderKeys
{
    /// <summary>OpenAI.</summary>
    public const string OpenAI = "openai";

    /// <summary>Anthropic.</summary>
    public const string Anthropic = "anthropic";

    /// <summary>A local Ollama server.</summary>
    public const string Ollama = "ollama";

    /// <summary>Azure OpenAI. <see cref="AzureShort"/> is the accepted alias.</summary>
    public const string AzureOpenAI = "azure-openai";

    /// <summary>Alias of <see cref="AzureOpenAI"/>.</summary>
    public const string AzureShort = "azure";


    /// <summary>TogetherAI. <see cref="TogetherAiAlias"/> is the accepted alias.</summary>
    public const string Together = "together";

    /// <summary>Alias of <see cref="Together"/>.</summary>
    public const string TogetherAiAlias = "togetherai";

    /// <summary>Qwen.</summary>
    public const string Qwen = "qwen";

    /// <summary>DeepSeek.</summary>
    public const string DeepSeek = "deepseek";

    /// <summary>Kimi (Moonshot). <see cref="MoonshotAlias"/> is the accepted alias.</summary>
    public const string Kimi = "kimi";

    /// <summary>Alias of <see cref="Kimi"/>.</summary>
    public const string MoonshotAlias = "moonshot";

    /// <summary>Mistral AI.</summary>
    public const string Mistral = "mistral";

    /// <summary>HuggingFace. <see cref="HuggingFaceAlias"/> is the accepted alias.</summary>
    public const string HuggingFace = "huggingface";

    /// <summary>Alias of <see cref="HuggingFace"/>.</summary>
    public const string HuggingFaceAlias = "hf";

    /// <summary>Google Gemini. <see cref="GoogleAlias"/> is the accepted alias.</summary>
    public const string Gemini = "gemini";

    /// <summary>Alias of <see cref="Gemini"/>.</summary>
    public const string GoogleAlias = "google";

    /// <summary>Z.AI (Zhipu GLM). <see cref="GlmAlias"/> and <see cref="ZhipuAlias"/> are accepted aliases.</summary>
    public const string Zai = "zai";

    /// <summary>Alias of <see cref="Zai"/>.</summary>
    public const string GlmAlias = "glm";

    /// <summary>Alias of <see cref="Zai"/>.</summary>
    public const string ZhipuAlias = "zhipu";

    /// <summary>Grok (x.AI). <see cref="XaiAlias"/> is the accepted alias.</summary>
    public const string Grok = "grok";

    /// <summary>MiniMax.</summary>
    public const string MiniMax = "minimax";

    /// <summary>Alias of <see cref="Grok"/> — the vendor's name rather than the model family's.</summary>
    public const string XaiAlias = "xai";

    /// <summary>
    /// Docker Model Runner's llama.cpp OpenAI-compatible endpoint. Detected from a URL rather
    /// than written by a user, and driven through the OpenAI dialect.
    /// </summary>
    public const string DockerModelRunner = "docker-model-runner";

    /// <summary>Reported by a detector when no base URL is configured at all.</summary>
    public const string None = "none";

    /// <summary>
    /// Reported by a detector for an endpoint matching no known host - an OpenAI-compatible
    /// server the runtime will drive through the OpenAI dialect.
    /// </summary>
    public const string Custom = "custom";

    /// <summary>
    /// The fourteen canonical keys, aliases excluded, in the order the documentation lists the
    /// providers. A set rather than fourteen comparisons: a provider added to the factory and
    /// forgotten in the tooling is the omission that pairwise checks do not see.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        OpenAI, Ollama, Anthropic, AzureOpenAI, Mistral,
        DeepSeek, Kimi, Qwen, Together, HuggingFace, Zai, Gemini, Grok, MiniMax,
    ];

    /// <summary>
    /// Every accepted alias mapped to its canonical key. A consumer resolving a user-written key
    /// looks here first; one that does not will reject a settings file the runtime accepts.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Aliases { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AzureShort] = AzureOpenAI,
            [TogetherAiAlias] = Together,
            [MoonshotAlias] = Kimi,
            [HuggingFaceAlias] = HuggingFace,
            [GoogleAlias] = Gemini,
            [GlmAlias] = Zai,
            [ZhipuAlias] = Zai,
            [XaiAlias] = Grok,
        };
}
