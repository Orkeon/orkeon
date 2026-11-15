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
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ModelPricing>> _providerPricings = new(StringComparer.OrdinalIgnoreCase);
    // The user's CustomPricings rows, in their own tables: they are consulted before any built-in row.
    private readonly ConcurrentDictionary<string, ModelPricing> _customPricings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ModelPricing>> _customProviderPricings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.OrdinalIgnoreCase);
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
                RowsFor(_customPricings, _customProviderPricings, pricing.Provider)[pricing.ModelPattern] = pricing;
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
        => string.IsNullOrWhiteSpace(model) ? null : Lookup(_customPricings, model, matchAll: false) ?? Lookup(_pricings, model, matchAll: false);

    /// <inheritdoc />
    public ModelPricing? TryGetPricing(string? provider, string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;

        // A custom row beats a built-in one; within each tier, the provider's own rows come first.
        return LookupScoped(_customProviderPricings, provider, model)
            ?? Lookup(_customPricings, model, matchAll: false)
            ?? LookupScoped(_providerPricings, provider, model)
            ?? Lookup(_pricings, model, matchAll: false);
    }

    /// <inheritdoc />
    public decimal CalculateCost(string? provider, string model, int promptTokens, int completionTokens)
        => Cost(TryGetPricing(provider, model), model, promptTokens, completionTokens);

    private static ModelPricing? LookupScoped(ConcurrentDictionary<string, ConcurrentDictionary<string, ModelPricing>> tables, string? provider, string model)
        => provider is not null && tables.TryGetValue(provider, out var rows) ? Lookup(rows, model, matchAll: true) : null;

    // matchAll: an empty pattern matches every model. Only a provider's own rows allow it (ollama prices every
    // model at 0); a provider-less row with an empty pattern would price every unknown model and hide its warning.
    private static ModelPricing? Lookup(ConcurrentDictionary<string, ModelPricing> rows, string model, bool matchAll)
    {
        // 1. Exact match
        if (rows.TryGetValue(model, out var exactMatch))
            return exactMatch;

        // 2. Prefix match (longest prefix wins)
        ModelPricing? bestMatch = null;
        var bestLength = matchAll ? -1 : 0;

        foreach (var kvp in rows)
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
        RowsFor(_pricings, _providerPricings, pricing.Provider)[modelPattern] = pricing;
        LogRegisteredPricingForModelPattern(modelPattern, pricing.PromptPricePerMillion, pricing.CompletionPricePerMillion);
    }

    /// <summary>Lists the provider-less rows in effect, custom rows included; rows scoped to one provider are not part of it.</summary>
    /// <returns>The provider-less rows in effect by model pattern.</returns>
    public IReadOnlyDictionary<string, ModelPricing> GetAllPricings()
    {
        var rows = new Dictionary<string, ModelPricing>(_pricings, StringComparer.OrdinalIgnoreCase);
        foreach (var custom in _customPricings)
            rows[custom.Key] = custom.Value;
        return rows;
    }

    /// <inheritdoc />
    public decimal CalculateCost(string model, int promptTokens, int completionTokens)
        => Cost(TryGetPricing(model), model, promptTokens, completionTokens);

    private decimal Cost(ModelPricing? pricing, string model, int promptTokens, int completionTokens)
    {
        if (pricing is null)
        {
            if (_warned.TryAdd(model, 0))
                LogNoPricingFoundForModel(model);
            return 0m;
        }

        var promptCost = promptTokens * pricing.PromptPricePerMillion / 1_000_000m;
        var completionCost = completionTokens * pricing.CompletionPricePerMillion / 1_000_000m;

        return promptCost + completionCost;
    }

    private static ConcurrentDictionary<string, ModelPricing> RowsFor(
        ConcurrentDictionary<string, ModelPricing> providerless,
        ConcurrentDictionary<string, ConcurrentDictionary<string, ModelPricing>> byProvider,
        string? provider)
        => provider is null
            ? providerless
            : byProvider.GetOrAdd(provider, _ => new(StringComparer.OrdinalIgnoreCase));

    private void RegisterDefaultPricings()
    {
        // OpenAI models — vendor pricing page read 2026-09-19. Sol is 4 / 20 "at least
        // through November 21, 2026" (the post-promotion price is not published); Terra and
        // Luna are their list prices. GPT-6 Astra bills input beyond 272K tokens twice — a
        // flat registry cannot express that, so a long-context call there is under-counted.
        Register("gpt-6-astra", 10.00m, 50.00m);
        // The bare "gpt-5.6" alias points at Sol; register it so the alias is priced too.
        // Prefix matching takes the longest key, so the three explicit variants still win.
        Register("gpt-5.6", 4.00m, 20.00m);
        Register("gpt-5.6-sol", 4.00m, 20.00m);
        Register("gpt-5.6-terra", 2.00m, 12.00m);
        Register("gpt-5.6-luna", 0.20m, 1.20m);
        Register("gpt-4o", 2.50m, 10.00m);
        Register("gpt-4o-mini", 0.15m, 0.60m);
        Register("gpt-4-turbo", 10.00m, 30.00m);
        // LLM-01: "gpt-4" is no longer the platform default but stays priced —
        // existing configurations that pin it must keep producing a real cost.
        Register("gpt-4", 30.00m, 60.00m);
        Register("gpt-3.5-turbo", 0.50m, 1.50m);
        Register("o1", 15.00m, 60.00m);
        Register("o1-mini", 3.00m, 12.00m);
        Register("o3-mini", 1.10m, 4.40m);

        // Anthropic models — vendor pricing page read 2026-10-30 (#257). Every entry carries the
        // prompt-cache rates: a write at 1.25x the prompt rate (the 5-minute TTL; the 1-hour share
        // of a write is costed at 2x by the cost manager),
        // a read at 0.1x the prompt rate unless the page states another figure. Sonnet 5's
        // introductory 2 / 10 is its standard price; Fable 5.1 and Fable 5 share the top tier.
        RegisterAnthropic("claude-fable-5-1", 10.00m, 50.00m, cacheReadPricePerMillion: 0.25m);
        RegisterAnthropic("claude-fable-5", 10.00m, 50.00m);
        // Opus 5.5 sits below the Opus 5 tier, so the prefix would overcharge it.
        RegisterAnthropic("claude-opus-5-5", 4.00m, 20.00m, cacheReadPricePerMillion: 0.20m);
        RegisterAnthropic("claude-opus-5", 5.00m, 25.00m);
        // Sonnet 5.5 and Sonnet 5 share 2 / 10 and the 0.20 read: the prefix holds.
        RegisterAnthropic("claude-sonnet-5", 2.00m, 10.00m);
        // Haiku 5.5: the rate of a prompt up to 100K tokens; a longer prompt bills 0.50 / 2.50,
        // which the registry does not model.
        RegisterAnthropic("claude-haiku-5", 0.10m, 0.50m);
        RegisterAnthropic("claude-haiku-4-5", 1.00m, 5.00m);
        // Opus 4.6, 4.7 and 4.8 are priced at the Opus 5 tier, below Opus 4 and 4.1.
        RegisterAnthropic("claude-opus-4-8", 5.00m, 25.00m);
        RegisterAnthropic("claude-opus-4-7", 5.00m, 25.00m);
        RegisterAnthropic("claude-opus-4-6", 5.00m, 25.00m);
        // Opus 4.5 is priced at the Opus 5 tier, below Opus 4 and 4.1.
        RegisterAnthropic("claude-opus-4-5", 5.00m, 25.00m);
        RegisterAnthropic("claude-opus-4", 15.00m, 75.00m);
        RegisterAnthropic("claude-sonnet-4", 3.00m, 15.00m);
        RegisterAnthropic("claude-3-opus", 15.00m, 75.00m);
        RegisterAnthropic("claude-3.5-sonnet", 3.00m, 15.00m);
        RegisterAnthropic("claude-3-sonnet", 3.00m, 15.00m);
        RegisterAnthropic("claude-3.5-haiku", 0.80m, 4.00m);
        RegisterAnthropic("claude-3-haiku", 0.25m, 1.25m);

        // Paid providers' default models, per provider so a gateway's row never shadows the vendor's.
        // deepseek: peak rate, https://api-docs.deepseek.com/quick_start/pricing read 2026-11-15 (cache read 0.006).
        RegisterFor("deepseek", "deepseek-flash", 0.30m, 0.006m, 1.20m);
        // zai: docs.z.ai pricing; glm-5.3 is a prefix of the others, the longest prefix wins.
        RegisterFor("zai", "glm-5.3-flashx", 0.37m, 0.075m, 1.25m);
        RegisterFor("zai", "glm-5.3-flash", 0.15m, 0.03m, 0.50m);
        RegisterFor("zai", "glm-5.3", 1.4m, 0.26m, 4.4m);
        // kimi: Alibaba Model Studio (k3.1-pro) and the comparison doc (k3).
        RegisterFor("kimi", "kimi-k3.1-pro", 2m, 0.2m, 10m);
        RegisterFor("kimi", "kimi-k3", 3m, null, 15m);
        // qwen: Alibaba Cloud model pricing, Singapore, limited-time 20% off; above 256K input is not modelled.
        RegisterFor("qwen", "qwen3.7-plus", 0.4m, null, 1.6m);
        // gemini-3.8-flash: https://ai.google.dev/gemini-api/docs/pricing read 2026-11-15; the 1.50 / 7.50 rate
        // Google applies from 2027-01-01, kept as the conservative figure for budgets.
        RegisterFor("gemini", "gemini-3.8-flash", 1.50m, 0.15m, 7.50m);
        // gemini-3.7-flash: comparison doc, not on the vendor page, so no cache-read price.
        RegisterFor("gemini", "gemini-3.7-flash", 0.75m, null, 3.75m);
        RegisterFor("mistral", "mistral-medium-2604", 1.50m, null, 7.50m);
        // grok: comparison doc; the 4 / 12 rate above 200K is not modelled.
        RegisterFor("grok", "grok-4.6", 2m, 0.5m, 6m);
        RegisterFor("grok", "grok-4.7", 2m, 0.5m, 6m);
        RegisterFor("minimax", "MiniMax-M3", 0.30m, null, 1.20m);
        RegisterFor("together", "zai-org/GLM-5.3-Flash", 0.15m, null, 0.50m);
        // mammouth: the gateway's own row, its upper bound.
        RegisterFor("mammouth", "gemini-3.8-flash", 1.5m, null, 7.5m);
        RegisterFor("mammouth", "gemini-3.7-flash", 1.5m, null, 7.5m);
        // Local providers cost nothing: ollama prices any model at 0; Docker Model Runner
        // (provider OpenAI) serves the Docker Hub "ai/" namespace.
        RegisterFor("ollama", string.Empty, 0m, null, 0m);
        Register("ai/", 0m, 0m);

        // Embedding models
        RegisterEmbedding("text-embedding-3-small", 0.02m);
        RegisterEmbedding("text-embedding-3-large", 0.13m);
        RegisterEmbedding("text-embedding-ada-002", 0.10m);
    }

    private void RegisterFor(
        string provider,
        string model,
        decimal promptPricePerMillion,
        decimal? cacheReadPricePerMillion,
        decimal completionPricePerMillion)
    {
        RowsFor(_pricings, _providerPricings, provider)[model] = new ModelPricing
        {
            ModelPattern = model,
            Provider = provider,
            PromptPricePerMillion = promptPricePerMillion,
            CompletionPricePerMillion = completionPricePerMillion,
            CacheReadPricePerMillion = cacheReadPricePerMillion,
            EffectiveDate = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc)
        };
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

    /// <summary>
    /// Registers an Anthropic model with its prompt-cache rates: a write costs 1.25x the prompt rate
    /// (the 5-minute TTL), a read 0.1x unless the vendor states another figure.
    /// </summary>
    private void RegisterAnthropic(
        string model,
        decimal promptPricePerMillion,
        decimal completionPricePerMillion,
        decimal? cacheReadPricePerMillion = null)
    {
        _pricings[model] = new ModelPricing
        {
            ModelPattern = model,
            PromptPricePerMillion = promptPricePerMillion,
            CompletionPricePerMillion = completionPricePerMillion,
            CacheWritePricePerMillion = promptPricePerMillion * 1.25m,
            CacheReadPricePerMillion = cacheReadPricePerMillion ?? promptPricePerMillion * 0.10m,
            EffectiveDate = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc)
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

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "No pricing found for model '{Model}', returning zero cost; add a price under Orkeon:CostTracking:CustomPricings")]
    private partial void LogNoPricingFoundForModel(string model);

}
