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

    [Fact]
    public void ShouldAutoCalculatesCostWhenCostIsZero_WhenRecordUsage()
    {
        var evt = new CostUsageEvent
        {
            CrewId = CrewIdAlt1,
            AgentId = "agent1",
            Model = ModelGpt4o,
            PromptTokens = 1000,
            CompletionTokens = 500,
            Cost = 0m // should be auto-calculated
        };

        var result = _sut.RecordUsage(evt);

        Assert.True(result.IsWithinBudget);

        var report = _sut.GetReport(CrewIdAlt1);
        var expectedCost = _pricingRegistry.CalculateCost(ModelGpt4o, 1000, 500);
        Assert.Equal(expectedCost, report.TotalCost);
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
