using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Risk.Contracts;
using Orkeon.Trading.Tools.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Risk;

/// <summary>
/// Stress testing tool for portfolio resilience analysis.
/// Simulates historical crisis scenarios (2008 financial crisis, 2020 COVID crash, 2022 bear market)
/// and custom shock scenarios to assess portfolio vulnerabilities.
/// </summary>
public class StressTestingTool(ILogger<StressTestingTool>? logger = null)
    : TradingToolBase<StressTestingRequest, StressTestingResponse>(logger)
{

    // Historical crisis scenarios (market moves)
    private readonly Dictionary<string, StressScenario> _historicalScenarios = new()
    {
        ["2008_financial_crisis"] = new StressScenario
        {
            Name = "2008 Financial Crisis",
            Description = "Global financial crisis, Lehman Brothers collapse",
            EquityShock = -0.37m,        // S&P 500 down 37%
            BondShock = 0.08m,           // Flight to quality
            VolatilityShock = 2.5m,      // VIX spike to 80+
            CorrelationShock = 0.25m,    // Correlations increase in crisis
            CreditSpreadShock = 0.06m,   // Credit spreads widen 600 bps
            Duration = "12 months"
        },
        ["2020_covid_crash"] = new StressScenario
        {
            Name = "2020 COVID-19 Crash",
            Description = "Pandemic-driven market crash",
            EquityShock = -0.34m,        // S&P 500 down 34% (peak to trough)
            BondShock = 0.12m,           // Treasuries rally
            VolatilityShock = 3.0m,      // VIX spike to 82
            CorrelationShock = 0.30m,    // Everything sold off together
            CreditSpreadShock = 0.04m,   // Credit spreads widen
            Duration = "1 month"
        },
        ["2022_bear_market"] = new StressScenario
        {
            Name = "2022 Bear Market",
            Description = "Fed rate hikes, inflation concerns",
            EquityShock = -0.25m,        // S&P 500 down 25%
            BondShock = -0.13m,          // Bonds down with stocks (unusual)
            VolatilityShock = 1.8m,      // Elevated VIX
            CorrelationShock = 0.15m,    // 60/40 portfolio suffered
            CreditSpreadShock = 0.02m,   // Moderate widening
            Duration = "10 months"
        },
        ["black_monday_1987"] = new StressScenario
        {
            Name = "Black Monday 1987",
            Description = "Largest single-day stock market crash",
            EquityShock = -0.23m,        // Dow down 22.6% in one day
            BondShock = 0.05m,           // Flight to quality
            VolatilityShock = 4.0m,      // Extreme volatility
            CorrelationShock = 0.20m,    // Global contagion
            CreditSpreadShock = 0.03m,
            Duration = "1 day"
        },
        ["dot_com_bubble_2000"] = new StressScenario
        {
            Name = "Dot-com Bubble Burst 2000-2002",
            Description = "Technology stock crash",
            EquityShock = -0.49m,        // NASDAQ down 49%
            BondShock = 0.10m,           // Safe haven demand
            VolatilityShock = 2.0m,      // High volatility
            CorrelationShock = 0.10m,    // Sector-specific initially
            CreditSpreadShock = 0.03m,
            Duration = "30 months"
        }
    };

    protected override string ToolId => "stress_testing";

    protected override async Task<StressTestingResponse> ExecuteTypedAsync(
        StressTestingRequest request,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Performing stress testing on portfolio value ${Value} with {Count} positions",
            request.PortfolioValue, request.PortfolioPositions.Count);

        var stressTestResults = await Task.Run(() => PerformStressTests(
            request.PortfolioPositions,
            request.PortfolioValue,
            request.Scenarios,
            request.CustomShocks), cancellationToken);

        _logger?.LogInformation("Stress testing completed");

        return stressTestResults;
    }

    private StressTestingResponse PerformStressTests(
        List<PortfolioPosition> positions,
        decimal portfolioValue,
        List<string> scenarios,
        Dictionary<string, decimal>? customShocks)
    {
        var includeAll = scenarios.Contains("all");
        var scenarioResults = new List<Dictionary<string, object>>();

        // Test historical scenarios
        foreach (var scenarioKey in _historicalScenarios.Keys)
        {
            if (includeAll || scenarios.Contains(scenarioKey))
            {
                var scenario = _historicalScenarios[scenarioKey];
                var result = SimulateScenario(positions, portfolioValue, scenario);
                scenarioResults.Add(result);
            }
        }

        // Test custom scenario if provided
        if (customShocks != null && customShocks.Count > 0)
        {
            var customScenario = new StressScenario
            {
                Name = "Custom Shock Scenario",
                Description = "User-defined custom shock scenario",
                EquityShock = customShocks.GetValueOrDefault("equity_shock", 0),
                BondShock = customShocks.GetValueOrDefault("bond_shock", 0),
                VolatilityShock = customShocks.GetValueOrDefault("volatility_shock", 1),
                CorrelationShock = customShocks.GetValueOrDefault("correlation_shock", 0),
                CreditSpreadShock = customShocks.GetValueOrDefault("credit_spread_shock", 0),
                Duration = "Custom"
            };
            var customResult = SimulateScenario(positions, portfolioValue, customScenario);
            scenarioResults.Add(customResult);
        }

        // Find worst-case scenario
        var worstCase = scenarioResults.OrderBy(r => (decimal)r["portfolio_value_after"]).First();

        // Generate recommendations
        var recommendations = GenerateRecommendations(scenarioResults, positions, portfolioValue);

        return new StressTestingResponse
        {
            PortfolioValue = portfolioValue,
            Timestamp = DateTime.UtcNow,
            ScenariosTested = scenarioResults.Count,
            ScenarioResults = scenarioResults,
            WorstCaseScenario = worstCase,
            Summary = new Dictionary<string, object>
            {
                ["average_loss_pct"] = Math.Round(scenarioResults.Average(r => (decimal)r["loss_percentage"]), 2),
                ["max_loss_pct"] = Math.Round(scenarioResults.Max(r => (decimal)r["loss_percentage"]), 2),
                ["min_loss_pct"] = Math.Round(scenarioResults.Min(r => (decimal)r["loss_percentage"]), 2),
                ["portfolio_resilience_score"] = CalculateResilienceScore(scenarioResults, portfolioValue)
            },
            Recommendations = recommendations
        };
    }

    private static Dictionary<string, object> SimulateScenario(
        List<PortfolioPosition> positions,
        decimal portfolioValue,
        StressScenario scenario)
    {
        decimal totalLoss = 0;
        var positionImpacts = new List<Dictionary<string, object>>();

        foreach (var position in positions)
        {
            var positionValue = portfolioValue * position.Weight;
            var shock = GetShockForAssetClass(position.AssetClass, scenario);
            var positionLoss = positionValue * shock;

            totalLoss += positionLoss;

            positionImpacts.Add(new Dictionary<string, object>
            {
                ["symbol"] = position.Symbol,
                ["asset_class"] = position.AssetClass,
                ["weight"] = Math.Round(position.Weight * 100, 2),
                ["value_before"] = Math.Round(positionValue, 2),
                ["shock_percentage"] = Math.Round(shock * 100, 2),
                ["loss_amount"] = Math.Round(Math.Abs(positionLoss), 2),
                ["value_after"] = Math.Round(positionValue + positionLoss, 2)
            });
        }

        var portfolioValueAfter = portfolioValue + totalLoss;
        var lossPercentage = (totalLoss / portfolioValue) * 100;

        return new Dictionary<string, object>
        {
            ["scenario_name"] = scenario.Name,
            ["description"] = scenario.Description,
            ["duration"] = scenario.Duration,
            ["portfolio_value_before"] = Math.Round(portfolioValue, 2),
            ["portfolio_value_after"] = Math.Round(portfolioValueAfter, 2),
            ["loss_amount"] = Math.Round(Math.Abs(totalLoss), 2),
            ["loss_percentage"] = Math.Round(lossPercentage, 2),
            ["position_impacts"] = positionImpacts,
            ["shocks_applied"] = new Dictionary<string, object>
            {
                ["equity_shock"] = Math.Round(scenario.EquityShock * 100, 1),
                ["bond_shock"] = Math.Round(scenario.BondShock * 100, 1),
                ["volatility_multiplier"] = Math.Round(scenario.VolatilityShock, 2),
                ["correlation_increase"] = Math.Round(scenario.CorrelationShock * 100, 1)
            }
        };
    }

    private static decimal GetShockForAssetClass(string assetClass, StressScenario scenario)
    {
        return assetClass.ToLower() switch
        {
            "equity" or "stock" or "etf" => scenario.EquityShock,
            "bond" or "fixed_income" => scenario.BondShock,
            "commodity" => scenario.EquityShock * 0.8m, // Commodities correlate with risk-off
            "real_estate" or "reit" => scenario.EquityShock * 0.7m,
            "cash" => 0m,
            "crypto" or "cryptocurrency" => scenario.EquityShock * 1.5m, // Higher volatility
            "alternative" => scenario.EquityShock * 0.5m,
            _ => scenario.EquityShock * 0.6m // Default
        };
    }

    private static decimal CalculateResilienceScore(List<Dictionary<string, object>> results, decimal portfolioValue)
    {
        // Score based on average loss and worst-case loss
        var avgLossPct = results.Average(r => Math.Abs((decimal)r["loss_percentage"]));
        var maxLossPct = results.Max(r => Math.Abs((decimal)r["loss_percentage"]));

        // Resilience score: 100 - weighted average of losses
        var score = 100 - (avgLossPct * 0.6m + maxLossPct * 0.4m);
        return Math.Max(0, Math.Round(score, 1));
    }

    private static List<string> GenerateRecommendations(
        List<Dictionary<string, object>> results,
        List<PortfolioPosition> positions,
        decimal portfolioValue)
    {
        var recommendations = new List<string>();

        var avgLossPct = results.Average(r => Math.Abs((decimal)r["loss_percentage"]));
        var maxLossPct = results.Max(r => Math.Abs((decimal)r["loss_percentage"]));

        // Analyze vulnerability
        if (maxLossPct > 40)
        {
            recommendations.Add("⚠️ CRITICAL: Portfolio could lose more than 40% in severe stress scenarios. Consider significant risk reduction.");
        }
        else if (maxLossPct > 30)
        {
            recommendations.Add("⚠️ HIGH RISK: Maximum stress loss exceeds 30%. Recommend increasing defensive positions.");
        }
        else if (maxLossPct > 20)
        {
            recommendations.Add("⚠️ MODERATE RISK: Maximum stress loss between 20-30%. Consider modest hedging strategies.");
        }
        else
        {
            recommendations.Add("✓ RESILIENT: Portfolio shows good resilience to historical stress scenarios.");
        }

        // Analyze asset class concentration
        var equityWeight = positions.Where(p => p.AssetClass.ToLower() == "equity" || p.AssetClass.ToLower() == "stock")
                                   .Sum(p => p.Weight);
        if (equityWeight > 0.8m)
        {
            recommendations.Add($"Consider diversification: Portfolio is {Math.Round(equityWeight * 100, 0)}% equities. Add bonds or alternative assets.");
        }

        // Hedging recommendations
        if (avgLossPct > 25)
        {
            recommendations.Add("Recommend implementing tail risk hedging strategies (put options, volatility hedges).");
        }

        if (maxLossPct > 35)
        {
            recommendations.Add("Consider increasing cash allocation by 10-15% to provide dry powder during crises.");
        }

        // Position-specific recommendations
        var worstPositions = results.SelectMany(r => (List<Dictionary<string, object>>)r["position_impacts"])
                                   .GroupBy(p => (string)p["symbol"])
                                   .Select(g => new
                                   {
                                       Symbol = g.Key,
                                       AvgLoss = g.Average(p => Math.Abs((decimal)p["loss_amount"]))
                                   })
                                   .OrderByDescending(p => p.AvgLoss)
                                   .Take(3)
                                   .ToList();

        if (worstPositions.Count > 0)
        {
            recommendations.Add($"Most vulnerable positions: {string.Join(", ", worstPositions.Select(p => p.Symbol))}. Consider reducing exposure or hedging.");
        }

        return recommendations;
    }

    private record StressScenario
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required decimal EquityShock { get; init; }
        public required decimal BondShock { get; init; }
        public required decimal VolatilityShock { get; init; }
        public required decimal CorrelationShock { get; init; }
        public required decimal CreditSpreadShock { get; init; }
        public required string Duration { get; init; }
    }
}
