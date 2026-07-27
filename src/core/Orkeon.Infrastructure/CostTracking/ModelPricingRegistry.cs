using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Infrastructure.CostTracking;

/// <summary>
/// Thread-safe registry of model pricing information.
/// Supports exact match, prefix match (longest prefix wins), and custom pricing overrides.
/// </summary>
public sealed partial class ModelPricingRegistry : IModelPricingRegistry
{
    private readonly ConcurrentDictionary<string, ModelPricing> _pricings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ModelPricingRegistry> _logger;

    /// <summary>Initializes a new instance of <see cref="ModelPricingRegistry"/> with default pricings and optional custom overrides.</summary>
    /// <param name="options">The cost tracking options containing custom pricing overrides.</param>
    /// <param name="logger">The logger.</param>
    public ModelPricingRegistry(
        IOptions<CostTrackingOptions> options,
        ILogger<ModelPricingRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        RegisterDefaultPricings();

        var customPricings = options.Value.CustomPricings;
        if (customPricings is { Count: > 0 })
        {
            foreach (var pricing in customPricings)
            {
                RegisterPricing(pricing.ModelPattern, pricing);
            }

            LogRegisteredCustomModelPricings(customPricings.Count);
        }
    }

    /// <inheritdoc />
    public ModelPricing GetPricing(string model)
    {
        var pricing = TryGetPricing(model);
        if (pricing is null)
        {
            throw new KeyNotFoundException($"No pricing found for model '{model}'. Register pricing via RegisterPricing().");
        }

        return pricing;
    }

    /// <inheritdoc />
    public ModelPricing? TryGetPricing(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;

        // 1. Exact match
        if (_pricings.TryGetValue(model, out var exactMatch))
            return exactMatch;

        // 2. Prefix match (longest prefix wins)
        ModelPricing? bestMatch = null;
        var bestLength = 0;

        foreach (var kvp in _pricings)
        {
            if (model.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase) && kvp.Key.Length > bestLength)
            {
                bestMatch = kvp.Value;
                bestLength = kvp.Key.Length;
            }
        }

        return bestMatch;
    }

    /// <inheritdoc />
    public void RegisterPricing(string modelPattern, ModelPricing pricing)
    {
        ArgumentNullException.ThrowIfNull(pricing);
        _pricings[modelPattern] = pricing;
        LogRegisteredPricingForModelPattern(modelPattern, pricing.PromptPricePerMillion, pricing.CompletionPricePerMillion);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ModelPricing> GetAllPricings()
    {
        return new Dictionary<string, ModelPricing>(_pricings, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public decimal CalculateCost(string model, int promptTokens, int completionTokens)
    {
        var pricing = TryGetPricing(model);
        if (pricing is null)
        {
            LogNoPricingFoundForModel(model);
            return 0m;
        }

        var promptCost = promptTokens * pricing.PromptPricePerMillion / 1_000_000m;
        var completionCost = completionTokens * pricing.CompletionPricePerMillion / 1_000_000m;

        return promptCost + completionCost;
    }

    private void RegisterDefaultPricings()
    {
        // OpenAI models
        Register("gpt-5.6-sol", 5.00m, 30.00m);
        Register("gpt-5.6-terra", 2.50m, 15.00m);
        Register("gpt-5.6-luna", 1.00m, 6.00m);
        Register("gpt-4o", 2.50m, 10.00m);
        Register("gpt-4o-mini", 0.15m, 0.60m);
        Register("gpt-4-turbo", 10.00m, 30.00m);
        // LLM-01: "gpt-4" is no longer the platform default but stays priced —
        // existing configurations that pin it must keep producing a real cost.
        Register("gpt-4", 30.00m, 60.00m);
        Register(LlmDefaults.LegacyModelName, 0.50m, 1.50m);
        Register("o1", 15.00m, 60.00m);
        Register("o1-mini", 3.00m, 12.00m);
        Register("o3-mini", 1.10m, 4.40m);

        // Anthropic models
        Register("claude-opus-5", 5.00m, 25.00m);
        Register("claude-sonnet-5", 3.00m, 15.00m);
        Register("claude-opus-4", 15.00m, 75.00m);
        Register("claude-sonnet-4", 3.00m, 15.00m);
        Register("claude-3-opus", 15.00m, 75.00m);
        Register("claude-3.5-sonnet", 3.00m, 15.00m);
        Register("claude-3-sonnet", 3.00m, 15.00m);
        Register("claude-3.5-haiku", 0.80m, 4.00m);
        Register("claude-3-haiku", 0.25m, 1.25m);

        // Embedding models
        RegisterEmbedding("text-embedding-3-small", 0.02m);
        RegisterEmbedding("text-embedding-3-large", 0.13m);
        RegisterEmbedding("text-embedding-ada-002", 0.10m);
    }

    private void Register(string model, decimal promptPricePerMillion, decimal completionPricePerMillion)
    {
        _pricings[model] = new ModelPricing
        {
            ModelPattern = model,
            PromptPricePerMillion = promptPricePerMillion,
            CompletionPricePerMillion = completionPricePerMillion,
            EffectiveDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    private void RegisterEmbedding(string model, decimal embeddingPricePerMillion)
    {
        _pricings[model] = new ModelPricing
        {
            ModelPattern = model,
            PromptPricePerMillion = embeddingPricePerMillion,
            CompletionPricePerMillion = 0m,
            EmbeddingPricePerMillion = embeddingPricePerMillion,
            EffectiveDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Registered {Count} custom model pricings")]
    private partial void LogRegisteredCustomModelPricings(int count);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Registered pricing for model pattern '{ModelPattern}': prompt={PromptPrice}/M, completion={CompletionPrice}/M")]
    private partial void LogRegisteredPricingForModelPattern(string modelPattern, decimal promptPrice, decimal completionPrice);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "No pricing found for model '{Model}', returning zero cost")]
    private partial void LogNoPricingFoundForModel(string model);

}
