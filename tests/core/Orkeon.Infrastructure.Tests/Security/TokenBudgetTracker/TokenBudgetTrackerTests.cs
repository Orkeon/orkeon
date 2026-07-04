using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Configuration;
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
        var cost = TokenBudgetTrackerSut.EstimateCost(1000, 1000, ModelGpt4);
        Assert.Equal(0.09m, cost); // 0.03 + 0.06
    }

    [Fact]
    public void ShouldCalculateCorrectRates_WhenModelIsClaude3Opus()
    {
        // claude-3-opus: $0.015/1K prompt, $0.075/1K completion
        var cost = TokenBudgetTrackerSut.EstimateCost(1000, 1000, "claude-3-opus");
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
}
