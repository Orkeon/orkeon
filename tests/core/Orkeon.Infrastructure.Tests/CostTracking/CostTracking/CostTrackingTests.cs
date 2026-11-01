using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.CostTracking;
using Orkeon.Infrastructure.DependencyInjection;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests;

public class ModelPricingRegistryTests
{
    private readonly ModelPricingRegistry _sut;

    public ModelPricingRegistryTests()
    {
        _sut = new ModelPricingRegistry(
            Options.Create(new CostTrackingOptions()),
            NullLogger<ModelPricingRegistry>.Instance);
    }

    [Fact]
    public void ShouldReturnPricing_WhenGetPricingExactMatch()
    {
        var pricing = _sut.GetPricing(ModelGpt4o);

        Assert.NotNull(pricing);
        Assert.Equal(2.50m, pricing.PromptPricePerMillion);
        Assert.Equal(10.00m, pricing.CompletionPricePerMillion);
    }

    [Fact]
    public void ShouldReturnPricing_WhenGetPricingCaseInsensitiveExactMatch()
    {
        var pricing = _sut.GetPricing("GPT-4o");

        Assert.NotNull(pricing);
        Assert.Equal(2.50m, pricing.PromptPricePerMillion);
    }

    [Fact]
    public void ShouldReturnLongestPrefixPricing_WhenGetPricingPrefixMatch()
    {
        // "gpt-4o-2024-08-06" should prefix-match "gpt-4o" (not "gpt-4")
        var pricing = _sut.GetPricing("gpt-4o-2024-08-06");

        Assert.NotNull(pricing);
        Assert.Equal(2.50m, pricing.PromptPricePerMillion);
        Assert.Equal(10.00m, pricing.CompletionPricePerMillion);
    }

    [Fact]
    public void ShouldThrowKeyNotFoundException_WhenGetPricingNoMatch()
    {
        Assert.Throws<KeyNotFoundException>(() => _sut.GetPricing("unknown-model"));
    }

    [Fact]
    public void ShouldReturnPricing_WhenTryGetPricingExactMatch()
    {
        var pricing = _sut.TryGetPricing(ModelGpt4oMini);

        Assert.NotNull(pricing);
        Assert.Equal(0.15m, pricing.PromptPricePerMillion);
    }

    [Fact]
    public void ShouldReturnNull_WhenTryGetPricingNoMatch()
    {
        var pricing = _sut.TryGetPricing("unknown-model");

        Assert.Null(pricing);
    }

    [Fact]
    public void ShouldReturnNull_WhenTryGetPricingEmptyString()
    {
        Assert.Null(_sut.TryGetPricing(""));
    }

    [Fact]
    public void ShouldReturnNull_WhenTryGetPricingNullString()
    {
        Assert.Null(_sut.TryGetPricing(null!));
    }

    [Fact]
    public void ShouldBeAbleToBeRetrieved_WhenRegisterPricingCustomModel()
    {
        var customPricing = new ModelPricing
        {
            ModelPattern = "my-custom-model",
            PromptPricePerMillion = 5.00m,
            CompletionPricePerMillion = 15.00m
        };

        _sut.RegisterPricing("my-custom-model", customPricing);

        var retrieved = _sut.GetPricing("my-custom-model");
        Assert.Equal(5.00m, retrieved.PromptPricePerMillion);
        Assert.Equal(15.00m, retrieved.CompletionPricePerMillion);
    }

    [Fact]
    public void ShouldUseNewPricing_WhenRegisterPricingOverrideExisting()
    {
        var customPricing = new ModelPricing
        {
            ModelPattern = ModelGpt4o,
            PromptPricePerMillion = 1.00m,
            CompletionPricePerMillion = 3.00m
        };

        _sut.RegisterPricing(ModelGpt4o, customPricing);

        var retrieved = _sut.GetPricing(ModelGpt4o);
        Assert.Equal(1.00m, retrieved.PromptPricePerMillion);
        Assert.Equal(3.00m, retrieved.CompletionPricePerMillion);
    }

    [Fact]
    public void ShouldCalculateCorrectly_WhenCalculateCostGpt4o()
    {
        // 1000 prompt tokens at $2.50/M + 500 completion tokens at $10.00/M
        var cost = _sut.CalculateCost(ModelGpt4o, 1000, 500);

        var expected = (1000 * 2.50m / 1_000_000m) + (500 * 10.00m / 1_000_000m);
        Assert.Equal(expected, cost);
    }

