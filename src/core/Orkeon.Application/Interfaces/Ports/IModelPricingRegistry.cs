using Orkeon.Domain.Constants.Platform;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Pricing information for a specific LLM model.
/// </summary>
public record ModelPricing
{
    /// <summary>
    /// Model name or pattern (e.g., "gpt-4o", "claude-3-opus").
    /// </summary>
    public string ModelPattern { get; init; } = string.Empty;

    /// <summary>
    /// Cost per million prompt (input) tokens in the specified currency.
    /// </summary>
    public decimal PromptPricePerMillion { get; init; }

    /// <summary>
    /// Cost per million completion (output) tokens in the specified currency.
    /// </summary>
    public decimal CompletionPricePerMillion { get; init; }

    /// <summary>
    /// Cost per million embedding tokens (null if not an embedding model).
    /// </summary>
    public decimal? EmbeddingPricePerMillion { get; init; }

    /// <summary>
    /// Currency for pricing (default: USD).
    /// </summary>
    public string Currency { get; init; } = PlatformDefaults.DefaultCurrency;

    /// <summary>
    /// Date when this pricing became effective.
    /// </summary>
    public DateTime EffectiveDate { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Registry for model pricing information, used to calculate costs from token usage.
/// </summary>
public interface IModelPricingRegistry
{
    /// <summary>
    /// Gets pricing for the specified model. Throws if no pricing found.
    /// </summary>
    ModelPricing GetPricing(string model);

    /// <summary>
    /// Tries to get pricing for the specified model. Returns null if not found.
    /// </summary>
    ModelPricing? TryGetPricing(string model);

    /// <summary>
    /// Registers pricing for a model pattern.
    /// </summary>
    void RegisterPricing(string modelPattern, ModelPricing pricing);

    /// <summary>
    /// Gets all registered pricings.
    /// </summary>
    IReadOnlyDictionary<string, ModelPricing> GetAllPricings();

    /// <summary>
    /// Calculates the cost for the given token usage on the specified model.
    /// </summary>
    decimal CalculateCost(string model, int promptTokens, int completionTokens);
}
