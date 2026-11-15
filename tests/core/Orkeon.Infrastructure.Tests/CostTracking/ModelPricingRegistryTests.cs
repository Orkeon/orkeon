using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.CostTracking;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.CostTracking;

/// <summary>What <see cref="ModelPricingRegistry"/> prices for a provider and a model.</summary>
public sealed class ModelPricingRegistryTests
{
    private const double NoCacheRead = -1;

    private static ModelPricingRegistry Create(FakeLoggerFactory logs, params ModelPricing[] custom)
    {
        var options = new CostTrackingOptions();
        foreach (var pricing in custom)
        {
            options.CustomPricings.Add(pricing);
        }

        return new ModelPricingRegistry(
            Options.Create(options),
            logs.CreateLogger<ModelPricingRegistry>());
    }

    // Excluded on purpose: Hugging Face (unpriced by decision), OpenRouter (vendor cost),
    // ollama and Docker Model Runner (local, zero cost).
    [Theory]
    [Trait("BR", "CH-08")]
    [InlineData("deepseek", "deepseek-flash", 0.30, NoCacheRead, 1.20)]
    [InlineData("zai", "glm-5.3-flashx", 0.37, 0.075, 1.25)]
    [InlineData("zai", "glm-5.3-flash", 0.15, 0.03, 0.50)]
    [InlineData("zai", "glm-5.3", 1.4, 0.26, 4.4)]
    [InlineData("kimi", "kimi-k3.1-pro", 2, 0.2, 10)]
    [InlineData("kimi", "kimi-k3", 3, NoCacheRead, 15)]
    [InlineData("qwen", "qwen3.7-plus", 0.4, NoCacheRead, 1.6)]
    [InlineData("gemini", "gemini-3.8-flash", 0.75, NoCacheRead, 3.75)]
    [InlineData("gemini", "gemini-3.7-flash", 0.75, NoCacheRead, 3.75)]
    [InlineData("mistral", "mistral-medium-2604", 1.50, NoCacheRead, 7.50)]
    [InlineData("grok", "grok-4.6", 2, 0.5, 6)]
    [InlineData("grok", "grok-4.7", 2, 0.5, 6)]
    [InlineData("minimax", "MiniMax-M3", 0.30, NoCacheRead, 1.20)]
    [InlineData("minimax", "minimax-m3", 0.30, NoCacheRead, 1.20)]
    [InlineData("together", "zai-org/GLM-5.3-Flash", 0.15, NoCacheRead, 0.50)]
    public void ShouldPriceTheDefaultModel_OfEveryPaidProvider(
        string provider, string model, double input, double cacheRead, double output)
    {
        using var logs = new FakeLoggerFactory();
        var pricing = Create(logs).TryGetPricing(provider, model);

        Assert.NotNull(pricing);
        Assert.Equal((decimal)input, pricing.PromptPricePerMillion);
        Assert.Equal((decimal)output, pricing.CompletionPricePerMillion);
        if (cacheRead >= 0)
        {
            Assert.Equal((decimal)cacheRead, pricing.CacheReadPricePerMillion);
        }
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ShouldKeepTheOpenAIAndAnthropicPrices_WhenTheTableGrows()
    {
        using var logs = new FakeLoggerFactory();
        var registry = Create(logs);

        var sol = registry.TryGetPricing("openai", "gpt-5.6-sol");
        var gpt4o = registry.TryGetPricing("openai", "gpt-4o");
        var sonnet = registry.TryGetPricing("anthropic", "claude-sonnet-5");
        var opus = registry.TryGetPricing("anthropic", "claude-opus-4");

        Assert.NotNull(sol);
        Assert.Equal(4.00m, sol.PromptPricePerMillion);
        Assert.Equal(20.00m, sol.CompletionPricePerMillion);
        Assert.NotNull(gpt4o);
        Assert.Equal(2.50m, gpt4o.PromptPricePerMillion);
        Assert.Equal(10.00m, gpt4o.CompletionPricePerMillion);
        Assert.NotNull(sonnet);
        Assert.Equal(2.00m, sonnet.PromptPricePerMillion);
        Assert.Equal(10.00m, sonnet.CompletionPricePerMillion);
        Assert.Equal(0.20m, sonnet.CacheReadPricePerMillion);
        Assert.NotNull(opus);
        Assert.Equal(15.00m, opus.PromptPricePerMillion);
        Assert.Equal(75.00m, opus.CompletionPricePerMillion);
    }

    [Theory]
    [Trait("BR", "CH-11")]
    [InlineData("gemini-3.8-flash")]
    [InlineData("gemini-3.7-flash")]
    public void ShouldPriceAGatewayByItsOwnRow_WhenTheVendorSharesTheModelId(string model)
    {
        using var logs = new FakeLoggerFactory();
        var registry = Create(logs);

        var gateway = registry.TryGetPricing("mammouth", model);
        var vendor = registry.TryGetPricing("gemini", model);

        Assert.NotNull(gateway);
        Assert.Equal(1.5m, gateway.PromptPricePerMillion);
        Assert.Equal(7.5m, gateway.CompletionPricePerMillion);
        Assert.NotNull(vendor);
        Assert.Equal(0.75m, vendor.PromptPricePerMillion);
        Assert.Equal(3.75m, vendor.CompletionPricePerMillion);
    }

    [Theory]
    [Trait("BR", "CH-09")]
    [InlineData("ollama", "llama3.2")]
    [InlineData("ollama", "some-unlisted-local-model")]
    [InlineData("OpenAI", "ai/granite-4.0-h-tiny")]
    public void ShouldCostNothingWithoutWarning_WhenTheProviderIsLocal(string provider, string model)
    {
        using var logs = new FakeLoggerFactory();
        var registry = Create(logs);

        var pricing = registry.TryGetPricing(provider, model);
        var cost = registry.CalculateCost(provider, model, 1_000_000, 1_000_000);

        Assert.NotNull(pricing);
        Assert.Equal(0m, cost);
        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    [Trait("BR", "CH-10")]
    public void ShouldWarnOncePerModel_NamingCustomPricings_WhenAModelHasNoPrice()
    {
        using var logs = new FakeLoggerFactory();
        var registry = Create(logs);

        registry.CalculateCost("openai", "no-such-model-x", 10, 10);
        registry.CalculateCost("no-such-model-x", 10, 10);
        registry.CalculateCost("openai", "no-such-model-x", 10, 10);
        var afterFirst = logs.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        registry.CalculateCost("openai", "no-such-model-y", 10, 10);
        registry.CalculateCost("openai", "no-such-model-x", 10, 10);
        var afterSecond = logs.Entries.Where(e => e.Level == LogLevel.Warning).ToList();

        Assert.Single(afterFirst);
        Assert.Contains("Orkeon:CostTracking:CustomPricings", afterFirst[0].Message, StringComparison.Ordinal);
        Assert.Equal(2, afterSecond.Count);
        Assert.Contains("no-such-model-y", afterSecond[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-11")]
    public void ShouldApplyACustomPricingScopedToAProvider()
    {
        var scoped = new ModelPricing
        {
            ModelPattern = "scoped-custom-model",
            Provider = "x",
            PromptPricePerMillion = 7m,
            CompletionPricePerMillion = 9m,
        };
        using var logs = new FakeLoggerFactory();
        var registry = Create(logs, scoped);

        var forX = registry.TryGetPricing("x", "scoped-custom-model");
        var forOther = registry.TryGetPricing("y", "scoped-custom-model");

        Assert.NotNull(forX);
        Assert.Equal(7m, forX.PromptPricePerMillion);
        Assert.Equal(9m, forX.CompletionPricePerMillion);
        Assert.Null(forOther);
    }
}