    [Fact]
    public void ShouldCalculateCorrectly_WhenCalculateCostGpt4oMini()
    {
        var cost = _sut.CalculateCost(ModelGpt4oMini, 10000, 5000);

        var expected = (10000 * 0.15m / 1_000_000m) + (5000 * 0.60m / 1_000_000m);
        Assert.Equal(expected, cost);
    }

    [Fact]
    public void ShouldReturnZero_WhenCalculateCostUnknownModel()
    {
        var cost = _sut.CalculateCost("unknown-model", 1000, 500);

        Assert.Equal(0m, cost);
    }

    [Fact]
    public void ShouldReturnZero_WhenCalculateCostZeroTokens()
    {
        var cost = _sut.CalculateCost(ModelGpt4o, 0, 0);

        Assert.Equal(0m, cost);
    }

    [Fact]
    public void ShouldCalculateCorrectly_WhenCalculateCostClaudeModels()
    {
        var cost = _sut.CalculateCost("claude-3-opus", 1000, 500);

        var expected = (1000 * 15.00m / 1_000_000m) + (500 * 75.00m / 1_000_000m);
        Assert.Equal(expected, cost);
    }

    [Fact]
    public void ShouldReturnDefaultPricings_WhenGetAllPricings()
    {
        var all = _sut.GetAllPricings();

        Assert.NotEmpty(all);
        Assert.True(all.Count >= 10); // At least 10 default models
        Assert.True(all.ContainsKey(ModelGpt4o));
        Assert.True(all.ContainsKey("claude-3-opus"));
        Assert.True(all.ContainsKey("text-embedding-3-small"));
    }

    [Fact]
    public void ShouldEmbeddingModelsHaveEmbeddingPrice_WhenDefaultPricings()
    {
        var pricing = _sut.GetPricing("text-embedding-3-small");

        Assert.NotNull(pricing.EmbeddingPricePerMillion);
        Assert.Equal(0.02m, pricing.EmbeddingPricePerMillion);
    }

    [Fact]
    public void ShouldRegisterThem_WhenConstructorWithCustomPricings()
    {
        var options = new CostTrackingOptions
        {
            CustomPricings =
            {
                new ModelPricing
                {
                    ModelPattern = "my-org-model",
                    PromptPricePerMillion = 7.00m,
                    CompletionPricePerMillion = 21.00m
                }
            }
        };

        var registry = new ModelPricingRegistry(
            Options.Create(options),
            NullLogger<ModelPricingRegistry>.Instance);

        var pricing = registry.GetPricing("my-org-model");
        Assert.Equal(7.00m, pricing.PromptPricePerMillion);
    }

    [Theory]
    [InlineData(ModelGpt4Turbo, 10.00, 30.00)]
    [InlineData(ModelGpt35Turbo, 0.50, 1.50)]
    [InlineData("claude-3.5-sonnet", 3.00, 15.00)]
    [InlineData("claude-3-haiku", 0.25, 1.25)]
    public void ShouldCommonModelsHaveExpectedPrices_WhenDefaultPricings(
        string model, double expectedPrompt, double expectedCompletion)
    {
        var pricing = _sut.GetPricing(model);

        Assert.Equal((decimal)expectedPrompt, pricing.PromptPricePerMillion);
        Assert.Equal((decimal)expectedCompletion, pricing.CompletionPricePerMillion);
    }

    /// <summary>#257 - every Anthropic 5.x model has a price, read through the prefix or its own entry, with the cache rates.</summary>
    [Trait("BR", "CH-257")]
    [Theory]
    [InlineData("claude-haiku-5-5", 0.10, 0.50, 0.125, 0.01)]
    [InlineData("claude-haiku-5", 0.10, 0.50, 0.125, 0.01)]
    [InlineData("claude-sonnet-5-5", 2.00, 10.00, 2.50, 0.20)]
    [InlineData("claude-opus-5-5", 4.00, 20.00, 5.00, 0.20)]
    [InlineData("claude-opus-5", 5.00, 25.00, 6.25, 0.50)]
    [InlineData("claude-fable-5-1", 10.00, 50.00, 12.50, 0.25)]
    [InlineData("claude-opus-4-8", 5.00, 25.00, 6.25, 0.50)]
    [InlineData("claude-opus-4-1", 15.00, 75.00, 18.75, 1.50)]
    public void An_Anthropic_model_declares_its_prompt_and_cache_rates(
        string model, double prompt, double completion, double cacheWrite, double cacheRead)
    {
        var pricing = _sut.GetPricing(model);

        Assert.Equal((decimal)prompt, pricing.PromptPricePerMillion);
        Assert.Equal((decimal)completion, pricing.CompletionPricePerMillion);
        Assert.Equal((decimal)cacheWrite, pricing.CacheWritePricePerMillion);
        Assert.Equal((decimal)cacheRead, pricing.CacheReadPricePerMillion);
    }

