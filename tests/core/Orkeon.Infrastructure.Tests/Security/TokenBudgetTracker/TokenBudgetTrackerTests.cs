using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.CostTracking;
using Orkeon.Infrastructure.Tests.Doubles;
using TokenBudgetTrackerSut = Orkeon.Infrastructure.Security.TokenBudgetTracker;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.Security;

public class TokenBudgetTrackerTests
{
    private readonly TokenBudgetTrackerSut _sut;
    private readonly TokenBudgetOptions _options;

    public TokenBudgetTrackerTests()
    {
        _options = new TokenBudgetOptions
        {
            MaxTokensPerAgent = 10_000,
            MaxTokensPerCrew = 50_000,
            MaxCostPerCrew = 5.0m
        };
        _sut = new TokenBudgetTrackerSut(
            Options.Create(_options),
            NullLogger<TokenBudgetTrackerSut>.Instance);
    }

    [Fact]
    public void ShouldReturnWithinBudget_WhenUsageIsUnderBudget()
    {
        var result = _sut.RecordUsage(CrewIdAlt1, "researcher", 100, 50, ModelGpt4o);

        Assert.True(result.IsWithinBudget);
        Assert.Null(result.DenialReason);
        Assert.NotNull(result.AgentUsage);
        Assert.NotNull(result.CrewUsage);
        Assert.Equal(150, result.AgentUsage!.TotalTokens);
        Assert.Equal(1, result.AgentUsage.CallCount);
    }

    [Fact]
    public void ShouldReturnBudgetExceeded_WhenAgentBudgetExceeded()
    {
        // Record enough to exceed agent limit of 10,000
        _sut.RecordUsage(CrewIdAlt1, "researcher", 5000, 4000, ModelGpt4o);
        var result = _sut.RecordUsage(CrewIdAlt1, "researcher", 1000, 1000, ModelGpt4o);

        Assert.False(result.IsWithinBudget);
        Assert.Contains("researcher", result.DenialReason!);
        Assert.Contains("token budget", result.DenialReason!);
    }

    [Fact]
    public void ShouldReturnBudgetExceeded_WhenCrewBudgetExceeded()
    {
        // Use different agents to avoid agent limit but exceed crew limit of 50,000
        for (int i = 0; i < 5; i++)
        {
            _sut.RecordUsage(CrewIdAlt1, $"agent{i}", 5000, 4000, ModelGpt35Turbo);
        }

        // This should push crew total over 50,000
        var result = _sut.RecordUsage(CrewIdAlt1, "agent5", 5000, 4000, ModelGpt35Turbo);

        Assert.False(result.IsWithinBudget);
        Assert.Contains(CrewIdAlt1, result.DenialReason!);
        Assert.Contains("token budget", result.DenialReason!);
    }

    [Fact]
    public void ShouldReturnBudgetExceeded_WhenCostBudgetExceeded()
    {
        var tracker = new TokenBudgetTrackerSut(
            Options.Create(new TokenBudgetOptions
            {
                MaxTokensPerAgent = 0, // unlimited
                MaxTokensPerCrew = 0,  // unlimited
                MaxCostPerCrew = 0.01m // very low cost limit
            }),
            NullLogger<TokenBudgetTrackerSut>.Instance);

        // gpt-4: prompt=$0.03/1K, completion=$0.06/1K
        // 1000 prompt + 1000 completion = $0.03 + $0.06 = $0.09
        var result = tracker.RecordUsage(CrewIdAlt1, "researcher", 1000, 1000, ModelGpt4);

        Assert.False(result.IsWithinBudget);
        Assert.Contains("cost budget", result.DenialReason!);
    }

    [Fact]
    public void ShouldCalculateCorrectRates_WhenModelIsGpt4()
    {
        // gpt-4: $0.03/1K prompt, $0.06/1K completion
        var cost = TokenBudgetTrackerSut.EstimateCost(1000, 1000, ModelGpt4, registry: null);
        Assert.Equal(0.09m, cost); // 0.03 + 0.06
    }

