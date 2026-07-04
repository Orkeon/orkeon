using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Tests.Security;

public class TokenBudgetTrackerTestsFixture
{
    private TokenBudgetOptions _options = new()
    {
        MaxTokensPerAgent = 10_000,
        MaxTokensPerCrew = 50_000,
        MaxCostPerCrew = 5.0m
    };
    private TokenBudgetTracker? _sut;

    // --- Fluent configuration ---

    public TokenBudgetTrackerTestsFixture WithMaxTokensPerAgent(int value)
    {
        _options.MaxTokensPerAgent = value;
        _sut = null;
        return this;
    }

    public TokenBudgetTrackerTestsFixture WithMaxTokensPerCrew(int value)
    {
        _options.MaxTokensPerCrew = value;
        _sut = null;
        return this;
    }

    public TokenBudgetTrackerTestsFixture WithMaxCostPerCrew(decimal value)
    {
        _options.MaxCostPerCrew = value;
        _sut = null;
        return this;
    }

    public TokenBudgetTrackerTestsFixture WithOptions(TokenBudgetOptions options)
    {
        _options = options;
        _sut = null;
        return this;
    }

    // --- Build / Execution ---

    public TokenBudgetTracker Build()
    {
        _sut = new TokenBudgetTracker(
            Options.Create(_options),
            NullLogger<TokenBudgetTracker>.Instance);
        return _sut;
    }

    public BudgetCheckResult RecordUsage(
        string crewId, string agentRole,
        int promptTokens, int completionTokens, string model)
    {
        var tracker = _sut ?? Build();
        return tracker.RecordUsage(crewId, agentRole, promptTokens, completionTokens, model);
    }

    public TokenUsageReport GetReport(string crewId)
    {
        var tracker = _sut ?? Build();
        return tracker.GetReport(crewId);
    }

    // --- Static helpers ---

    public static decimal EstimateCost(int promptTokens, int completionTokens, string model)
        => TokenBudgetTracker.EstimateCost(promptTokens, completionTokens, model);

    // --- Inspection ---

    public TokenBudgetTracker GetTracker() => _sut ?? Build();
}