    /// <summary>#257 - no Anthropic entry is left without the cache rates: a cached call would pay the full prompt rate.</summary>
    [Trait("BR", "CH-257")]
    [Fact]
    public void Every_Anthropic_default_pricing_declares_both_cache_rates()
    {
        var anthropic = _sut.GetAllPricings().Values.Where(p => p.ModelPattern.StartsWith("claude-", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(anthropic);
        Assert.All(anthropic, p =>
        {
            Assert.NotNull(p.CacheWritePricePerMillion);
            Assert.NotNull(p.CacheReadPricePerMillion);
        });
    }
}

public class CostBudgetManagerTests
{
    private readonly CostBudgetManager _sut;
    private readonly ModelPricingRegistry _pricingRegistry;

    public CostBudgetManagerTests()
    {
        _pricingRegistry = new ModelPricingRegistry(
            Options.Create(new CostTrackingOptions()),
            NullLogger<ModelPricingRegistry>.Instance);

        _sut = new CostBudgetManager(
            _pricingRegistry,
            Options.Create(new CostTrackingOptions()),
            NullLogger<CostBudgetManager>.Instance);
    }

    [Trait("BR", "R-42")]
    [Fact]
    public void ShouldPriceOpus45_WhenModelIdIsDated()
    {
        var pricing = _pricingRegistry.TryGetPricing("claude-opus-4-5-20251101");

        Assert.NotNull(pricing);
        Assert.Equal(5.00m, pricing.PromptPricePerMillion);
        Assert.Equal(25.00m, pricing.CompletionPricePerMillion);
        Assert.Equal(6.25m, pricing.CacheWritePricePerMillion);
        Assert.Equal(0.50m, pricing.CacheReadPricePerMillion);
    }

    [Trait("BR", "R-47")]
    [Fact]
    public void ShouldCostOneHourCacheWriteAtTwiceInput_WhenUsageReportsEphemeral1h()
    {
        // claude-sonnet-4: input 3.00 -> 5-minute write 3.75, one-hour write 6.00 per MTok.
        var result = _sut.RecordUsage(new CostUsageEvent
        {
            Model = "claude-sonnet-4",
            PromptTokens = 10_000,
            CompletionTokens = 0,
            CacheWriteTokens = 10_000,
            CacheWrite1hTokens = 4_000
        });

        // 6000 * 3.75 + 4000 * 6.00 = 46500 per million
        Assert.Equal(0.0465m, result.Cost);
    }

    /// <summary>#257 - a Haiku 5.5 call is priced, and its cache tokens at the cache rates, not the prompt rate.</summary>
    [Trait("BR", "CH-257")]
    [Fact]
    public void A_cached_Haiku_5_5_call_costs_the_cache_rates_and_is_priced()
    {
        _sut.RecordUsage(new CostUsageEvent
        {
            Model = "claude-haiku-5-5",
            PromptTokens = 10_000,
            CompletionTokens = 1_000,
            CacheWriteTokens = 4_000,
            CacheHitTokens = 5_000
        });

        var report = _sut.GetReport();

        var usage = report.ModelUsageByModel["claude-haiku-5-5"];
        Assert.True(usage.Priced);
        Assert.Equal(0, usage.UnpricedCalls);
        Assert.Equal(0, report.TotalUsage.UnpricedCalls);
        var expected = (1_000 * 0.10m + 4_000 * 0.125m + 5_000 * 0.01m + 1_000 * 0.50m) / 1_000_000m;
        Assert.Equal(expected, usage.Cost);
    }

    /// <summary>#226 - the report keeps the reasoning tokens by model and says which models carry a price.</summary>
    [Trait("BR", "CH-226")]
    [Fact]
    public void The_report_by_model_carries_reasoning_tokens_and_whether_the_model_is_priced()
    {
        _sut.RecordUsage(new CostUsageEvent { Model = ModelGpt4o, PromptTokens = 1000, CompletionTokens = 500, ReasoningTokens = 120 });
        _sut.RecordUsage(new CostUsageEvent { Model = ModelGpt4o, PromptTokens = 10, CompletionTokens = 5, ReasoningTokens = 3 });
        _sut.RecordUsage(new CostUsageEvent { Model = "local-unpriced-model", PromptTokens = 10, CompletionTokens = 5 });

        var report = _sut.GetReport();

        var priced = report.ModelUsageByModel[ModelGpt4o];
        Assert.Equal(123, priced.ReasoningTokens);
        Assert.True(priced.Priced);
        var unpriced = report.ModelUsageByModel["local-unpriced-model"];
        Assert.Null(unpriced.ReasoningTokens);
        Assert.False(unpriced.Priced);
    }

    /// <summary>
    /// #226 - the report's totals count every call, one whose provider named no model included, though the
    /// per-model rows leave it out.
    /// </summary>
    [Trait("BR", "CH-226")]
    [Fact]
    public void The_total_usage_counts_a_call_whose_provider_named_no_model()
    {
        _sut.RecordUsage(new CostUsageEvent { Model = ModelGpt4o, PromptTokens = 1000, CompletionTokens = 500, CacheHitTokens = 200, ReasoningTokens = 20 });
        _sut.RecordUsage(new CostUsageEvent { Model = string.Empty, PromptTokens = 40, CompletionTokens = 8, CacheHitTokens = 10, CacheWriteTokens = 5, ReasoningTokens = 2 });

        var report = _sut.GetReport();

        Assert.Equal([ModelGpt4o], report.ModelUsageByModel.Keys);
        var total = report.TotalUsage;
        Assert.Equal(2, report.TotalCalls);
        Assert.Equal(2, total.Calls);
        Assert.Equal(1040, total.PromptTokens);
        Assert.Equal(508, total.CompletionTokens);
        Assert.Equal(210, total.CacheReadTokens);
        Assert.Equal(5, total.CacheWriteTokens);
        Assert.Equal(22, total.ReasoningTokens);
        Assert.Equal(1, total.UnpricedCalls);
        Assert.True(total.Priced);
    }

    /// <summary>#254 - a cache or reasoning count no call reported stays null, never 0.</summary>
    [Trait("BR", "CH-254")]
    [Fact]
    public void ShouldKeepUnreportedCountsNull_WhenNoCallReportedThem()
    {
        _sut.RecordUsage(new CostUsageEvent { Model = ModelGpt4o, PromptTokens = 100, CompletionTokens = 10 });
        _sut.RecordUsage(new CostUsageEvent { Model = ModelGpt4o, PromptTokens = 50, CompletionTokens = 5 });

        var report = _sut.GetReport();

        foreach (var usage in new[] { report.ModelUsageByModel[ModelGpt4o], report.TotalUsage })
        {
            Assert.Equal(150, usage.PromptTokens);
            Assert.Null(usage.CacheReadTokens);
            Assert.Null(usage.CacheWriteTokens);
            Assert.Null(usage.ReasoningTokens);
        }
    }

    /// <summary>#254 - the calls that reported a count add up; the ones that did not add nothing.</summary>
    [Trait("BR", "CH-254")]
    [Fact]
    public void ShouldSumReportedCountsOnly_WhenSomeCallsReportedNone()
    {
        _sut.RecordUsage(new CostUsageEvent { Model = ModelGpt4o, PromptTokens = 100, CompletionTokens = 10 });
        _sut.RecordUsage(new CostUsageEvent { Model = ModelGpt4o, PromptTokens = 50, CompletionTokens = 5, CacheHitTokens = 5, CacheWriteTokens = 0, ReasoningTokens = 3 });

        var usage = _sut.GetReport().ModelUsageByModel[ModelGpt4o];

        Assert.Equal(5, usage.CacheReadTokens);
        Assert.Equal(0, usage.CacheWriteTokens);
        Assert.Equal(3, usage.ReasoningTokens);
    }

    /// <summary>#226 - the report counts the calls nobody priced, in each model and in the totals.</summary>
    [Trait("BR", "CH-226")]
    [Fact]
    public void The_report_counts_the_unpriced_calls_of_a_mix_of_priced_and_unpriced_models()
    {
        _sut.RecordUsage(new CostUsageEvent { Model = ModelGpt4o, PromptTokens = 1000, CompletionTokens = 500 });
        _sut.RecordUsage(new CostUsageEvent { Model = "local-unpriced-model", PromptTokens = 10, CompletionTokens = 5 });
        _sut.RecordUsage(new CostUsageEvent { Model = "local-unpriced-model", PromptTokens = 10, CompletionTokens = 5 });
        _sut.RecordUsage(new CostUsageEvent { Model = "vendor/model", PromptTokens = 10, CompletionTokens = 5, Cost = 0.01m });
        _sut.RecordUsage(new CostUsageEvent { Model = "vendor/model", PromptTokens = 10, CompletionTokens = 5 });

        var report = _sut.GetReport();

        Assert.Equal(0, report.ModelUsageByModel[ModelGpt4o].UnpricedCalls);
        Assert.Equal(1, report.ModelUsageByModel[ModelGpt4o].Calls);
        Assert.Equal(2, report.ModelUsageByModel["local-unpriced-model"].UnpricedCalls);
        Assert.False(report.ModelUsageByModel["local-unpriced-model"].Priced);
        var vendor = report.ModelUsageByModel["vendor/model"];
        Assert.Equal(2, vendor.Calls);
        Assert.Equal(1, vendor.UnpricedCalls);
        Assert.True(vendor.Priced);
        Assert.Equal(5, report.TotalUsage.Calls);
        Assert.Equal(3, report.TotalUsage.UnpricedCalls);
    }

    /// <summary>#226 - a vendor charge prices the model, 0 included: a free model is priced at 0.</summary>
    [Trait("BR", "CH-226")]
    [Fact]
    public void A_vendor_charge_prices_a_model_the_registry_does_not_know()
    {
        var result = _sut.RecordUsage(new CostUsageEvent { Model = "vendor/free-model", PromptTokens = 10, CompletionTokens = 5, Cost = 0m });

        Assert.Equal(0m, result.Cost);
        Assert.True(_sut.GetReport().ModelUsageByModel["vendor/free-model"].Priced);
    }

    /// <summary>#226 - RecordUsage answers the cost it settled, so a caller can credit it elsewhere.</summary>
    [Trait("BR", "CH-226")]
    [Fact]
    public void Record_usage_answers_the_settled_cost_and_null_when_nobody_priced_the_call()
    {
        var priced = _sut.RecordUsage(new CostUsageEvent { Model = ModelGpt4o, PromptTokens = 1000, CompletionTokens = 500 });
        var unpriced = _sut.RecordUsage(new CostUsageEvent { Model = "local-unpriced-model", PromptTokens = 1000, CompletionTokens = 500 });

        Assert.Equal(_pricingRegistry.CalculateCost(ModelGpt4o, 1000, 500), priced.Cost);
        Assert.Null(unpriced.Cost);
    }

    [Fact]
    public void A_call_nobody_priced_is_estimated_from_the_price_registry()
    {
        // No vendor cost (null) is an unknown cost: the manager prices it for its own
        // accounting, and only then.
        var evt = new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            PromptTokens = 1000,
            CompletionTokens = 500,
        };

        var result = _sut.RecordUsage(evt);

        Assert.True(result.IsWithinBudget);

        var report = _sut.GetReport(CrewIdAlt1);
        var expectedCost = _pricingRegistry.CalculateCost(ModelGpt4o, 1000, 500);
        Assert.True(expectedCost > 0m);
        Assert.Equal(expectedCost, report.TotalCost);
    }

    [Fact]
    public void A_vendor_cost_of_zero_is_a_free_call_and_is_never_replaced_by_an_estimate()
    {
        // STUDIO-29: a free model on OpenRouter bills 0. The manager used to read 0 as "not
        // provided" and charge the registry price for a call that cost nothing.
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            PromptTokens = 1000,
            CompletionTokens = 500,
            Cost = 0m,
            CostCurrency = "USD",
        });

