using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.CostTracking;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.CostTracking;

/// <summary>
/// What a vendor billed for one call, read back from the metadata the provider wrote
/// (STUDIO-29). Absent means unknown — never free — and a billed 0 is a free call that must
/// stay 0 all the way to the wire.
/// </summary>
public sealed class LlmVendorCostTests
{
    private static LlmResponse Billed(object cost, string? currency = null)
    {
        var metadata = new Dictionary<string, object> { ["cost"] = cost };
        if (currency is not null)
            metadata["cost_currency"] = currency;
        return new LlmResponse { Content = "ok", Metadata = metadata };
    }

    private static CostBudgetManager NewManager(ModelPricingRegistry? registry = null) =>
        new(registry ?? new ModelPricingRegistry(Options.Create(new CostTrackingOptions()), NullLogger<ModelPricingRegistry>.Instance),
            Options.Create(new CostTrackingOptions()), NullLogger<CostBudgetManager>.Instance);

    [Fact]
    [Trait("BR", "RT-12")]
    public async Task ShouldAccumulateCacheWriteAndReadPerModel_WhenMeteredCallsComplete()
    {
        var sink = new MockLlmUsageSink();
        var mock = new MockLlmProvider();
        var metered = MeteredLlmProvider.Wrap(mock, sink);
        var ct = TestContext.Current.CancellationToken;

        async Task Call(string model, int prompt, int completion, int write, int read)
        {
            mock.CacheWriteTokens = write;
            mock.CacheHitTokens = read;
            mock.SetGenerateResult(new LlmResponse { Content = "ok", Model = model, PromptTokens = prompt, CompletionTokens = completion, TokensUsed = prompt + completion });
            await metered.GenerateAsync("p", cancellationToken: ct);
        }

        await Call("unpriced-model-a", 200, 20, 100, 40);
        await Call("unpriced-model-a", 150, 10, 50, 10);
        await Call("unpriced-model-b", 70, 5, 7, 3);

        var manager = NewManager();
        foreach (var e in sink.Recorded)
            manager.RecordUsage(e);
        var report = manager.GetReport();

        var a = report.ModelUsageByModel["unpriced-model-a"];
        Assert.Equal((350L, 30L, 150L, 50L), (a.PromptTokens, a.CompletionTokens, a.CacheWriteTokens, a.CacheReadTokens));
        var b = report.ModelUsageByModel["unpriced-model-b"];
        Assert.Equal((70L, 5L, 7L, 3L), (b.PromptTokens, b.CompletionTokens, b.CacheWriteTokens, b.CacheReadTokens));
        Assert.Equal(2, report.ModelUsageByModel.Count);
        Assert.True(report.ByModel.ContainsKey("unpriced-model-a"));
        Assert.True(report.ByModel.ContainsKey("unpriced-model-b"));
        Assert.Equal(2, report.ByModel.Count);
    }

    [Fact]
    [Trait("BR", "RT-12")]
    public void ShouldPriceCacheWriteAndRead_WhenPricingDeclaresCacheRates()
    {
        var registry = new ModelPricingRegistry(Options.Create(new CostTrackingOptions()), NullLogger<ModelPricingRegistry>.Instance);
        registry.RegisterPricing("cache-model", new ModelPricing
        {
            ModelPattern = "cache-model",
            PromptPricePerMillion = 3m,
            CompletionPricePerMillion = 15m,
            CacheWritePricePerMillion = 3.75m,
            CacheReadPricePerMillion = 0.30m,
        });
        registry.RegisterPricing("plain-model", new ModelPricing
        {
            ModelPattern = "plain-model",
            PromptPricePerMillion = 3m,
            CompletionPricePerMillion = 15m,
        });
        var manager = NewManager(registry);

        CostUsageEvent Event(string model) => new()
        {
            Model = model, PromptTokens = 1000, CompletionTokens = 500, CacheWriteTokens = 200, CacheHitTokens = 300,
        };
        manager.RecordUsage(Event("cache-model"));
        manager.RecordUsage(Event("plain-model"));
        var report = manager.GetReport();

        // PromptTokens is inclusive of the cache tokens: (1000-200-300)*3/1M + 200*3.75/1M + 300*0.30/1M + 500*15/1M
        // = 0.0015 + 0.00075 + 0.00009 + 0.0075
        Assert.Equal(0.00984m, report.ByModel["cache-model"]);
        // No cache rates declared: prompt and completion only, as before.
        Assert.Equal(0.0105m, report.ByModel["plain-model"]);
    }

    [Fact]
    public void The_charge_the_provider_wrote_is_read_as_billed_with_its_currency()
    {
        Assert.True(LlmVendorCost.TryRead(Billed(0.00042, "USD"), out var cost, out var currency));

        Assert.Equal(0.00042m, cost);
        Assert.Equal("USD", currency);
    }

    [Fact]
    public void A_free_call_is_billed_zero_and_stays_zero()
    {
        Assert.True(LlmVendorCost.TryRead(Billed(0.0, "USD"), out var cost, out _));

        Assert.Equal(0m, cost);
    }

    [Fact]
    public void A_response_the_vendor_did_not_bill_has_no_charge()
    {
        Assert.False(LlmVendorCost.TryRead(new LlmResponse { Content = "ok" }, out _, out var currency));

        Assert.Null(currency);
    }

    [Fact]
    public void A_charge_whose_currency_nobody_stated_is_read_without_one()
    {
        Assert.True(LlmVendorCost.TryRead(Billed(0.0125), out var cost, out var currency));

        Assert.Equal(0.0125m, cost);
        Assert.Null(currency);
    }

    [Fact]
    public void A_value_that_is_not_an_amount_is_no_charge()
    {
        // A decimal cannot hold these, and a meter must not fail the call it observes.
        Assert.False(LlmVendorCost.TryRead(Billed(double.NaN, "USD"), out _, out _));
        Assert.False(LlmVendorCost.TryRead(Billed(1e300, "USD"), out _, out _));
        Assert.False(LlmVendorCost.TryRead(Billed("0.01", "USD"), out _, out _));
    }
}