    [Fact]
    public void ShouldCalculateCorrectRates_WhenModelIsClaude3Opus()
    {
        // claude-3-opus: $0.015/1K prompt, $0.075/1K completion
        var cost = TokenBudgetTrackerSut.EstimateCost(1000, 1000, "claude-3-opus", registry: null);
        Assert.Equal(0.09m, cost); // 0.015 + 0.075
    }

    [Fact]
    public void ShouldAccumulateCounters_WhenMultipleCallsMade()
    {
        _sut.RecordUsage(CrewIdAlt1, "researcher", 100, 50, ModelGpt4o);
        _sut.RecordUsage(CrewIdAlt1, "researcher", 200, 100, ModelGpt4o);
        var result = _sut.RecordUsage(CrewIdAlt1, "researcher", 300, 150, ModelGpt4o);

        Assert.True(result.IsWithinBudget);
        Assert.Equal(900, result.AgentUsage!.TotalTokens); // 150+300+450
        Assert.Equal(3, result.AgentUsage.CallCount);
        Assert.Equal(900, result.CrewUsage!.TotalTokens);
    }

    [Fact]
    public void ShouldReturnCorrectData_WhenGettingReport()
    {
        _sut.RecordUsage(CrewIdAlt1, "researcher", 100, 50, ModelGpt4o);
        _sut.RecordUsage(CrewIdAlt1, "writer", 200, 100, ModelGpt4o);
        _sut.RecordUsage(CrewIdAlt1, "researcher", 50, 25, ModelGpt4o);

        var report = _sut.GetReport(CrewIdAlt1);

        Assert.Equal(CrewIdAlt1, report.CrewId);
        Assert.Equal(525, report.TotalUsage.TotalTokens); // 150+300+75
        Assert.Equal(3, report.TotalUsage.CallCount);
        Assert.Equal(2, report.UsageByAgent.Count);
        Assert.Equal(225, report.UsageByAgent["researcher"].TotalTokens);
        Assert.Equal(300, report.UsageByAgent["writer"].TotalTokens);
    }

    [Fact]
    public void ShouldReturnZeros_WhenCrewHasNoUsage()
    {
        var report = _sut.GetReport("nonexistent");

        Assert.Equal("nonexistent", report.CrewId);
        Assert.Equal(0, report.TotalUsage.TotalTokens);
        Assert.Equal(0, report.TotalUsage.CallCount);
        Assert.Empty(report.UsageByAgent);
    }

    [Fact]
    public void ShouldAlwaysBeWithinBudget_WhenBudgetIsUnlimited()
    {
        var tracker = new TokenBudgetTrackerSut(
            Options.Create(new TokenBudgetOptions
            {
                MaxTokensPerAgent = 0,
                MaxTokensPerCrew = 0,
                MaxCostPerCrew = 0m
            }),
            NullLogger<TokenBudgetTrackerSut>.Instance);

        // Large usage should still be within budget
        var result = tracker.RecordUsage(CrewIdAlt1, "researcher", 1_000_000, 1_000_000, ModelGpt4);
        Assert.True(result.IsWithinBudget);
    }

    [Fact]
    [Trait("BR", "CH-36")]
    public void ShouldPriceWithTheProviderlessRegistryRow_WhenTheRegistryHasACustomRowForTheModel()
    {
        var options = new CostTrackingOptions();
        options.CustomPricings.Add(new ModelPricing
        {
            ModelPattern = "invented-model-ch36",
            PromptPricePerMillion = 3m,
            CompletionPricePerMillion = 6m,
        });
        var registry = new ModelPricingRegistry(Options.Create(options), NullLogger<ModelPricingRegistry>.Instance);

        var cost = TokenBudgetTrackerSut.EstimateCost(1_000_000, 500_000, "invented-model-ch36", registry);

        Assert.Equal(6m, cost);
    }

    private static ModelPricingRegistry Registry(Action<CostTrackingOptions>? configure = null)
    {
        var options = new CostTrackingOptions();
        configure?.Invoke(options);
        return new ModelPricingRegistry(Options.Create(options), NullLogger<ModelPricingRegistry>.Instance);
    }