        Assert.Equal(0m, _sut.GetReport(CrewIdAlt1).TotalCost);
    }

    [Fact]
    public void ShouldUseProvidedCostWhenCostIsNonZero_WhenRecordUsage()
    {
        var evt = new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            PromptTokens = 1000,
            CompletionTokens = 500,
            Cost = 0.05m
        };

        _sut.RecordUsage(evt);

        var report = _sut.GetReport(CrewIdAlt1);
        Assert.Equal(0.05m, report.TotalCost);
    }

    [Fact]
    public void ShouldWithNoBudgetAlwaysWithinBudget_WhenRecordUsage()
    {
        var result = _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            PromptTokens = 100000,
            CompletionTokens = 50000,
            Cost = 100m
        });

        Assert.True(result.IsWithinBudget);
    }

    [Fact]
    public void ShouldReturnBudgetExceeded_WhenRecordUsageExceedsCostBudget()
    {
        _sut.SetCrewBudget(CrewIdAlt1, new BudgetLimit { MaxCostUsd = 0.01m });

        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.02m
        });

        // The usage was already recorded and exceeds budget, so next check should catch it
        // Actually, the check happens immediately upon RecordUsage
        // Let me re-approach: record something that pushes over the budget
        var result = _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.01m
        });

        Assert.False(result.IsWithinBudget);
        Assert.NotNull(result.Alert);
        Assert.Equal(BudgetAlertType.CostBudgetExceeded, result.Alert!.Type);
    }

    [Fact]
    public void ShouldCostThresholdWarningAtEightyPercent_WhenRecordUsage()
    {
        _sut.SetCrewBudget(CrewIdAlt1, new BudgetLimit
        {
            MaxCostUsd = 1.00m,
            AlertThresholdPercent = 80m
        });

        // Record usage at exactly 85% of budget
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.85m
        });

        var alerts = _sut.GetActiveAlerts();
        Assert.Contains(alerts, a => a.Type == BudgetAlertType.CostThresholdWarning);
    }

    [Fact]
    public void ShouldReturnBudgetExceeded_WhenRecordUsageExceedsTokenBudget()
    {
        _sut.SetCrewBudget(CrewIdAlt1, new BudgetLimit { MaxTokens = 100 });

        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            PromptTokens = 80,
            CompletionTokens = 30
        });

        var result = _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            PromptTokens = 10,
            CompletionTokens = 10
        });

        Assert.False(result.IsWithinBudget);
        Assert.NotNull(result.Alert);
        Assert.Equal(BudgetAlertType.TokenBudgetExceeded, result.Alert!.Type);
    }

    [Fact]
    public void ShouldReturnBudgetExceeded_WhenRecordUsageAgentBudgetExceeded()
    {
        _sut.SetAgentBudget(CrewIdAlt1, "agent1", new BudgetLimit { MaxCostUsd = 0.01m });

        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.02m
        });

        var result = _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.01m
        });

        Assert.False(result.IsWithinBudget);
    }

    [Fact]
    public void ShouldAgentBudgetExceededOtherAgentStillOk_WhenRecordUsage()
    {
        _sut.SetAgentBudget(CrewIdAlt1, "agent1", new BudgetLimit { MaxCostUsd = 0.01m });

        // Agent1 exceeds budget
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.02m
        });

        // Agent2 (no budget set) should be fine
        var result = _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent2",
            Model = ModelGpt4o,
            Cost = 0.50m
        });

        Assert.True(result.IsWithinBudget);
    }

    [Fact]
    public void ShouldOnBudgetAlertEventIsFired_WhenRecordUsage()
    {
        BudgetAlert? receivedAlert = null;
        _sut.OnBudgetAlert += (sender, args) => receivedAlert = args.Alert;

        _sut.SetCrewBudget(CrewIdAlt1, new BudgetLimit { MaxCostUsd = 0.001m });

        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.01m
        });

        Assert.NotNull(receivedAlert);
        Assert.Equal(CrewIdAlt1, receivedAlert!.CrewId);
    }

    [Fact]
    public void ShouldAggregateCorrectly_WhenGetReportByAgent()
    {
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.10m,
            PromptTokens = 100,
            CompletionTokens = 50
        });
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.20m,
            PromptTokens = 200,
            CompletionTokens = 100
        });
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent2",
            Model = ModelGpt4o,
            Cost = 0.05m,
            PromptTokens = 50,
            CompletionTokens = 25
        });

        var report = _sut.GetReport(CrewIdAlt1);

        Assert.Equal(0.35m, report.TotalCost);
        Assert.Equal(3, report.TotalCalls);
        Assert.Equal(525, report.TotalTokens); // 100+50+200+100+50+25
        Assert.Equal(2, report.ByAgent.Count);
        Assert.Equal(0.30m, report.ByAgent["agent1"].TotalCost);
        Assert.Equal(2, report.ByAgent["agent1"].TotalCalls);
        Assert.Equal(0.15m, report.ByAgent["agent1"].AverageCostPerCall);
        Assert.Equal(0.05m, report.ByAgent["agent2"].TotalCost);
    }

    [Fact]
    public void ShouldAggregateCorrectly_WhenGetReportByModel()
    {
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.10m
        });
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4oMini,
            Cost = 0.02m
        });
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.05m
        });

        var report = _sut.GetReport(CrewIdAlt1);

        Assert.Equal(2, report.ByModel.Count);
        Assert.Equal(0.15m, report.ByModel[ModelGpt4o]);
        Assert.Equal(0.02m, report.ByModel[ModelGpt4oMini]);
    }

    [Fact]
    public void ShouldAggregateCorrectly_WhenGetReportByOperationType()
    {
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.10m,
            OperationType = "llm_call"
        });
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = "text-embedding-3-small",
            Cost = 0.001m,
            OperationType = "embedding"
        });

        var report = _sut.GetReport(CrewIdAlt1);

        Assert.Equal(2, report.ByOperationType.Count);
        Assert.Equal(0.10m, report.ByOperationType["llm_call"]);
        Assert.Equal(0.001m, report.ByOperationType["embedding"]);
    }

    [Fact]
    public void ShouldReturnOnlyAgentData_WhenGetReportFilterByAgent()
    {
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.10m
        });
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent2",
            Model = ModelGpt4o,
            Cost = 0.20m
        });

        var report = _sut.GetReport(CrewIdAlt1, "agent1");

        Assert.Equal(0.10m, report.TotalCost);
        Assert.Equal(1, report.TotalCalls);
    }

    [Fact]
    public void ShouldFilterByTimestamp_WhenGetReportForPeriod()
    {
        var now = DateTime.UtcNow;

        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.10m,
            Timestamp = now.AddHours(-2)
        });
        _sut.RecordUsage(new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.20m,
            Timestamp = now
        });

        var report = _sut.GetReportForPeriod(now.AddHours(-1), now.AddHours(1), CrewIdAlt1);

        Assert.Equal(0.20m, report.TotalCost);
        Assert.Equal(1, report.TotalCalls);
    }

    [Fact]
    public void ShouldReturnZeroReport_WhenGetReportEmptyHistory()
    {
        var report = _sut.GetReport("nonexistent-crew");

        Assert.Equal(0m, report.TotalCost);
        Assert.Equal(0, report.TotalTokens);
        Assert.Equal(0, report.TotalCalls);
        Assert.Empty(report.ByAgent);
        Assert.Empty(report.ByModel);
    }

    [Fact]
    public void ShouldApply_WhenSetCrewBudgetDefaultBudgetFromOptions()
    {
        var manager = new CostBudgetManager(
            _pricingRegistry,
            Options.Create(new CostTrackingOptions
            {
                DefaultCrewBudget = new BudgetLimit { MaxCostUsd = 0.001m }
            }),
            NullLogger<CostBudgetManager>.Instance);

        manager.RecordUsage(new CostUsageEvent
        {
            CrewId = "any-crew",
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.01m
        });

        var result = manager.RecordUsage(new CostUsageEvent
        {
            CrewId = "any-crew",
            AgentId = "agent1",
            Model = ModelGpt4o,
            Cost = 0.001m
        });

        Assert.False(result.IsWithinBudget);
    }

    [Fact]
    public void ShouldReturnAllAlerts_WhenGetActiveAlerts()
    {
        _sut.SetCrewBudget(CrewIdAlt1, new BudgetLimit { MaxCostUsd = 0.001m });
        _sut.SetCrewBudget("crew2", new BudgetLimit { MaxCostUsd = 0.001m });

        _sut.RecordUsage(new CostUsageEvent { CrewId = CrewIdAlt1, AgentId = "a", Model = ModelGpt4o, Cost = 0.01m });
        _sut.RecordUsage(new CostUsageEvent { CrewId = "crew2", AgentId = "a", Model = ModelGpt4o, Cost = 0.01m });

        var alerts = _sut.GetActiveAlerts();
        Assert.True(alerts.Count >= 2);
    }

    [Fact]
    public void ShouldHaveCorrectDefaults_WhenBudgetCheckResultWithinBudget()
    {
        var result = CostBudgetCheckResult.WithinBudget();

        Assert.True(result.IsWithinBudget);
        Assert.Null(result.Message);
        Assert.Null(result.Alert);
    }

    [Fact]
    public void ShouldContainDetails_WhenBudgetCheckResultBudgetExceeded()
    {
        var alert = new BudgetAlert
        {
            CrewId = CrewIdAlt1,
            Type = BudgetAlertType.CostBudgetExceeded,
            CurrentUsage = 10m,
            Limit = 5m,
            UsagePercent = 200m
        };

        var result = CostBudgetCheckResult.BudgetExceeded("Budget exceeded", alert);

        Assert.False(result.IsWithinBudget);
        Assert.Equal("Budget exceeded", result.Message);
        Assert.Equal(alert, result.Alert);
    }
}

