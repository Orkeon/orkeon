using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Constants.Llm;

/// <summary>
/// One model provider as <see cref="ModelProviderCatalog"/> lists it.
/// </summary>
/// <param name="Key">The canonical key (<see cref="LlmProviderKeys"/>, <see cref="DecisionProviderKeys"/>).</param>
/// <param name="DisplayName">The name the documentation and the diagnostics show.</param>
/// <param name="Categories">What the provider is called for, at least one.</param>
/// <param name="ApiKeyVariable">
/// The conventional environment variable holding its API key; null when it takes none (a local
/// server) or none is conventional (Azure OpenAI keys belong to a resource).
/// </param>
/// <param name="DefaultBaseUrl">
/// The base URL used when a configuration names none; null when there is no single one
/// (Azure OpenAI is addressed per resource).
/// </param>
[SuppressMessage("Design", "CA1054",
    Justification = "Mirrors the string constants of LlmProviderEndpoints, which settings files carry as text.")]
[SuppressMessage("Design", "CA1056",
    Justification = "Mirrors the string constants of LlmProviderEndpoints, which settings files carry as text.")]
public sealed record ModelProviderDescriptor(
    string Key,
    string DisplayName,
    IReadOnlyList<ModelProviderCategory> Categories,
    string? ApiKeyVariable,
    string? DefaultBaseUrl)
{
    /// <summary>True when the provider is listed in <paramref name="category"/>.</summary>
    public bool Serves(ModelProviderCategory category) => Categories.Contains(category);
}
