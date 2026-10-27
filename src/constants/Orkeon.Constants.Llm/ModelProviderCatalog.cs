namespace Orkeon.Constants.Llm;

/// <summary>
/// Every model provider Orkeon speaks to, with the categories it serves. The single place the
/// documentation counts, the doc-claims gate checks and a diagnostic reads; the contracts stay
/// one per category (<see cref="ModelProviderCategory"/>).
/// <para>
/// Only remote services are listed. A reranker or an embedder running in process (ONNX, the
/// local embedding model) and one built over another category (the listwise LLM reranker, the
/// TypeSafe reranker) are not providers of their own.
/// </para>
/// </summary>
public static class ModelProviderCatalog
{
    private static readonly ModelProviderCategory[] s_generation = [ModelProviderCategory.Generation];
    private static readonly ModelProviderCategory[] s_generationAndEmbedding =
        [ModelProviderCategory.Generation, ModelProviderCategory.Embedding];

    /// <summary>Every provider, generation providers first in <see cref="LlmProviderKeys.All"/> order.</summary>
    public static IReadOnlyList<ModelProviderDescriptor> All { get; } =
    [
        new(LlmProviderKeys.OpenAI, "OpenAI", s_generationAndEmbedding, "OPENAI_API_KEY", LlmProviderEndpoints.OpenAI),
        new(LlmProviderKeys.Ollama, "Ollama", s_generationAndEmbedding, null, LlmProviderEndpoints.OllamaDefault),
        new(LlmProviderKeys.Anthropic, "Anthropic", s_generation, "ANTHROPIC_API_KEY", LlmProviderEndpoints.Anthropic),
        new(LlmProviderKeys.AzureOpenAI, "Azure OpenAI", s_generation, null, null),
        new(LlmProviderKeys.Mistral, "Mistral", s_generation, "MISTRAL_API_KEY", LlmProviderEndpoints.Mistral),
        new(LlmProviderKeys.DeepSeek, "DeepSeek", s_generation, "DEEPSEEK_API_KEY", LlmProviderEndpoints.DeepSeek),
        new(LlmProviderKeys.Kimi, "Kimi (Moonshot)", s_generation, "MOONSHOT_API_KEY", LlmProviderEndpoints.Kimi),
        new(LlmProviderKeys.Qwen, "Qwen (DashScope)", s_generation, "DASHSCOPE_API_KEY", LlmProviderEndpoints.Qwen),
        new(LlmProviderKeys.Together, "Together AI", s_generation, "TOGETHER_API_KEY", LlmProviderEndpoints.Together),
        new(LlmProviderKeys.HuggingFace, "Hugging Face", s_generation, "HF_TOKEN", LlmProviderEndpoints.HuggingFace),
        new(LlmProviderKeys.Zai, "Z.ai (GLM)", s_generation, "ZAI_API_KEY", LlmProviderEndpoints.Zai),
        new(LlmProviderKeys.Gemini, "Gemini", s_generation, "GEMINI_API_KEY", LlmProviderEndpoints.Gemini),
        new(LlmProviderKeys.Grok, "Grok (xAI)", s_generation, "XAI_API_KEY", LlmProviderEndpoints.Grok),
        new(LlmProviderKeys.MiniMax, "MiniMax", s_generation, "MINIMAX_API_KEY", LlmProviderEndpoints.MiniMax),
        new(LlmProviderKeys.OpenRouter, "OpenRouter", s_generation, "OPENROUTER_API_KEY", LlmProviderEndpoints.OpenRouter),
        new(LlmProviderKeys.Mammouth, "Mammouth", s_generation, "MAMMOUTH_API_KEY", LlmProviderEndpoints.Mammouth),
        new(DecisionProviderKeys.TypeSafe, "TypeSafe", [ModelProviderCategory.Decision],
            DecisionProviderDefaults.TypeSafeApiKeyVariable, DecisionProviderDefaults.TypeSafeBaseUrl),
    ];

    /// <summary>The providers serving <paramref name="category"/>, in <see cref="All"/> order.</summary>
    public static IReadOnlyList<ModelProviderDescriptor> InCategory(ModelProviderCategory category) =>
        All.Where(p => p.Serves(category)).ToList();

    /// <summary>
    /// The provider of <paramref name="key"/>, an alias of <see cref="LlmProviderKeys.Aliases"/>
    /// included; null when no provider has that key.
    /// </summary>
    public static ModelProviderDescriptor? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var trimmed = key.Trim();
        var canonical = LlmProviderKeys.Aliases.TryGetValue(trimmed, out var target) ? target : trimmed;
        return All.FirstOrDefault(p => string.Equals(p.Key, canonical, StringComparison.OrdinalIgnoreCase));
    }
}