public class EnhancedTokenCounterTests
{
    private readonly EnhancedTokenCounter _sut;

    public EnhancedTokenCounterTests()
    {
        _sut = new EnhancedTokenCounter(
            Options.Create(new TokenCounterOptions()));
    }

    [Fact]
    public void ShouldReturnZero_WhenCountTokensEmptyString()
    {
        Assert.Equal(0, _sut.CountTokens(""));
    }

    [Fact]
    public void ShouldReturnZero_WhenCountTokensNullString()
    {
        Assert.Equal(0, _sut.CountTokens(null!));
    }

    [Fact]
    public void ShouldSimpleTextApproximatesCorrectly_WhenCountTokens()
    {
        // "Hello world" = 11 chars, at 3.5 chars/token = ceil(11/3.5) = ceil(3.14) = 4
        var count = _sut.CountTokens("Hello world");

        Assert.Equal(4, count);
    }

    [Fact]
    public void ShouldScaleLinearly_WhenCountTokensLongText()
    {
        var text = new string('a', 350); // 350 chars / 3.5 = 100 tokens
        var count = _sut.CountTokens(text);

        Assert.Equal(100, count);
    }

    [Fact]
    public void ShouldUseConfigured_WhenCountTokensCustomCharsPerToken()
    {
        var counter = new EnhancedTokenCounter(
            Options.Create(new TokenCounterOptions { CharsPerToken = 4.0f }));

        var text = new string('a', 100); // 100 chars / 4.0 = 25 tokens
        Assert.Equal(25, counter.CountTokens(text));
    }

