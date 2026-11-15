using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.CostTracking;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.CostTracking;

/// <summary>
/// <c>Orkeon:CostTracking:DefaultCrewBudget</c> is a cap, not a readout: the meter records every call
/// against the crew's budget, and once the crew has spent it the next call is refused before it starts.
/// </summary>
public sealed class CostBudgetEnforcementTests
{
    private const string Crew = "crew-1";

    private static CostBudgetManager Manager(BudgetLimit? crewBudget, bool enabled = true)
    {
        var options = new CostTrackingOptions { Enabled = enabled, DefaultCrewBudget = crewBudget };
        return new CostBudgetManager(
            new ModelPricingRegistry(Options.Create(new CostTrackingOptions()), NullLogger<ModelPricingRegistry>.Instance),
            Options.Create(options),
            NullLogger<CostBudgetManager>.Instance);
    }

    private static CostBudgetManager Manager(BudgetLimit? crewBudget, FakeLoggerFactory logs, bool enabled = true)
    {
        var options = new CostTrackingOptions { Enabled = enabled, DefaultCrewBudget = crewBudget };
        return new CostBudgetManager(
            new ModelPricingRegistry(Options.Create(new CostTrackingOptions()), NullLogger<ModelPricingRegistry>.Instance),
            Options.Create(options),
            logs.CreateLogger<CostBudgetManager>());
    }

    private static List<(string Category, LogLevel Level, string Message)> BudgetWarnings(FakeLoggerFactory logs) =>
        [.. logs.Entries.Where(e => e.Level == LogLevel.Warning && e.Category.EndsWith("CostBudgetManager", StringComparison.Ordinal))];

    private static CostUsageEvent Usage(int prompt, int completion, string crewId = Crew, decimal? cost = null) => new()
    {
        CrewId = crewId,
        Model = "vendor/model-x",
        Provider = "vendor",
        PromptTokens = prompt,
        CompletionTokens = completion,
        Cost = cost,
    };

    private static LlmResponse Answer(int prompt, int completion) => new()
    {
        Content = "ok",
        TokensUsed = prompt + completion,
        PromptTokens = prompt,
        CompletionTokens = completion,
        Model = "vendor/model-x",
    };

    private static LlmMessage[] Conversation() => [LlmMessage.User("hello")];

