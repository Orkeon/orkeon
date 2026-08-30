using Orkeon.Constants.Llm;

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
    /// <summary>
    /// Default model per provider key, using the same keys as
    /// <c>LlmProviderFactory.Create(string, LlmConfig)</c>.
    /// </summary>
    /// <remarks>
    /// Without this, a caller holding only a provider key — the campaign harness, for one — has
    /// no way to ask "what would this provider run by default?" and ends up sending OpenAI's
    /// default model to Groq. Exposing the lookup rather than the constants keeps the nested
    /// classes internal and keeps this file the single place a default is written down.
    /// </remarks>
    private static readonly Dictionary<string, string> ByProviderKey = new(StringComparer.OrdinalIgnoreCase)
    {
        [LlmProviderKeys.OpenAI] = LlmProviderDefaultModels.OpenAI,
        [LlmProviderKeys.Anthropic] = LlmProviderDefaultModels.Anthropic,
        [LlmProviderKeys.Ollama] = LlmProviderDefaultModels.Ollama,
        [LlmProviderKeys.Groq] = LlmProviderDefaultModels.Groq,
        [LlmProviderKeys.Together] = LlmProviderDefaultModels.Together,
        [LlmProviderKeys.TogetherAiAlias] = LlmProviderDefaultModels.Together,
        [LlmProviderKeys.DeepSeek] = LlmProviderDefaultModels.DeepSeek,
        [LlmProviderKeys.Kimi] = LlmProviderDefaultModels.Kimi,
        [LlmProviderKeys.MoonshotAlias] = LlmProviderDefaultModels.Kimi,
        [LlmProviderKeys.Qwen] = LlmProviderDefaultModels.Qwen,
        [LlmProviderKeys.Mistral] = LlmProviderDefaultModels.Mistral,
        [LlmProviderKeys.HuggingFace] = LlmProviderDefaultModels.HuggingFace,
        [LlmProviderKeys.HuggingFaceAlias] = LlmProviderDefaultModels.HuggingFace,
        [LlmProviderKeys.Gemini] = LlmProviderDefaultModels.Gemini,
        [LlmProviderKeys.GoogleAlias] = LlmProviderDefaultModels.Gemini,
        [LlmProviderKeys.Grok] = LlmProviderDefaultModels.Grok,
        [LlmProviderKeys.XaiAlias] = LlmProviderDefaultModels.Grok,
        [LlmProviderKeys.Zai] = LlmProviderDefaultModels.Zai,
        [LlmProviderKeys.GlmAlias] = LlmProviderDefaultModels.Zai,
        [LlmProviderKeys.ZhipuAlias] = LlmProviderDefaultModels.Zai,
    };

    /// <summary>Resolves a provider's default model from its factory key.</summary>
    /// <param name="providerKey">Provider key, e.g. <c>groq</c>.</param>
    /// <returns>
    /// The default model, or <see langword="null"/> when the provider has none — Azure OpenAI
    /// serves deployments an operator named, so there is nothing to default to.
    /// </returns>
    public static string? ForProvider(string providerKey) =>
        ByProviderKey.GetValueOrDefault(providerKey ?? "");
}