    [Fact]
    public void ShouldIncludeOverhead_WhenCountTokensForMessages()
    {
        var messages = new List<(string role, string content)>
        {
            ("system", "You are helpful."),
            ("user", "Hello")
        };

        var count = _sut.CountTokensForMessages(messages);

        // 2 messages * 4 tokens/message overhead = 8
        // "system" = ceil(6/3.5) = 2 tokens
        // "You are helpful." = ceil(16/3.5) = ceil(4.57) = 5 tokens
        // "user" = ceil(4/3.5) = ceil(1.14) = 2 tokens
        // "Hello" = ceil(5/3.5) = ceil(1.43) = 2 tokens
        // + 3 reply priming + 2 special overhead = 5
        // Total = 8 + 2 + 5 + 2 + 2 + 5 = 24
        Assert.True(count > 0);
        Assert.True(count >= 20); // Reasonable lower bound
    }

    [Fact]
    public void ShouldReturnOverheadOnly_WhenCountTokensForMessagesEmptyMessages()
    {
        var messages = new List<(string role, string content)>();

        var count = _sut.CountTokensForMessages(messages);

        // Just reply priming (3) + special (2) = 5
        Assert.Equal(5, count);
    }

    [Fact]
    public void ShouldImplementsITokenCounter()
    {
        var counter = _sut;
        Assert.True(counter.CountTokens("Hello") > 0);
    }
}