    [Fact]
    [Trait("BR", "CH-10")]
    public void ShouldWarnThatTheBudgetCannotCoverIt_WhenAnUnpricedModelRunsUnderMaxCostUsd()
    {
        using var logs = new FakeLoggerFactory();
        var manager = Manager(new BudgetLimit { MaxCostUsd = 5m }, logs);

        manager.RecordUsage(Usage(10, 10));
        manager.RecordUsage(Usage(10, 10));
        var afterSame = BudgetWarnings(logs);
        manager.RecordUsage(Usage(10, 10) with { Model = "vendor/model-y" });
        var afterOther = BudgetWarnings(logs);

        Assert.Single(afterSame);
        Assert.Contains("vendor/model-x", afterSame[0].Message, StringComparison.Ordinal);
        Assert.Contains("budget", afterSame[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, afterOther.Count);
        Assert.Contains("vendor/model-y", afterOther[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-10")]
    public void ShouldNotWarnAboutTheBudget_WhenNoCostBudgetIsSet()
    {
        using var logs = new FakeLoggerFactory();
        var withCost = Manager(new BudgetLimit { MaxCostUsd = 5m }, logs);
        var tokenOnly = Manager(new BudgetLimit { MaxTokens = 1000 }, logs);
        var none = Manager(crewBudget: null, logs);

        withCost.RecordUsage(Usage(10, 10));
        var control = BudgetWarnings(logs);
        tokenOnly.RecordUsage(Usage(10, 10));
        none.RecordUsage(Usage(10, 10));
        var after = BudgetWarnings(logs);

        Assert.Single(control);
        Assert.Single(after);
    }

    [Fact]
    public void ShouldRefuse_WhenRecordedTokensReachTheCap()
    {
        var manager = Manager(new BudgetLimit { MaxTokens = 100 });

        manager.RecordUsage(Usage(60, 30));
        Assert.True(manager.CheckBudget(Crew, null).IsWithinBudget);

        manager.RecordUsage(Usage(5, 5));
        var spent = manager.CheckBudget(Crew, null);

        Assert.False(spent.IsWithinBudget);
        Assert.Equal(BudgetAlertType.TokenBudgetExceeded, spent.Alert?.Type);
        Assert.Contains("100", spent.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuseWithCostAlert_WhenRecordedCostReachesTheCap()
    {
        var manager = Manager(new BudgetLimit { MaxCostUsd = 0.10m });

        manager.RecordUsage(Usage(10, 10, cost: 0.10m));
        var spent = manager.CheckBudget(Crew, null);

        Assert.False(spent.IsWithinBudget);
        Assert.Equal(BudgetAlertType.CostBudgetExceeded, spent.Alert?.Type);
    }

    [Fact]
    public void ShouldRaiseNoAlert_WhenOnlyCheckingTheBudget()
    {
        var manager = Manager(new BudgetLimit { MaxTokens = 10 });
        manager.RecordUsage(Usage(10, 10));
        var alerts = manager.GetActiveAlerts().Count;

        manager.CheckBudget(Crew, null);
        manager.CheckBudget(Crew, null);

        Assert.Equal(alerts, manager.GetActiveAlerts().Count);
    }

    [Fact]
    public void ShouldAllow_WhenTheCallIsOutsideAnyCrew()
    {
        var manager = Manager(new BudgetLimit { MaxTokens = 1 });
        manager.RecordUsage(Usage(10, 10, crewId: ""));

        Assert.True(manager.CheckBudget(null, null).IsWithinBudget);
    }

    [Fact]
    public void ShouldAllow_WhenCostTrackingIsDisabled()
    {
        var manager = Manager(new BudgetLimit { MaxTokens = 1 }, enabled: false);
        manager.RecordUsage(Usage(10, 10));

        Assert.True(manager.CheckBudget(Crew, null).IsWithinBudget);
    }

    [Fact]
    public void ShouldRefuseOnlyThatAgent_WhenItsAgentBudgetIsSpent()
    {
        var manager = Manager(crewBudget: null);
        manager.SetAgentBudget(Crew, "writer", new BudgetLimit { MaxTokens = 10 });
        manager.RecordUsage(Usage(10, 10) with { AgentId = "writer" });

        Assert.False(manager.CheckBudget(Crew, "writer").IsWithinBudget);
        Assert.True(manager.CheckBudget(Crew, "reader").IsWithinBudget);
    }

    [Fact]
    public void ShouldCountOnce_WhenTheSameEventIsRecordedTwice()
    {
        var manager = Manager(new BudgetLimit { MaxTokens = 30 });
        var usage = Usage(10, 10);

        manager.RecordUsage(usage);
        manager.RecordUsage(usage);

        Assert.True(manager.CheckBudget(Crew, null).IsWithinBudget);
        Assert.Equal(20, manager.GetReport(Crew).TotalTokens);
    }

    [Fact]
    public async Task ShouldRefuseTheNextCall_WhenAChatSpentTheTokenBudget()
    {
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetChatResult(Answer(3000, 823));
        var metered = MeteredLlmProvider.Wrap(provider, sink: null, Manager(new BudgetLimit { MaxTokens = 1 }));
        var ct = TestContext.Current.CancellationToken;

        using (LlmUsageScope.Begin(crewId: Crew))
        {
            await metered.ChatAsync(Conversation(), cancellationToken: ct);
            var refused = await Assert.ThrowsAsync<BudgetExhaustedException>(
                () => metered.ChatAsync(Conversation(), cancellationToken: ct));

            Assert.Equal(BudgetDimension.Tokens, refused.Dimension);
            Assert.Contains("3823", refused.Message, StringComparison.Ordinal);
        }

        Assert.Equal(1, provider.ChatCallCount);
    }

    [Fact]
    public async Task ShouldRefuseAStreamedGenerate_WhenTheBudgetIsSpent()
    {
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetGenerateResult(Answer(10, 10));
        var metered = MeteredLlmProvider.Wrap(provider, sink: null, Manager(new BudgetLimit { MaxTokens = 5 }));
        var ct = TestContext.Current.CancellationToken;

        using (LlmUsageScope.Begin(crewId: Crew))
        {
            await metered.GenerateAsync("hello", cancellationToken: ct);
            await Assert.ThrowsAsync<BudgetExhaustedException>(async () =>
            {
                await foreach (var _ in ((IStreamingLlmProvider)metered).GenerateStreamingAsync("hello", cancellationToken: ct))
                {
                    // the refusal comes before the first chunk
                }
            });
        }

        Assert.Equal(1, provider.GenerateCallCount);
    }

    [Fact]
    public async Task ShouldRefuseWithCostDimension_WhenTheCostBudgetIsSpent()
    {
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetChatResult(Answer(10, 10) with
        {
            Metadata = new Dictionary<string, object>
            {
                [LlmUsageMetadataKeys.Cost] = 0.5,
                [LlmUsageMetadataKeys.CostCurrency] = "USD",
            },
        });
        var metered = MeteredLlmProvider.Wrap(provider, sink: null, Manager(new BudgetLimit { MaxCostUsd = 0.25m }));
        var ct = TestContext.Current.CancellationToken;

        using (LlmUsageScope.Begin(crewId: Crew))
        {
            await metered.ChatAsync(Conversation(), cancellationToken: ct);
            var refused = await Assert.ThrowsAsync<BudgetExhaustedException>(
                () => metered.ChatAsync(Conversation(), cancellationToken: ct));

            Assert.Equal(BudgetDimension.Cost, refused.Dimension);
        }
    }

    [Fact]
    public async Task ShouldRecordOnce_WhenAHostSinkAlsoHandsTheManagerTheEvent()
    {
        var manager = Manager(new BudgetLimit { MaxTokens = 1000 });
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetChatResult(Answer(10, 10));
        var sink = new StubForwardingLlmUsageSink(manager);
        var metered = MeteredLlmProvider.Wrap(provider, sink, manager);

        using (LlmUsageScope.Begin(crewId: Crew))
            await metered.ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(20, manager.GetReport(Crew).TotalTokens);
    }

    [Fact]
    public void ShouldRaiseTheAlert_WhenTheRecordedTotalEqualsTheCap()
    {
        var manager = Manager(new BudgetLimit { MaxTokens = 20 });

        var answer = manager.RecordUsage(Usage(10, 10));

        Assert.False(answer.IsWithinBudget);
        Assert.Equal(BudgetAlertType.TokenBudgetExceeded, answer.Alert?.Type);
        Assert.False(manager.CheckBudget(Crew, null).IsWithinBudget);
    }

    [Fact]
    public async Task ShouldRefuseAStreamedChat_WhenTheBudgetIsSpent()
    {
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetChatResult(Answer(10, 10));
        var metered = MeteredLlmProvider.Wrap(provider, sink: null, Manager(new BudgetLimit { MaxTokens = 5 }));
        var ct = TestContext.Current.CancellationToken;

        using (LlmUsageScope.Begin(crewId: Crew))
        {
            await metered.ChatAsync(Conversation(), cancellationToken: ct);
            await Assert.ThrowsAsync<BudgetExhaustedException>(async () =>
            {
                await foreach (var _ in ((IStreamingLlmProvider)metered).ChatStreamingAsync(Conversation(), cancellationToken: ct))
                {
                    // the refusal comes before the first event
                }
            });
        }

        Assert.Equal(1, provider.ChatCallCount);
    }

    [Fact]
    public async Task ShouldRecordInBothPlaces_WhenAnInnerMeterHasOnlyASinkAndTheOuterOnlyABudget()
    {
        var manager = Manager(new BudgetLimit { MaxTokens = 1000 });
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetChatResult(Answer(10, 10));
        var sink = new StubForwardingLlmUsageSink();
        var inner = MeteredLlmProvider.Wrap(provider, sink);
        var outer = MeteredLlmProvider.Wrap(inner, sink: null, manager);

        using (LlmUsageScope.Begin(crewId: Crew))
            await outer.ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, sink.Events);
        Assert.Equal(20, manager.GetReport(Crew).TotalTokens);
    }

    [Fact]
    [Trait("BR", "CH-31")]
    public void ShouldRecordAndRefuseNothing_WhenCostTrackingIsOff()
    {
        var manager = Manager(new BudgetLimit { MaxCostUsd = 0.10m, MaxTokens = 10 }, enabled: false);

        var first = manager.RecordUsage(Usage(10, 10, cost: 0.5m));
        var second = manager.RecordUsage(Usage(10, 10, cost: 0.5m));

        Assert.True(first.IsWithinBudget);
        Assert.True(second.IsWithinBudget);
        Assert.Equal(0.5m, second.Cost);
        Assert.Equal(40, manager.GetReport(Crew).TotalTokens);
    }

    [Fact]
    [Trait("BR", "CH-31")]
    public void ShouldRaiseNoAlert_WhenCostTrackingIsOff()
    {
        var manager = Manager(new BudgetLimit { MaxCostUsd = 0.10m, MaxTokens = 10 }, enabled: false);
        var raised = 0;
        manager.OnBudgetAlert += (_, _) => raised++;

        var answer = manager.RecordUsage(Usage(10, 10, cost: 0.5m));

        Assert.Null(answer.Alert);
        Assert.Equal(0, raised);
        Assert.Empty(manager.GetActiveAlerts());
    }

    [Fact]
    [Trait("BR", "CH-31")]
    public void ShouldNotWarnTheBudgetCannotCount_WhenCostTrackingIsOff()
    {
        using var logs = new FakeLoggerFactory();
        var manager = Manager(new BudgetLimit { MaxCostUsd = 5m }, logs, enabled: false);

        manager.RecordUsage(Usage(10, 10));

        Assert.Empty(BudgetWarnings(logs));
    }

    [Fact]
    [Trait("BR", "CH-31")]
    public void ShouldStillRefuse_WhenCostTrackingIsOn()
    {
        using var logs = new FakeLoggerFactory();
        var manager = Manager(new BudgetLimit { MaxCostUsd = 5m, MaxTokens = 10 }, logs);

        var answer = manager.RecordUsage(Usage(10, 10));

        Assert.False(answer.IsWithinBudget);
        Assert.Equal(BudgetAlertType.TokenBudgetExceeded, answer.Alert?.Type);
        Assert.Single(BudgetWarnings(logs), w => w.Message.Contains("vendor/model-x", StringComparison.Ordinal));
    }

    private static CostBudgetManager ManagerWithRegistry(
        BudgetLimit? crewBudget, FakeLoggerFactory logs, params ModelPricing[] custom)
    {
        var pricingOptions = new CostTrackingOptions();
        foreach (var row in custom)
        {
            pricingOptions.CustomPricings.Add(row);
        }

        return new CostBudgetManager(
            new ModelPricingRegistry(Options.Create(pricingOptions), logs.CreateLogger<ModelPricingRegistry>()),
            Options.Create(new CostTrackingOptions { DefaultCrewBudget = crewBudget }),
            logs.CreateLogger<CostBudgetManager>());
    }

    private static CostUsageEvent DeepSeekUsage() => Usage(100_000, 100_000) with
    {
        Model = "deepseek-flash",
        Provider = "deepseek",
    };

    [Fact]
    [Trait("BR", "CH-36")]
    public void ShouldPriceAndNotWarn_WhenRecordUsageCarriesAProviderWithABuiltInRow()
    {
        using var logs = new FakeLoggerFactory();
        var manager = ManagerWithRegistry(new BudgetLimit { MaxCostUsd = 5m }, logs);

        var answer = manager.RecordUsage(DeepSeekUsage());

        Assert.Equal(0.15m, answer.Cost);
        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    [Trait("BR", "CH-36")]
    public void ShouldPriceWithTheCustomRow_WhenAProviderlessCustomRowNamesTheModel()
    {
        using var logs = new FakeLoggerFactory();
        var custom = new ModelPricing
        {
            ModelPattern = "deepseek-flash",
            PromptPricePerMillion = 2m,
            CompletionPricePerMillion = 4m,
        };
        var manager = ManagerWithRegistry(new BudgetLimit { MaxCostUsd = 5m }, logs, custom);

        var answer = manager.RecordUsage(DeepSeekUsage());

        Assert.Equal(0.6m, answer.Cost);
        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    [Trait("BR", "CH-36")]
    public void ShouldWarnCannotCount_WhenOnlyAnAgentBudgetHasACostCap()
    {
        using var logs = new FakeLoggerFactory();
        var manager = Manager(crewBudget: null, logs);
        manager.SetAgentBudget(Crew, "writer", new BudgetLimit { MaxCostUsd = 5m });

        manager.RecordUsage(Usage(10, 10) with { AgentId = "writer" });

        var warnings = BudgetWarnings(logs);
        Assert.Single(warnings);
        Assert.Contains("vendor/model-x", warnings[0].Message, StringComparison.Ordinal);
        Assert.Contains("budget", warnings[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("BR", "CH-36")]
    [InlineData(0, -1, 10, 10)]
    [InlineData(-1, 0, 10, 10)]
    [InlineData(5, -1, 0, 0)]
    public void ShouldNotWarnCannotCount_WhenMaxCostIsZeroOrTheCallHasNoTokens(
        int crewCap, int agentCap, int prompt, int completion)
    {
        using var logs = new FakeLoggerFactory();
        var manager = Manager(crewCap < 0 ? null : new BudgetLimit { MaxCostUsd = crewCap }, logs);
        if (agentCap >= 0)
        {
            manager.SetAgentBudget(Crew, "writer", new BudgetLimit { MaxCostUsd = agentCap });
        }

        manager.RecordUsage(Usage(prompt, completion) with { AgentId = "writer" });

        Assert.Empty(BudgetWarnings(logs));
    }
}