    [Fact]
    [Trait("BR", "#396")]
    public void ShouldPriceWithTheProviderRow_WhenACustomRowIsScopedToTheProvider()
    {
        var registry = Registry(options => options.CustomPricings.Add(new ModelPricing
        {
            Provider = "invented-provider",
            ModelPattern = "invented-model-396",
            PromptPricePerMillion = 3m,
            CompletionPricePerMillion = 6m,
        }));

        var scoped = TokenBudgetTrackerSut.EstimateCost(1_000_000, 500_000, "invented-model-396", "invented-provider", registry);
        var blind = TokenBudgetTrackerSut.EstimateCost(1_000_000, 500_000, "invented-model-396", registry);

        Assert.Equal(6m, scoped);
        // Without the provider the scoped row stays unseen: the built-in approximate rates apply.
        Assert.Equal(2m, blind);
    }

    [Fact]
    [Trait("BR", "#396")]
    public void ShouldPriceWithTheBuiltInProviderRow_WhenTheProviderHasItsOwnRowForTheModel()
    {
        var registry = Registry();
        var row = registry.TryGetPricing("deepseek", "deepseek-flash");
        Assert.NotNull(row);
        Assert.Null(registry.TryGetPricing("deepseek-flash"));

        var scoped = TokenBudgetTrackerSut.EstimateCost(1_000_000, 1_000_000, "deepseek-flash", "DeepSeek", registry);
        var blind = TokenBudgetTrackerSut.EstimateCost(1_000_000, 1_000_000, "deepseek-flash", registry);

        Assert.Equal(row.PromptPricePerMillion + row.CompletionPricePerMillion, scoped);
        Assert.NotEqual(scoped, blind);
    }

    [Fact]
    [Trait("BR", "#396")]
    public void ShouldReachTheProviderOverload_WhenTheTrackerIsCalledThroughItsInterface()
    {
        var options = Options.Create(new TokenBudgetOptions { MaxTokensPerAgent = 0, MaxTokensPerCrew = 0, MaxCostPerCrew = 1m });
        ITokenBudgetTracker tracker = new TokenBudgetTrackerSut(options, NullLogger<TokenBudgetTrackerSut>.Instance, Registry());

        var result = tracker.RecordUsage(CrewIdAlt1, "researcher", 1_000_000, 1_000_000, "invented-local-model", "ollama");

        Assert.True(result.IsWithinBudget);
        Assert.Equal(0m, result.CrewUsage!.EstimatedCost);
    }

    [Fact]
    [Trait("BR", "#396")]
    public void ShouldCostNothing_WhenTheModelIsServedByOllama()
    {
        var cost = TokenBudgetTrackerSut.EstimateCost(1_000_000, 1_000_000, "invented-local-model", "ollama", Registry());

        Assert.Equal(0m, cost);
    }

    [Fact]
    [Trait("BR", "#396")]
    public void ShouldCheckTheCostBudgetWithTheProviderRow_WhenUsageIsRecordedWithItsProvider()
    {
        var options = Options.Create(new TokenBudgetOptions { MaxTokensPerAgent = 0, MaxTokensPerCrew = 0, MaxCostPerCrew = 1m });
        var withProvider = new TokenBudgetTrackerSut(options, NullLogger<TokenBudgetTrackerSut>.Instance, Registry());
        var withoutProvider = new TokenBudgetTrackerSut(options, NullLogger<TokenBudgetTrackerSut>.Instance, Registry());

        var local = withProvider.RecordUsage(CrewIdAlt1, "researcher", 1_000_000, 1_000_000, "invented-local-model", "ollama");
        var blind = withoutProvider.RecordUsage(CrewIdAlt1, "researcher", 1_000_000, 1_000_000, "invented-local-model");

        Assert.True(local.IsWithinBudget);
        Assert.Equal(0m, local.CrewUsage!.EstimatedCost);
        Assert.False(blind.IsWithinBudget);
    }

    [Fact]
    [Trait("BR", "#396")]
    public void ShouldIgnoreTheProvider_WhenATrackerDoesNotOverrideTheProviderOverload()
    {
        var stub = new StubTokenBudgetTracker();
        ITokenBudgetTracker tracker = stub;

        tracker.RecordUsage(CrewIdAlt1, "researcher", 10, 5, ModelGpt4o, "ollama");

        Assert.Equal(1, stub.Calls);
    }
}