public class CostTrackingDependencyInjectionTests
{
    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // BindConfiguration requires IConfiguration to be registered
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        return services;
    }

    [Fact]
    public void ShouldRegisterAllServices_WhenAddOrkeonCostTracking()
    {
        var services = CreateServiceCollection();
        services.AddOrkeonCostTracking();

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IModelPricingRegistry>());
        Assert.NotNull(provider.GetService<ICostBudgetManager>());
        Assert.NotNull(provider.GetService<ITokenCounter>());
    }

    [Fact]
    public void ShouldServicesAreSingletons_WhenAddOrkeonCostTracking()
    {
        var services = CreateServiceCollection();
        services.AddOrkeonCostTracking();

        var provider = services.BuildServiceProvider();

        var registry1 = provider.GetService<IModelPricingRegistry>();
        var registry2 = provider.GetService<IModelPricingRegistry>();
        Assert.Same(registry1, registry2);

        var manager1 = provider.GetService<ICostBudgetManager>();
        var manager2 = provider.GetService<ICostBudgetManager>();
        Assert.Same(manager1, manager2);

        var counter1 = provider.GetService<ITokenCounter>();
        var counter2 = provider.GetService<ITokenCounter>();
        Assert.Same(counter1, counter2);
    }

    [Fact]
    public void ShouldBeEnhancedTokenCounter_WhenAddOrkeonCostTrackingTokenCounter()
    {
        var services = CreateServiceCollection();
        services.AddOrkeonCostTracking();

        var provider = services.BuildServiceProvider();
        var counter = provider.GetService<ITokenCounter>();

        Assert.IsType<EnhancedTokenCounter>(counter);
    }

    [Fact]
    public void ShouldCalledMultipleTimesDoesNotDuplicate_WhenAddOrkeonCostTracking()
    {
        var services = CreateServiceCollection();
        services.AddOrkeonCostTracking();
        services.AddOrkeonCostTracking();

        var provider = services.BuildServiceProvider();

        // Should resolve without exception
        Assert.NotNull(provider.GetService<IModelPricingRegistry>());
        Assert.NotNull(provider.GetService<ICostBudgetManager>());
        Assert.NotNull(provider.GetService<ITokenCounter>());
    }
}
